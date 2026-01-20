namespace Grigori.Contracts.Dtos.Metrics;

public record MetricsSnapshotDto(TimeSpan Uptime, SearchStatsDto SearchStats, IndexingStatsDto IndexingStats, EmbeddingStatsDto EmbeddingStats);

public record SearchStatsDto(long TotalSearches, long CacheHits, long CacheMisses, long VectorSearches, double AverageTimeMs, double AverageResultCount);

public record IndexingStatsDto(long TotalOperations, long FilesIndexed, long ChunksCreated, long TotalTimeMs);

public record EmbeddingStatsDto(long TotalEmbeddings, long TotalTimeMs, double AverageTimeMs);
