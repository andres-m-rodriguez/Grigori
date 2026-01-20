namespace Grigori.Contracts.Dtos.Metrics;

public record SearchHistoryDto(long Id, string Query, int ResultCount, long DurationMs, bool CacheHit, bool UsedVectorSearch, DateTime Timestamp)
{
    public string FormattedDuration => $"{DurationMs}ms";

    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("g");

    public string RelativeTime => (DateTime.UtcNow - Timestamp).TotalMinutes switch
    {
        < 1 => "just now",
        < 60 => $"{(int)(DateTime.UtcNow - Timestamp).TotalMinutes}m ago",
        < 1440 => $"{(int)(DateTime.UtcNow - Timestamp).TotalHours}h ago",
        _ => $"{(int)(DateTime.UtcNow - Timestamp).TotalDays}d ago"
    };

    public string TruncatedQuery => Query.Length > 50 ? Query[..47] + "..." : Query;
}
