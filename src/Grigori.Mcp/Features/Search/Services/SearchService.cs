using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Grigori.Infrastructure.Chunking;
using Grigori.Infrastructure.Indexing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Mcp.Features.Search.Services;

public class SearchService : ISearchService
{
    private readonly IChunkRepository _chunkRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IMetricsService _metricsService;
    private readonly HnswIndex _hnswIndex;
    private readonly GrigoriOptions _options;
    private readonly ILogger<SearchService> _logger;

    private const float DefaultScoreThreshold = 0.3f;
    private const float FeatureBoostFactor = 0.05f;
    private static readonly TimeSpan QueryCacheTtl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, (float[] Embedding, DateTime CachedAt)> _queryEmbeddingCache = new();

    public SearchService(
        IChunkRepository chunkRepository,
        IEmbeddingProvider embeddingProvider,
        IMetricsService metricsService,
        HnswIndex hnswIndex,
        IOptions<GrigoriOptions> options,
        ILogger<SearchService> logger)
    {
        _chunkRepository = chunkRepository;
        _embeddingProvider = embeddingProvider;
        _metricsService = metricsService;
        _hnswIndex = hnswIndex;
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

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cacheHit = IsQueryCached(request.Query);

            // Get query embedding
            var embeddingResult = await GetQueryEmbeddingAsync(request.Query, cancellationToken);
            if (embeddingResult.IsFailure)
            {
                return embeddingResult.Error;
            }

            var queryEmbedding = embeddingResult.Value;
            var queryFeatures = FeatureExtractor.ExtractFeaturesFromQuery(request.Query);

            // Parse file extensions
            List<string>? fileExtensions = null;
            if (!string.IsNullOrEmpty(request.FileTypes))
            {
                fileExtensions = request.FileTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ext => ext.StartsWith('.') ? ext : $".{ext}")
                    .ToList();
            }

            // Try HNSW search first if enabled and index is built
            var usedHnsw = false;
            List<SearchResultItem> results;

            if (_options.Hnsw.Enabled && _hnswIndex.IsBuilt && fileExtensions == null)
            {
                // Use HNSW for approximate nearest neighbor search
                var hnswResults = _hnswIndex.Search(queryEmbedding, request.Limit * 2);

                if (hnswResults.Count > 0)
                {
                    usedHnsw = true;
                    var candidateIds = hnswResults
                        .Where(r => HnswIndex.DistanceToSimilarity(r.Distance) >= DefaultScoreThreshold)
                        .Select(r => r.Id)
                        .ToList();

                    var chunksResult = await _chunkRepository.GetChunksByIdsAsync(candidateIds, cancellationToken);
                    if (chunksResult.IsFailure)
                    {
                        return chunksResult.Error;
                    }

                    // Create a lookup for HNSW distances
                    var distanceLookup = hnswResults.ToDictionary(r => r.Id, r => r.Distance);

                    // Set scores from HNSW distances
                    results = chunksResult.Value.Select(r => r with
                    {
                        Score = distanceLookup.TryGetValue(r.Id, out var distance)
                            ? HnswIndex.DistanceToSimilarity(distance)
                            : r.Score
                    }).ToList();

                    _logger.LogDebug("HNSW search returned {Count} results", results.Count);
                }
                else
                {
                    results = [];
                }
            }
            else
            {
                // Fall back to linear scan (required when filtering by file extensions)
                var searchResult = await _chunkRepository.SearchAsync(
                    queryEmbedding,
                    request.Limit * 2,
                    DefaultScoreThreshold,
                    requiredFeatures: null,
                    fileExtensions: fileExtensions,
                    cancellationToken);

                if (searchResult.IsFailure)
                {
                    return searchResult.Error;
                }

                results = searchResult.Value;
            }

            // Apply feature boosting
            if (queryFeatures.Count > 0)
            {
                results = results.Select(r => ApplyFeatureBoost(r, queryFeatures)).ToList();
            }

            // Re-sort and take limit
            var finalResults = results
                .OrderByDescending(r => r.Score)
                .Take(request.Limit)
                .ToList();

            stopwatch.Stop();
            await _metricsService.RecordSearchAsync(request.Query, stopwatch.ElapsedMilliseconds, finalResults.Count, cacheHit, usedHnsw, cancellationToken);

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
                    TokenEstimate = tokenEstimate
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", request.Query);
            return GrigoriError.SearchFailed(request.Query, ex.Message, ex);
        }
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
