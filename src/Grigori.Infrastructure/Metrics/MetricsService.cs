using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace Grigori.Infrastructure.Metrics;

public class MetricsService : IMetricsService
{
    private readonly DateTime _serverStartTime;
    private readonly object _activityLock = new();
    private readonly LinkedList<ActivityEvent> _recentActivity = new();
    private const int MaxActivityEvents = 1000;
    private readonly IMetricsRepository _metricsRepository;
    private readonly ILogger<MetricsService> _logger;

    private long _totalSearches;
    private long _totalSearchTimeMs;
    private long _totalResultsReturned;
    private long _cacheHits;
    private long _cacheMisses;
    private long _vectorSearches;
    private long _nonVectorSearches;

    private long _totalIndexingTimeMs;
    private long _totalFilesIndexed;
    private long _totalChunksIndexed;
    private long _totalIndexingOperations;

    private long _totalEmbeddingsGenerated;
    private long _totalEmbeddingTimeMs;

    public MetricsService(IMetricsRepository metricsRepository, ILogger<MetricsService> logger)
    {
        _metricsRepository = metricsRepository;
        _logger = logger;
        _serverStartTime = DateTime.UtcNow;
    }

    public void RecordSearch(long durationMs, int resultCount, bool cacheHit, bool usedVectorSearch)
    {
        Interlocked.Increment(ref _totalSearches);
        Interlocked.Add(ref _totalSearchTimeMs, durationMs);
        Interlocked.Add(ref _totalResultsReturned, resultCount);

        if (cacheHit)
            Interlocked.Increment(ref _cacheHits);
        else
            Interlocked.Increment(ref _cacheMisses);

        if (usedVectorSearch)
            Interlocked.Increment(ref _vectorSearches);
        else
            Interlocked.Increment(ref _nonVectorSearches);
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
        var vectorSearches = Interlocked.Read(ref _vectorSearches);

        var totalIndexingTimeMs = Interlocked.Read(ref _totalIndexingTimeMs);
        var totalFilesIndexed = Interlocked.Read(ref _totalFilesIndexed);
        var totalChunksIndexed = Interlocked.Read(ref _totalChunksIndexed);
        var totalIndexingOperations = Interlocked.Read(ref _totalIndexingOperations);

        var totalEmbeddingsGenerated = Interlocked.Read(ref _totalEmbeddingsGenerated);
        var totalEmbeddingTimeMs = Interlocked.Read(ref _totalEmbeddingTimeMs);

        var uptime = DateTime.UtcNow - _serverStartTime;

        return new MetricsSnapshotDto(
            uptime,
            new SearchStatsDto(
                totalSearches,
                cacheHits,
                cacheMisses,
                vectorSearches,
                totalSearches > 0 ? Math.Round((double)totalSearchTimeMs / totalSearches, 2) : 0,
                totalSearches > 0 ? Math.Round((double)totalResultsReturned / totalSearches, 2) : 0),
            new IndexingStatsDto(
                totalIndexingOperations,
                totalFilesIndexed,
                totalChunksIndexed,
                totalIndexingTimeMs),
            new EmbeddingStatsDto(
                totalEmbeddingsGenerated,
                totalEmbeddingTimeMs,
                totalEmbeddingsGenerated > 0 ? Math.Round((double)totalEmbeddingTimeMs / totalEmbeddingsGenerated, 2) : 0));
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalSearches, 0);
        Interlocked.Exchange(ref _totalSearchTimeMs, 0);
        Interlocked.Exchange(ref _totalResultsReturned, 0);
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _vectorSearches, 0);
        Interlocked.Exchange(ref _nonVectorSearches, 0);

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

    public async Task RecordSearchAsync(string query, long durationMs, int resultCount, bool cacheHit, bool usedVectorSearch, CancellationToken cancellationToken = default)
    {
        RecordSearch(durationMs, resultCount, cacheHit, usedVectorSearch);

        var result = await _metricsRepository.AddSearchHistoryAsync(query, resultCount, durationMs, cacheHit, usedVectorSearch, cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Failed to persist search history for query: {Query}", query);
        }
    }

    public async Task RecordIndexingAsync(string projectPath, long durationMs, int fileCount, int chunkCount, CancellationToken cancellationToken = default)
    {
        RecordIndexing(durationMs, fileCount, chunkCount);

        var projectName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var description = $"Indexed {fileCount} files ({chunkCount} chunks) from {projectName}";
        var details = System.Text.Json.JsonSerializer.Serialize(new
        {
            ProjectPath = projectPath,
            FileCount = fileCount,
            ChunkCount = chunkCount
        });

        var result = await _metricsRepository.AddActivityLogAsync("indexing", description, durationMs, details, cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Failed to persist indexing activity for path: {Path}", projectPath);
        }
    }
}
