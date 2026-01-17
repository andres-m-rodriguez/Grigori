using System.Collections.Concurrent;
using System.Diagnostics;
using Grigori.Core.Embeddings;
using Grigori.Core.Indexing;
using Grigori.Core.Metrics;
using Grigori.Core.Storage;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Core.Search;

public class SemanticSearch
{
    private readonly VectorStore _vectorStore;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly CodeChunker _chunker;
    private readonly ILogger<SemanticSearch> _logger;
    private readonly GrigoriOptions _options;

    private const float FeatureBoostFactor = 0.05f;
    private static readonly TimeSpan QueryCacheTtl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, (float[] Embedding, DateTime CachedAt)> _queryEmbeddingCache = new();

    public SemanticSearch(
        VectorStore vectorStore,
        IEmbeddingProvider embeddingProvider,
        CodeChunker chunker,
        ILogger<SemanticSearch> logger,
        IOptions<GrigoriOptions> options)
    {
        _vectorStore = vectorStore;
        _embeddingProvider = embeddingProvider;
        _chunker = chunker;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<List<SearchResult>> SearchAsync(
        string query,
        int limit = 5,
        List<string>? fileExtensions = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug("Searching for: {Query}", query);

        // Get query embedding from cache or generate new one
        var (queryEmbedding, cacheHit) = await GetQueryEmbeddingAsync(query, cancellationToken);

        // Extract features from query for boosting
        var queryFeatures = FeatureExtractor.ExtractFeaturesFromQuery(query);
        _logger.LogDebug("Query features: {Features}", string.Join(", ", queryFeatures));

        // Get results with score threshold and optional file extension filtering
        var results = await _vectorStore.SearchAsync(
            queryEmbedding,
            limit * 2,
            VectorStore.DefaultScoreThreshold,
            requiredFeatures: null,
            fileExtensions: fileExtensions,
            cancellationToken);

        // Apply feature boosting
        if (queryFeatures.Count > 0)
        {
            results = results.Select(r => ApplyFeatureBoost(r, queryFeatures)).ToList();
        }

        // Re-sort by boosted score and take limit
        var finalResults = results
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();

        stopwatch.Stop();
        SearchMetrics.Instance.RecordSearch(stopwatch.ElapsedMilliseconds, finalResults.Count, cacheHit, usedHnsw: false);

        _logger.LogDebug("Found {Count} results (after threshold filtering)", finalResults.Count);
        return finalResults;
    }

    private static SearchResult ApplyFeatureBoost(SearchResult result, List<string> queryFeatures)
    {
        var chunkFeatures = result.Chunk.Features;
        if (chunkFeatures.Count == 0)
            return result;

        // Count matching features
        var matchingFeatures = queryFeatures.Count(qf =>
            chunkFeatures.Any(cf => cf.Equals(qf, StringComparison.OrdinalIgnoreCase)));

        if (matchingFeatures == 0)
            return result;

        // Boost score based on matching features
        var boost = matchingFeatures * FeatureBoostFactor;
        var boostedScore = Math.Min(1.0f, result.Score + boost);

        return result with { Score = boostedScore };
    }

    private async Task<(float[] Embedding, bool CacheHit)> GetQueryEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        var cacheKey = NormalizeQueryForCacheKey(query);

        // Check if we have a cached embedding that hasn't expired
        if (_queryEmbeddingCache.TryGetValue(cacheKey, out var cached))
        {
            var age = DateTime.UtcNow - cached.CachedAt;
            if (age < QueryCacheTtl)
            {
                _logger.LogDebug("Using cached query embedding (age: {Age:F1}s)", age.TotalSeconds);
                return (cached.Embedding, CacheHit: true);
            }

            // Expired, remove from cache
            _queryEmbeddingCache.TryRemove(cacheKey, out _);
            _logger.LogDebug("Query embedding cache expired, regenerating");
        }

        // Generate new embedding with timing
        var embeddingStopwatch = Stopwatch.StartNew();
        var embedding = await _embeddingProvider.GetEmbeddingAsync(query, EmbeddingInputType.Query, cancellationToken);
        embeddingStopwatch.Stop();
        SearchMetrics.Instance.RecordEmbeddingGeneration(embeddingStopwatch.ElapsedMilliseconds, 1);

        // Cache the result
        _queryEmbeddingCache[cacheKey] = (embedding, DateTime.UtcNow);
        _logger.LogDebug("Cached new query embedding");

        return (embedding, CacheHit: false);
    }

