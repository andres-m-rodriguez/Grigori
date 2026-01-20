using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Grigori.Infrastructure.Chunking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Mcp.Features.Search.Services;

public class SearchService : ISearchService
{
    private readonly IChunkRepository _chunkRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IMetricsService _metricsService;
    private readonly GrigoriOptions _options;
    private readonly ILogger<SearchService> _logger;

    private const float DefaultScoreThreshold = 0.3f;
    private const float FeatureBoostFactor = 0.05f;
    private const double SemanticWeight = 1.0;
    private const double LexicalWeight = 0.8;  // Slightly favor semantic results
    private static readonly TimeSpan QueryCacheTtl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, (float[] Embedding, DateTime CachedAt)> _queryEmbeddingCache = new();
    private Bm25Scorer? _bm25Scorer;
    private DateTime _bm25ScorerLastUpdate = DateTime.MinValue;
    private static readonly TimeSpan Bm25ScorerRefreshInterval = TimeSpan.FromMinutes(5);

    public SearchService(
        IChunkRepository chunkRepository,
        IEmbeddingProvider embeddingProvider,
        IMetricsService metricsService,
        IOptions<GrigoriOptions> options,
        ILogger<SearchService> logger)
    {
        _chunkRepository = chunkRepository;
        _embeddingProvider = embeddingProvider;
        _metricsService = metricsService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<SearchResultDto, GrigoriError>> SearchAsync(
        SearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return GrigoriError.InvalidQuery("Query cannot be empty");
        }

        var searchMode = (request.SearchMode ?? "hybrid").ToLowerInvariant();
        if (searchMode != "semantic" && searchMode != "lexical" && searchMode != "hybrid")
        {
            return GrigoriError.InvalidQuery($"Invalid search mode '{request.SearchMode}'. Valid options: semantic, lexical, hybrid");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cacheHit = IsQueryCached(request.Query);

            // Parse file extensions
            List<string>? fileExtensions = null;
            if (!string.IsNullOrEmpty(request.FileTypes))
            {
                fileExtensions = request.FileTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ext => ext.StartsWith('.') ? ext : $".{ext}")
                    .ToList();
            }

            List<SearchResultItem> finalResults;

            if (searchMode == "lexical")
            {
                // Pure lexical search (BM25)
                var lexicalResult = await PerformLexicalSearchAsync(request.Query, request.Limit, fileExtensions, cancellationToken);
                if (lexicalResult.IsFailure)
                    return lexicalResult.Error;
                finalResults = lexicalResult.Value;
            }
            else if (searchMode == "semantic")
            {
                // Pure semantic search using pgvector
                var semanticResult = await PerformSemanticSearchAsync(request.Query, request.Limit, fileExtensions, cancellationToken);
                if (semanticResult.IsFailure)
                    return semanticResult.Error;
                finalResults = semanticResult.Value;
            }
            else
            {
                // Hybrid search with RRF fusion
                var hybridResult = await PerformHybridSearchAsync(request.Query, request.Limit, fileExtensions, cancellationToken);
                if (hybridResult.IsFailure)
                    return hybridResult.Error;
                finalResults = hybridResult.Value;
            }

            stopwatch.Stop();
            // usedPgvector is always true now since we use PostgreSQL for vector search
            await _metricsService.RecordSearchAsync(request.Query, stopwatch.ElapsedMilliseconds, finalResults.Count, cacheHit, usedPgvector: true, cancellationToken);

            // Format results based on output mode
            var formattedResults = FormatResults(finalResults, request.OutputMode);
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                success = true,
                count = finalResults.Count,
                metrics = new
                {
                    durationMs = stopwatch.ElapsedMilliseconds,
                    cacheHit,
                    outputMode = request.OutputMode,
                    searchMode,
                    tokenEstimate = 0
                },
                results = formattedResults
            });

            var tokenEstimate = json.Length / 4;

            return new SearchResultDto
            {
                Success = true,
                Count = finalResults.Count,
                Results = finalResults.Select(r => new CodeChunkDto
                {
                    FilePath = r.FilePath,
                    StartLine = r.StartLine,
                    EndLine = r.EndLine,
                    Content = FormatContent(r.Content, request.OutputMode),
                    Score = r.Score
                }).ToList(),
                Metrics = new SearchMetricsDto
                {
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    CacheHit = cacheHit,
                    OutputMode = request.OutputMode,
                    TokenEstimate = tokenEstimate,
                    SearchMode = searchMode
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", request.Query);
            return GrigoriError.SearchFailed(request.Query, ex.Message, ex);
        }
    }

    private async Task<Result<List<SearchResultItem>, GrigoriError>> PerformSemanticSearchAsync(
        string query,
        int limit,
        List<string>? fileExtensions,
        CancellationToken cancellationToken)
    {
        // Get query embedding
        var embeddingResult = await GetQueryEmbeddingAsync(query, cancellationToken);
        if (embeddingResult.IsFailure)
            return embeddingResult.Error;

        var queryEmbedding = embeddingResult.Value;
        var queryFeatures = FeatureExtractor.ExtractFeaturesFromQuery(query);

        // Use pgvector for similarity search via the repository
        var searchResult = await _chunkRepository.SearchAsync(
            queryEmbedding,
            limit * 2,
            DefaultScoreThreshold,
            requiredFeatures: null,
            fileExtensions: fileExtensions,
            cancellationToken);

        if (searchResult.IsFailure)
            return searchResult.Error;

        var results = searchResult.Value;

        // Apply feature boosting
        if (queryFeatures.Count > 0)
        {
            results = results.Select(r => ApplyFeatureBoost(r, queryFeatures)).ToList();
        }

        // Re-sort and take limit
        var finalResults = results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();

        return finalResults;
    }

    private async Task<Result<List<SearchResultItem>, GrigoriError>> PerformLexicalSearchAsync(
        string query,
        int limit,
        List<string>? fileExtensions,
        CancellationToken cancellationToken)
    {
        // Get chunks for lexical search
        var chunksResult = await _chunkRepository.GetChunksForLexicalSearchAsync(fileExtensions, cancellationToken);
        if (chunksResult.IsFailure)
            return chunksResult.Error;

        var chunks = chunksResult.Value;
        if (chunks.Count == 0)
            return new List<SearchResultItem>();

        // Initialize or refresh BM25 scorer
        await RefreshBm25ScorerIfNeededAsync(chunks, cancellationToken);

        // Score chunks with BM25
        var scoredChunks = _bm25Scorer!
            .ScoreDocuments(query, chunks.Select(c => (c.Id, c.Content)))
            .Take(limit)
            .ToList();

        // Normalize scores to 0-1 range
        var normalizedScores = Bm25Scorer.NormalizeScores(scoredChunks).ToDictionary(x => x.Id, x => x.Score);

        // Build result items
        var chunkLookup = chunks.ToDictionary(c => c.Id);
        var results = scoredChunks
            .Where(s => chunkLookup.ContainsKey(s.Id))
            .Select(s =>
            {
                var chunk = chunkLookup[s.Id];
                return new SearchResultItem
                {
                    Id = chunk.Id,
                    FilePath = chunk.FilePath,
                    StartLine = chunk.StartLine,
                    EndLine = chunk.EndLine,
                    Content = chunk.Content,
                    Score = (float)normalizedScores.GetValueOrDefault(s.Id, 0),
                    Features = chunk.Features
                };
            })
            .ToList();

        return results;
    }

    private async Task<Result<List<SearchResultItem>, GrigoriError>> PerformHybridSearchAsync(
        string query,
        int limit,
        List<string>? fileExtensions,
        CancellationToken cancellationToken)
    {
        // Run semantic and lexical searches in parallel
        var semanticTask = PerformSemanticSearchAsync(query, limit * 2, fileExtensions, cancellationToken);
        var lexicalTask = PerformLexicalSearchAsync(query, limit * 2, fileExtensions, cancellationToken);

        await Task.WhenAll(semanticTask, lexicalTask);

        var semanticResult = await semanticTask;
        var lexicalResult = await lexicalTask;

        if (semanticResult.IsFailure && lexicalResult.IsFailure)
            return semanticResult.Error;

        var semanticResults = semanticResult.IsSuccess ? semanticResult.Value : [];
        var lexicalResults = lexicalResult.IsSuccess ? lexicalResult.Value : [];

        _logger.LogDebug(
            "Hybrid search: {SemanticCount} semantic results, {LexicalCount} lexical results",
            semanticResults.Count,
            lexicalResults.Count);

        // If only one search type returned results, use that
        if (semanticResults.Count == 0 && lexicalResults.Count > 0)
            return lexicalResults.Take(limit).ToList();
        if (lexicalResults.Count == 0 && semanticResults.Count > 0)
            return semanticResults.Take(limit).ToList();
        if (semanticResults.Count == 0 && lexicalResults.Count == 0)
            return new List<SearchResultItem>();

        // Fuse results using RRF
        var fusedResults = ReciprocalRankFusion.FuseWithScores(
            semanticResults.Select(r => (r.Id, r.Score)),
            lexicalResults.Select(r => (r.Id, (double)r.Score)),
            SemanticWeight,
            LexicalWeight);

        // Build lookup for chunk data
        var allChunks = semanticResults
            .Concat(lexicalResults)
            .DistinctBy(r => r.Id)
            .ToDictionary(r => r.Id);

        // Map fused scores back to SearchResultItems
        var finalResults = fusedResults
            .Where(f => allChunks.ContainsKey(f.Id))
            .Take(limit)
            .Select(f =>
            {
                var chunk = allChunks[f.Id];
                // Use the higher of the two original scores, or a combination
                var combinedScore = f.InBothLists
                    ? Math.Max(f.SemanticScore, (float)f.LexicalScore) * 1.1f  // Bonus for appearing in both
                    : Math.Max(f.SemanticScore, (float)f.LexicalScore);
                return chunk with { Score = Math.Min(1.0f, combinedScore) };
            })
            .ToList();

        return finalResults;
    }

    private Task RefreshBm25ScorerIfNeededAsync(List<ChunkForLexicalSearch> chunks, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_bm25Scorer == null || (now - _bm25ScorerLastUpdate) > Bm25ScorerRefreshInterval)
        {
            _logger.LogDebug("Refreshing BM25 scorer with {Count} documents", chunks.Count);
            _bm25Scorer = new Bm25Scorer(chunks.Select(c => c.Content));
            _bm25ScorerLastUpdate = now;
        }
        return Task.CompletedTask;
    }

    public bool IsQueryCached(string query)
    {
        var cacheKey = NormalizeQueryForCacheKey(query);
        if (_queryEmbeddingCache.TryGetValue(cacheKey, out var cached))
        {
            var age = DateTime.UtcNow - cached.CachedAt;
            return age < QueryCacheTtl;
        }
        return false;
    }

    public void ClearCache()
    {
        var count = _queryEmbeddingCache.Count;
        _queryEmbeddingCache.Clear();
        _logger.LogInformation("Cleared {Count} cached query embeddings", count);
    }

    private async Task<Result<float[], GrigoriError>> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        var cacheKey = NormalizeQueryForCacheKey(query);

        if (_queryEmbeddingCache.TryGetValue(cacheKey, out var cached))
        {
            var age = DateTime.UtcNow - cached.CachedAt;
            if (age < QueryCacheTtl)
            {
                _logger.LogDebug("Using cached query embedding (age: {Age:F1}s)", age.TotalSeconds);
                return cached.Embedding;
            }

            _queryEmbeddingCache.TryRemove(cacheKey, out _);
        }

        var embeddingStopwatch = Stopwatch.StartNew();
        var embeddingResult = await _embeddingProvider.GetEmbeddingAsync(query, EmbeddingInputType.Query, cancellationToken);
        embeddingStopwatch.Stop();

        if (embeddingResult.IsFailure)
        {
            return embeddingResult.Error;
        }

        _metricsService.RecordEmbeddingGeneration(embeddingStopwatch.ElapsedMilliseconds, 1);
        _queryEmbeddingCache[cacheKey] = (embeddingResult.Value, DateTime.UtcNow);

        return embeddingResult.Value;
    }

    private static string NormalizeQueryForCacheKey(string query) => query.Trim().ToLowerInvariant();

    private static SearchResultItem ApplyFeatureBoost(SearchResultItem result, List<string> queryFeatures)
    {
        if (result.Features.Count == 0)
            return result;

        var matchingFeatures = queryFeatures.Count(qf =>
            result.Features.Any(cf => cf.Equals(qf, StringComparison.OrdinalIgnoreCase)));

        if (matchingFeatures == 0)
            return result;

        var boost = matchingFeatures * FeatureBoostFactor;
        var boostedScore = Math.Min(1.0f, result.Score + boost);

        return result with { Score = boostedScore };
    }

    private static object FormatResults(List<SearchResultItem> results, string outputMode)
    {
        return outputMode.ToLowerInvariant() switch
        {
            "paths" => results.Select(r => new
            {
                filePath = r.FilePath,
                startLine = r.StartLine,
                endLine = r.EndLine,
                score = r.Score
            }).ToList(),

            "summary" => results.Select(r => new
            {
                filePath = r.FilePath,
                startLine = r.StartLine,
                endLine = r.EndLine,
                content = ExtractFirstMeaningfulLine(r.Content),
                score = r.Score
            }).ToList(),

            "compact" => results.Select(r => new
            {
                filePath = r.FilePath,
                startLine = r.StartLine,
                endLine = r.EndLine,
                content = StripContextPrefix(r.Content),
                score = r.Score
            }).ToList(),

            _ => results.Select(r => new
            {
                filePath = r.FilePath,
                startLine = r.StartLine,
                endLine = r.EndLine,
                content = r.Content,
                score = r.Score
            }).ToList()
        };
    }

    private static string FormatContent(string content, string outputMode)
    {
        return outputMode.ToLowerInvariant() switch
        {
            "summary" => ExtractFirstMeaningfulLine(content),
            "compact" => StripContextPrefix(content),
            _ => content
        };
    }

    private static string StripContextPrefix(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var pattern = @"^(//\s*File:.*\r?\n|//\s*Lines:.*\r?\n)+";
        return Regex.Replace(content, pattern, "", RegexOptions.Multiline).TrimStart();
    }

    private static string ExtractFirstMeaningfulLine(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.StartsWith("//") ||
                trimmed.StartsWith("#") ||
                trimmed.StartsWith("*") ||
                trimmed.StartsWith("/*") ||
                trimmed.StartsWith("'''") ||
                trimmed.StartsWith("\"\"\"") ||
                trimmed.StartsWith("///"))
                continue;

            return trimmed;
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed;
        }

        return string.Empty;
    }
}
