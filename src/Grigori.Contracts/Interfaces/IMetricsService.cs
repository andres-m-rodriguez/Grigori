using Grigori.Contracts.Dtos.Metrics;

namespace Grigori.Contracts.Interfaces;

public interface IMetricsService
{
    MetricsSnapshotDto GetSnapshot();
    void Reset();
    void RecordSearch(long durationMs, int resultCount, bool cacheHit, bool usedHnsw);
    void RecordIndexing(long durationMs, int fileCount, int chunkCount);
    void RecordEmbeddingGeneration(long durationMs, int count);
    void RecordFileActivity(string filePath, string projectName, int chunksCreated);
    List<ActivityEvent> GetRecentActivity(int count = 50);
}
