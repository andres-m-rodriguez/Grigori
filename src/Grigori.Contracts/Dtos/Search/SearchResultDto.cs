namespace Grigori.Contracts.Dtos.Search;

public record SearchResultDto
{
    public required bool Success { get; init; }
    public required int Count { get; init; }
    public required List<CodeChunkDto> Results { get; init; }
    public required SearchMetricsDto Metrics { get; init; }
}

public record SearchMetricsDto
{
    public required long DurationMs { get; init; }
    public required bool CacheHit { get; init; }
    public required string OutputMode { get; init; }
    public required int TokenEstimate { get; init; }
}
