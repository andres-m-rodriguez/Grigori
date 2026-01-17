namespace Grigori.Contracts.Dtos.Metrics;

public record MetricsSnapshotDto
{
    public required TimeSpan Uptime { get; init; }
    public required SearchStatsDto SearchStats { get; init; }
    public required IndexingStatsDto IndexingStats { get; init; }
    public required EmbeddingStatsDto EmbeddingStats { get; init; }
}

public record SearchStatsDto
{
    public required long TotalSearches { get; init; }
    public required long CacheHits { get; init; }
    public required long CacheMisses { get; init; }
    public required long HnswSearches { get; init; }
    public required double AverageTimeMs { get; init; }
    public required double AverageResultCount { get; init; }
}

public record IndexingStatsDto
{
    public required long TotalOperations { get; init; }
    public required long FilesIndexed { get; init; }
    public required long ChunksCreated { get; init; }
    public required long TotalTimeMs { get; init; }
}

public record EmbeddingStatsDto
{
    public required long TotalEmbeddings { get; init; }
    public required long TotalTimeMs { get; init; }
    public required double AverageTimeMs { get; init; }
}
