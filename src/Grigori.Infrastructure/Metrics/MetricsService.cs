using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Interfaces;
using Grigori.Database;
using Grigori.Database.Entities;
using Microsoft.Extensions.Logging;

namespace Grigori.Infrastructure.Metrics;

public class MetricsService : IMetricsService
{
    private readonly DateTime _serverStartTime;
    private readonly object _activityLock = new();
    private readonly LinkedList<ActivityEvent> _recentActivity = new();
    private const int MaxActivityEvents = 1000;
    private readonly GrigoriDbContext _dbContext;
    private readonly ILogger<MetricsService> _logger;

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

    public MetricsService(GrigoriDbContext dbContext, ILogger<MetricsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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

    public void RecordFileActivity(string filePath, string projectName, int chunksCreated)
    {
        var activity = new ActivityEvent(DateTime.UtcNow, filePath, projectName, chunksCreated);
        lock (_activityLock)
        {
            _recentActivity.AddFirst(activity);
            while (_recentActivity.Count > MaxActivityEvents)
                _recentActivity.RemoveLast();
        }
    }

    public List<ActivityEvent> GetRecentActivity(int count = 50)
    {
        lock (_activityLock)
        {
            return _recentActivity.Take(count).ToList();
        }
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

        lock (_activityLock)
        {
            _recentActivity.Clear();
        }
    }

    public async Task RecordSearchAsync(string query, long durationMs, int resultCount, bool cacheHit, bool usedHnsw, CancellationToken cancellationToken = default)
    {
        // Record in-memory metrics first
        RecordSearch(durationMs, resultCount, cacheHit, usedHnsw);

        // Persist to database
        try
        {
            var entity = new SearchHistory
            {
                Query = query,
                ResultCount = resultCount,
                DurationMs = durationMs,
                CacheHit = cacheHit,
                UsedHnsw = usedHnsw,
                Timestamp = DateTime.UtcNow
            };

            _dbContext.SearchHistories.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist search history for query: {Query}", query);
        }
    }

    public async Task RecordIndexingAsync(string projectPath, long durationMs, int fileCount, int chunkCount, CancellationToken cancellationToken = default)
    {
        // Record in-memory metrics first
        RecordIndexing(durationMs, fileCount, chunkCount);

        // Persist to database
        try
        {
            var projectName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var description = $"Indexed {fileCount} files ({chunkCount} chunks) from {projectName}";
            var details = System.Text.Json.JsonSerializer.Serialize(new
            {
                ProjectPath = projectPath,
                FileCount = fileCount,
                ChunkCount = chunkCount
            });

            var entity = new ActivityLog
            {
                ActivityType = "indexing",
                Description = description,
                DurationMs = durationMs,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            _dbContext.ActivityLogs.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist indexing activity for path: {Path}", projectPath);
        }
    }
}
