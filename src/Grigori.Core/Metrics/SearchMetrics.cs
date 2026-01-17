using System.Threading;

namespace Grigori.Core.Metrics;

/// <summary>
/// Singleton service that tracks performance metrics for semantic search operations.
/// Uses thread-safe Interlocked operations for concurrent access.
/// </summary>
public sealed class SearchMetrics
{
    private static readonly Lazy<SearchMetrics> _instance = new(() => new SearchMetrics());

    /// <summary>
    /// Gets the singleton instance of the SearchMetrics service.
    /// </summary>
    public static SearchMetrics Instance => _instance.Value;

    private readonly DateTime _serverStartTime;

    // Search metrics - using long for Interlocked compatibility
    private long _totalSearches;
    private long _totalSearchTimeMs;
    private long _totalResultsReturned;
    private long _cacheHits;
    private long _cacheMisses;
    private long _hnswSearches;
    private long _nonHnswSearches;

    // Indexing metrics
    private long _totalIndexingTimeMs;
    private long _totalFilesIndexed;
    private long _totalChunksIndexed;
    private long _totalIndexingOperations;

    // Embedding metrics
    private long _totalEmbeddingsGenerated;
    private long _totalEmbeddingTimeMs;

    /// <summary>
    /// Private constructor for singleton pattern.
    /// </summary>
    private SearchMetrics()
    {
        _serverStartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a search operation with its timing and characteristics.
    /// Thread-safe using Interlocked operations.
    /// </summary>
    /// <param name="durationMs">Time taken for the search in milliseconds.</param>
    /// <param name="resultCount">Number of results returned.</param>
    /// <param name="cacheHit">Whether the query embedding was retrieved from cache.</param>
    /// <param name="usedHnsw">Whether HNSW index was used for the search.</param>
    public void RecordSearch(double durationMs, int resultCount, bool cacheHit, bool usedHnsw)
    {
        Interlocked.Increment(ref _totalSearches);
        Interlocked.Add(ref _totalSearchTimeMs, (long)durationMs);
        Interlocked.Add(ref _totalResultsReturned, resultCount);

        if (cacheHit)
            Interlocked.Increment(ref _cacheHits);
        else
            Interlocked.Increment(ref _cacheMisses);

        if (usedHnsw)
            Interlocked.Increment(ref _hnswSearches);
        else
            Interlocked.Increment(ref _nonHnswSearches);
    }

    /// <summary>
    /// Records an indexing operation with file and chunk counts.
    /// Thread-safe using Interlocked operations.
    /// </summary>
    /// <param name="durationMs">Time taken for the indexing operation in milliseconds.</param>
    /// <param name="fileCount">Number of files indexed.</param>
    /// <param name="chunkCount">Number of chunks created.</param>
    public void RecordIndexing(double durationMs, int fileCount, int chunkCount)
    {
        Interlocked.Increment(ref _totalIndexingOperations);
        Interlocked.Add(ref _totalIndexingTimeMs, (long)durationMs);
        Interlocked.Add(ref _totalFilesIndexed, fileCount);
        Interlocked.Add(ref _totalChunksIndexed, chunkCount);
    }

    /// <summary>
    /// Records embedding generation with timing.
    /// Thread-safe using Interlocked operations.
    /// </summary>
    /// <param name="durationMs">Time taken for embedding generation in milliseconds.</param>
    /// <param name="count">Number of embeddings generated.</param>
    public void RecordEmbeddingGeneration(double durationMs, int count)
    {
        Interlocked.Add(ref _totalEmbeddingsGenerated, count);
        Interlocked.Add(ref _totalEmbeddingTimeMs, (long)durationMs);
    }

    /// <summary>
    /// Gets a snapshot of the current metrics with all computed values.
    /// Thread-safe read of all metrics.
    /// </summary>
    /// <returns>An immutable MetricsSnapshot record with all computed metrics.</returns>
    public MetricsSnapshot GetSnapshot()
    {
        // Read all values atomically
        var totalSearches = Interlocked.Read(ref _totalSearches);
        var totalSearchTimeMs = Interlocked.Read(ref _totalSearchTimeMs);
        var totalResultsReturned = Interlocked.Read(ref _totalResultsReturned);
        var cacheHits = Interlocked.Read(ref _cacheHits);
        var cacheMisses = Interlocked.Read(ref _cacheMisses);
        var hnswSearches = Interlocked.Read(ref _hnswSearches);
        var nonHnswSearches = Interlocked.Read(ref _nonHnswSearches);

        var totalIndexingTimeMs = Interlocked.Read(ref _totalIndexingTimeMs);
        var totalFilesIndexed = Interlocked.Read(ref _totalFilesIndexed);
        var totalChunksIndexed = Interlocked.Read(ref _totalChunksIndexed);
        var totalIndexingOperations = Interlocked.Read(ref _totalIndexingOperations);

        var totalEmbeddingsGenerated = Interlocked.Read(ref _totalEmbeddingsGenerated);
        var totalEmbeddingTimeMs = Interlocked.Read(ref _totalEmbeddingTimeMs);

        // Calculate derived metrics
        var totalCacheAttempts = cacheHits + cacheMisses;
        var totalHnswAttempts = hnswSearches + nonHnswSearches;
        var uptime = DateTime.UtcNow - _serverStartTime;

        return new MetricsSnapshot
        {
            // Search metrics
            TotalSearches = totalSearches,
            AverageSearchTimeMs = totalSearches > 0
                ? Math.Round((double)totalSearchTimeMs / totalSearches, 2)
                : 0,
            AverageResultsReturned = totalSearches > 0
                ? Math.Round((double)totalResultsReturned / totalSearches, 2)
                : 0,

            // Cache metrics
            CacheHits = cacheHits,
            CacheMisses = cacheMisses,
            CacheHitRate = totalCacheAttempts > 0
                ? Math.Round((double)cacheHits / totalCacheAttempts, 4)
                : 0,

            // HNSW metrics
            HnswSearches = hnswSearches,
            HnswIndexUsageRate = totalHnswAttempts > 0
                ? Math.Round((double)hnswSearches / totalHnswAttempts, 4)
                : 0,

            // Indexing metrics
            TotalIndexingOperations = totalIndexingOperations,
            TotalIndexingTimeMs = totalIndexingTimeMs,
            FilesIndexedCount = totalFilesIndexed,
            ChunksIndexedCount = totalChunksIndexed,
            AverageChunksPerFile = totalFilesIndexed > 0
                ? Math.Round((double)totalChunksIndexed / totalFilesIndexed, 2)
                : 0,

            // Embedding metrics
            EmbeddingsGeneratedCount = totalEmbeddingsGenerated,
            TotalEmbeddingTimeMs = totalEmbeddingTimeMs,
            AverageEmbeddingTimeMs = totalEmbeddingsGenerated > 0
                ? Math.Round((double)totalEmbeddingTimeMs / totalEmbeddingsGenerated, 2)
                : 0,

            // Server metrics
            ServerStartTime = _serverStartTime,
            Uptime = uptime,
            UptimeFormatted = FormatUptime(uptime)
        };
    }

    /// <summary>
    /// Resets all metrics to zero. Server start time remains unchanged.
    /// Thread-safe using Interlocked operations.
    /// </summary>
    public void Reset()
    {
        // Reset search metrics
        Interlocked.Exchange(ref _totalSearches, 0);
        Interlocked.Exchange(ref _totalSearchTimeMs, 0);
        Interlocked.Exchange(ref _totalResultsReturned, 0);
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _hnswSearches, 0);
        Interlocked.Exchange(ref _nonHnswSearches, 0);

        // Reset indexing metrics
        Interlocked.Exchange(ref _totalIndexingTimeMs, 0);
        Interlocked.Exchange(ref _totalFilesIndexed, 0);
        Interlocked.Exchange(ref _totalChunksIndexed, 0);
        Interlocked.Exchange(ref _totalIndexingOperations, 0);

        // Reset embedding metrics
        Interlocked.Exchange(ref _totalEmbeddingsGenerated, 0);
        Interlocked.Exchange(ref _totalEmbeddingTimeMs, 0);
    }

