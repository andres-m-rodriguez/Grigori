namespace Grigori.Contracts.Dtos.Metrics;

public record SearchHistoryDto
{
    public required long Id { get; init; }
    public required string Query { get; init; }
    public required int ResultCount { get; init; }
    public required long DurationMs { get; init; }
    public required bool CacheHit { get; init; }
    public required bool UsedHnsw { get; init; }
    public required DateTime Timestamp { get; init; }

    public string FormattedDuration => $"{DurationMs}ms";

    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("g");

    public string RelativeTime
    {
        get
        {
            var elapsed = DateTime.UtcNow - Timestamp;
            return elapsed.TotalMinutes switch
            {
                < 1 => "just now",
                < 60 => $"{(int)elapsed.TotalMinutes}m ago",
                < 1440 => $"{(int)elapsed.TotalHours}h ago",
                _ => $"{(int)elapsed.TotalDays}d ago"
            };
        }
    }

    public string TruncatedQuery => Query.Length > 50 ? Query[..47] + "..." : Query;
}