    private static string NormalizeQueryForCacheKey(string query)
    {
        return query.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Clears all cached query embeddings.
    /// </summary>
    public void ClearQueryEmbeddingCache()
    {
        var count = _queryEmbeddingCache.Count;
        _queryEmbeddingCache.Clear();
        _logger.LogInformation("Cleared {Count} cached query embeddings", count);
    }

    /// <summary>
    /// Gets the current number of cached query embeddings.
    /// </summary>
    public int QueryEmbeddingCacheCount => _queryEmbeddingCache.Count;

    /// <summary>
    /// Checks if a query embedding is currently cached and valid.
    /// </summary>
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

    public async Task IndexFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return;
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var contentHash = VectorStore.ComputeHash(content);

        // Check if already indexed with same content
        if (await _vectorStore.HasContentHashAsync(filePath, contentHash, cancellationToken))
        {
            _logger.LogDebug("File already indexed with same content: {FilePath}", filePath);
            return;
        }

        // Delete existing chunks for this file
        await _vectorStore.DeleteByFilePathAsync(filePath, cancellationToken);

        // Chunk the file
        var chunks = _chunker.ChunkFile(filePath, content);
        if (chunks.Count == 0)
        {
            _logger.LogDebug("No chunks created for: {FilePath}", filePath);
            return;
        }

        // Get embeddings in batch using "document" input type for indexing
        var contents = chunks.Select(c => c.Content).ToList();
        var embeddingStopwatch = Stopwatch.StartNew();
        var embeddings = await _embeddingProvider.GetEmbeddingsAsync(contents, EmbeddingInputType.Document, cancellationToken);
        embeddingStopwatch.Stop();
        SearchMetrics.Instance.RecordEmbeddingGeneration(embeddingStopwatch.ElapsedMilliseconds, contents.Count);

        // Store chunks with embeddings in a single batch transaction
        await _vectorStore.InsertChunksBatchAsync(chunks, embeddings, cancellationToken);

        stopwatch.Stop();
        SearchMetrics.Instance.RecordIndexing(stopwatch.ElapsedMilliseconds, 1, chunks.Count);

        _logger.LogInformation("Indexed {ChunkCount} chunks from {FilePath}", chunks.Count, filePath);
    }

    public async Task IndexDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Directory not found: {Directory}", directory);
            return;
        }

        // Build exclude matcher from patterns
        var excludeMatcher = new Matcher();
        foreach (var pattern in _options.ExcludePatterns)
        {
            excludeMatcher.AddInclude(pattern);
        }

        var allFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
        var files = new List<string>();
        var excludedCount = 0;

        foreach (var file in allFiles)
        {
            if (!IsCodeFile(file))
                continue;

            // Get relative path for glob matching
            var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');

            // Check if file matches any exclude pattern
            if (excludeMatcher.Match(relativePath).HasMatches)
            {
                excludedCount++;
                continue;
            }

            files.Add(file);
        }

        _logger.LogInformation("Indexing {FileCount} files from {Directory} (excluded {ExcludedCount} files matching exclude patterns)",
            files.Count, directory, excludedCount);

        var totalFilesIndexed = 0;
        var totalChunksIndexed = 0;

        foreach (var file in files)
        {
            try
            {
                var chunkCount = await IndexFileAsyncInternal(file, cancellationToken);
                if (chunkCount > 0)
                {
                    totalFilesIndexed++;
                    totalChunksIndexed += chunkCount;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index file: {FilePath}", file);
            }
        }

        stopwatch.Stop();
        if (totalFilesIndexed > 0)
        {
            SearchMetrics.Instance.RecordIndexing(stopwatch.ElapsedMilliseconds, totalFilesIndexed, totalChunksIndexed);
        }

        _logger.LogInformation("Finished indexing {Directory}", directory);
    }

    public async Task RemoveFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await _vectorStore.DeleteByFilePathAsync(filePath, cancellationToken);
        _logger.LogInformation("Removed from index: {FilePath}", filePath);
    }

    /// <summary>
    /// Internal method for indexing a file without recording individual metrics.
    /// Used by IndexDirectoryAsync which records aggregated metrics.
    /// </summary>
    /// <returns>The number of chunks indexed</returns>
    private async Task<int> IndexFileAsyncInternal(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return 0;
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var contentHash = VectorStore.ComputeHash(content);

        // Check if already indexed with same content
        if (await _vectorStore.HasContentHashAsync(filePath, contentHash, cancellationToken))
        {
            _logger.LogDebug("File already indexed with same content: {FilePath}", filePath);
            return 0;
        }

        // Delete existing chunks for this file
        await _vectorStore.DeleteByFilePathAsync(filePath, cancellationToken);

        // Chunk the file
        var chunks = _chunker.ChunkFile(filePath, content);
        if (chunks.Count == 0)
        {
            _logger.LogDebug("No chunks created for: {FilePath}", filePath);
            return 0;
        }

        // Get embeddings in batch using "document" input type for indexing
        var contents = chunks.Select(c => c.Content).ToList();
        var embeddingStopwatch = Stopwatch.StartNew();
        var embeddings = await _embeddingProvider.GetEmbeddingsAsync(contents, EmbeddingInputType.Document, cancellationToken);
        embeddingStopwatch.Stop();
        SearchMetrics.Instance.RecordEmbeddingGeneration(embeddingStopwatch.ElapsedMilliseconds, contents.Count);

        // Store chunks with embeddings in a single batch transaction
        await _vectorStore.InsertChunksBatchAsync(chunks, embeddings, cancellationToken);

        _logger.LogInformation("Indexed {ChunkCount} chunks from {FilePath}", chunks.Count, filePath);
        return chunks.Count;
    }

    private bool IsCodeFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return _options.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
