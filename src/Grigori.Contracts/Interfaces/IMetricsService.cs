using Grigori.Contracts.Dtos.Metrics;

namespace Grigori.Contracts.Interfaces;

public interface IMetricsService
{
    MetricsSnapshotDto GetSnapshot();
    void Reset();
    void RecordSearch(long durationMs, int resultCount, bool cacheHit, bool usedHnsw);
    void RecordIndexing(long durationMs, int fileCount, int chunkCount);
    void RecordEmbeddingGeneration(long durationMs, int count);

    // Async methods for persistence with additional context
    Task RecordSearchAsync(string query, long durationMs, int resultCount, bool cacheHit, bool usedHnsw, CancellationToken cancellationToken = default);
    Task RecordIndexingAsync(string projectPath, long durationMs, int fileCount, int chunkCount, CancellationToken cancellationToken = default);
}
