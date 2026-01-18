namespace Grigori.Database.Models;

public record SearchHistory
{
    public required long Id { get; init; }
    public required string Query { get; init; }
    public required int ResultCount { get; init; }
    public required long DurationMs { get; init; }
    public required bool CacheHit { get; init; }
    public required bool UsedHnsw { get; init; }
    public required DateTime Timestamp { get; init; }
}
