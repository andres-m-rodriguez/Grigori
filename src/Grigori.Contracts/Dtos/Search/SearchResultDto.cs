namespace Grigori.Contracts.Dtos.Search;

public record SearchResultDto(bool Success, int Count, List<CodeChunkDto> Results, SearchMetricsDto Metrics);

public record SearchMetricsDto(long DurationMs, bool CacheHit, string OutputMode, int TokenEstimate, string SearchMode = "hybrid");
