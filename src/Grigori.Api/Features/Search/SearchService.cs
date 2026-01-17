using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Results;
using Grigori.Infrastructure.Chunking;
using Microsoft.Extensions.Logging;

namespace Grigori.Api.Features.Search;

public class SearchService : ISearchService
{
    private readonly IChunkRepository _chunkRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IMetricsService _metricsService;
    private readonly ILogger<SearchService> _logger;

    private const float DefaultScoreThreshold = 0.3f;
    private const float FeatureBoostFactor = 0.05f;
    private static readonly TimeSpan QueryCacheTtl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, (float[] Embedding, DateTime CachedAt)> _queryEmbeddingCache = new();

    public SearchService(
        IChunkRepository chunkRepository,
        IEmbeddingProvider embeddingProvider,
        IMetricsService metricsService,
        ILogger<SearchService> logger)
    {
        _chunkRepository = chunkRepository;
        _embeddingProvider = embeddingProvider;
        _metricsService = metricsService;
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

            var embeddingResult = await GetQueryEmbeddingAsync(request.Query, cancellationToken);
            if (embeddingResult.IsFailure)
            {
                return embeddingResult.Error;
            }

            var queryEmbedding = embeddingResult.Value;
            var queryFeatures = FeatureExtractor.ExtractFeaturesFromQuery(request.Query);

            List<string>? fileExtensions = null;
            if (!string.IsNullOrEmpty(request.FileTypes))
            {
                fileExtensions = request.FileTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ext => ext.StartsWith('.') ? ext : $".{ext}")
                    .ToList();
            }

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

            var results = searchResult.Value;

            if (queryFeatures.Count > 0)
            {
                results = results.Select(r => ApplyFeatureBoost(r, queryFeatures)).ToList();
            }

            var finalResults = results
                .OrderByDescending(r => r.Score)
                .Take(request.Limit)
                .ToList();

            stopwatch.Stop();
            _metricsService.RecordSearch(stopwatch.ElapsedMilliseconds, finalResults.Count, cacheHit, false);

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
                    TokenEstimate = finalResults.Sum(r => r.Content.Length) / 4
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
            return (DateTime.UtcNow - cached.CachedAt) < QueryCacheTtl;
        }
        return false;
    }

    public void ClearCache()
    {
        _queryEmbeddingCache.Clear();
        _logger.LogInformation("Query cache cleared");
    }

    private async Task<Result<float[], GrigoriError>> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        var cacheKey = NormalizeQueryForCacheKey(query);

        if (_queryEmbeddingCache.TryGetValue(cacheKey, out var cached))
        {
            if ((DateTime.UtcNow - cached.CachedAt) < QueryCacheTtl)
            {
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
        if (result.Features.Count == 0) return result;

        var matchingFeatures = queryFeatures.Count(qf =>
            result.Features.Any(cf => cf.Equals(qf, StringComparison.OrdinalIgnoreCase)));

        if (matchingFeatures == 0) return result;

        var boost = matchingFeatures * FeatureBoostFactor;
        return result with { Score = Math.Min(1.0f, result.Score + boost) };
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
        if (string.IsNullOrEmpty(content)) return content;
        var pattern = @"^(//\s*File:.*\r?\n|//\s*Lines:.*\r?\n)+";
        return Regex.Replace(content, pattern, "", RegexOptions.Multiline).TrimStart();
    }

    private static string ExtractFirstMeaningfulLine(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (trimmed.StartsWith("//") || trimmed.StartsWith("#") || trimmed.StartsWith("*") ||
                trimmed.StartsWith("/*") || trimmed.StartsWith("///")) continue;
            return trimmed;
        }
        return content.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;
    }
}
