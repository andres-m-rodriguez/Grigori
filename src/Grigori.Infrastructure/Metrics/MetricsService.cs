using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Interfaces;

namespace Grigori.Infrastructure.Metrics;

public class MetricsService : IMetricsService
{
    private readonly DateTime _serverStartTime;

    private long _totalSearches;
    private long _totalSearchTimeMs;
    private long _totalResultsReturned;
    private long _cacheHits;
    private long _cacheMisses;
    private long _hnswSearches;
    private long _nonHnswSearches;

    private long _totalIndexingTimeMs;
    private long _totalFilesIndexed;
    private long _totalChunksIndexed;
    private long _totalIndexingOperations;

    private long _totalEmbeddingsGenerated;
    private long _totalEmbeddingTimeMs;

    public MetricsService()
    {
        _serverStartTime = DateTime.UtcNow;
    }

    public void RecordSearch(long durationMs, int resultCount, bool cacheHit, bool usedHnsw)
    {
        Interlocked.Increment(ref _totalSearches);
        Interlocked.Add(ref _totalSearchTimeMs, durationMs);
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

    public void RecordIndexing(long durationMs, int fileCount, int chunkCount)
    {
        Interlocked.Increment(ref _totalIndexingOperations);
        Interlocked.Add(ref _totalIndexingTimeMs, durationMs);
        Interlocked.Add(ref _totalFilesIndexed, fileCount);
        Interlocked.Add(ref _totalChunksIndexed, chunkCount);
    }

    public void RecordEmbeddingGeneration(long durationMs, int count)
    {
        Interlocked.Add(ref _totalEmbeddingsGenerated, count);
        Interlocked.Add(ref _totalEmbeddingTimeMs, durationMs);
    }

    public MetricsSnapshotDto GetSnapshot()
    {
        var totalSearches = Interlocked.Read(ref _totalSearches);
        var totalSearchTimeMs = Interlocked.Read(ref _totalSearchTimeMs);
        var totalResultsReturned = Interlocked.Read(ref _totalResultsReturned);
        var cacheHits = Interlocked.Read(ref _cacheHits);
        var cacheMisses = Interlocked.Read(ref _cacheMisses);
        var hnswSearches = Interlocked.Read(ref _hnswSearches);

        var totalIndexingTimeMs = Interlocked.Read(ref _totalIndexingTimeMs);
        var totalFilesIndexed = Interlocked.Read(ref _totalFilesIndexed);
        var totalChunksIndexed = Interlocked.Read(ref _totalChunksIndexed);
        var totalIndexingOperations = Interlocked.Read(ref _totalIndexingOperations);

        var totalEmbeddingsGenerated = Interlocked.Read(ref _totalEmbeddingsGenerated);
        var totalEmbeddingTimeMs = Interlocked.Read(ref _totalEmbeddingTimeMs);

        var uptime = DateTime.UtcNow - _serverStartTime;

        return new MetricsSnapshotDto
        {
            Uptime = uptime,
            SearchStats = new SearchStatsDto
            {
                TotalSearches = totalSearches,
                CacheHits = cacheHits,
                CacheMisses = cacheMisses,
                HnswSearches = hnswSearches,
                AverageTimeMs = totalSearches > 0 ? Math.Round((double)totalSearchTimeMs / totalSearches, 2) : 0,
                AverageResultCount = totalSearches > 0 ? Math.Round((double)totalResultsReturned / totalSearches, 2) : 0
            },
            IndexingStats = new IndexingStatsDto
            {
                TotalOperations = totalIndexingOperations,
                FilesIndexed = totalFilesIndexed,
                ChunksCreated = totalChunksIndexed,
                TotalTimeMs = totalIndexingTimeMs
            },
            EmbeddingStats = new EmbeddingStatsDto
            {
                TotalEmbeddings = totalEmbeddingsGenerated,
                TotalTimeMs = totalEmbeddingTimeMs,
                AverageTimeMs = totalEmbeddingsGenerated > 0 ? Math.Round((double)totalEmbeddingTimeMs / totalEmbeddingsGenerated, 2) : 0
            }
        };
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalSearches, 0);
        Interlocked.Exchange(ref _totalSearchTimeMs, 0);
        Interlocked.Exchange(ref _totalResultsReturned, 0);
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _hnswSearches, 0);
        Interlocked.Exchange(ref _nonHnswSearches, 0);

        Interlocked.Exchange(ref _totalIndexingTimeMs, 0);
        Interlocked.Exchange(ref _totalFilesIndexed, 0);
        Interlocked.Exchange(ref _totalChunksIndexed, 0);
        Interlocked.Exchange(ref _totalIndexingOperations, 0);

        Interlocked.Exchange(ref _totalEmbeddingsGenerated, 0);
        Interlocked.Exchange(ref _totalEmbeddingTimeMs, 0);
    }
}
