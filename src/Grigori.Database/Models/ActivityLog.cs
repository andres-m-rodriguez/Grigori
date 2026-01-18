namespace Grigori.Database.Models;

public record ActivityLog
{
    public required long Id { get; init; }
    public required string ActivityType { get; init; } // "indexing" or "search"
    public required string Description { get; init; }
    public required long DurationMs { get; init; }
    public required DateTime Timestamp { get; init; }
    public string? Details { get; init; } // JSON for additional metadata
}