    /// <summary>
    /// Formats a TimeSpan as a human-readable uptime string.
    /// </summary>
    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
        if (uptime.TotalMinutes >= 1)
        {
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        }
        return $"{uptime.Seconds}s";
    }
}

/// <summary>
/// Immutable snapshot of all metrics at a point in time.
/// Contains both raw totals and computed averages/rates.
/// </summary>
public record MetricsSnapshot
{
    // Search metrics
    /// <summary>Total number of searches performed.</summary>
    public long TotalSearches { get; init; }

    /// <summary>Average search time in milliseconds.</summary>
    public double AverageSearchTimeMs { get; init; }

    /// <summary>Average number of results returned per search.</summary>
    public double AverageResultsReturned { get; init; }

    // Cache metrics
    /// <summary>Total number of cache hits.</summary>
    public long CacheHits { get; init; }

    /// <summary>Total number of cache misses.</summary>
    public long CacheMisses { get; init; }

    /// <summary>Cache hit rate (0.0 to 1.0).</summary>
    public double CacheHitRate { get; init; }

    // HNSW metrics
    /// <summary>Total number of searches using HNSW index.</summary>
    public long HnswSearches { get; init; }

    /// <summary>HNSW index usage rate (0.0 to 1.0).</summary>
    public double HnswIndexUsageRate { get; init; }

    // Indexing metrics
    /// <summary>Total number of indexing operations performed.</summary>
    public long TotalIndexingOperations { get; init; }

    /// <summary>Total time spent indexing in milliseconds.</summary>
    public long TotalIndexingTimeMs { get; init; }

    /// <summary>Total number of files indexed.</summary>
    public long FilesIndexedCount { get; init; }

    /// <summary>Total number of chunks indexed.</summary>
    public long ChunksIndexedCount { get; init; }

    /// <summary>Average number of chunks per indexed file.</summary>
    public double AverageChunksPerFile { get; init; }

    // Embedding metrics
    /// <summary>Total number of embeddings generated.</summary>
    public long EmbeddingsGeneratedCount { get; init; }

    /// <summary>Total time spent generating embeddings in milliseconds.</summary>
    public long TotalEmbeddingTimeMs { get; init; }

    /// <summary>Average time per embedding generation in milliseconds.</summary>
    public double AverageEmbeddingTimeMs { get; init; }

    // Server metrics
    /// <summary>Server start time in UTC.</summary>
    public DateTime ServerStartTime { get; init; }

    /// <summary>Server uptime as a TimeSpan.</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>Server uptime formatted as a human-readable string.</summary>
    public string UptimeFormatted { get; init; } = string.Empty;
}
