namespace Grigori.Contracts.Dtos.Metrics;

public record ActivityLogDto(long Id, string ActivityType, string Description, long DurationMs, DateTime Timestamp, string? Details = null)
{
    public string FormattedDuration => DurationMs switch
    {
        < 1000 => $"{DurationMs}ms",
        < 60000 => $"{DurationMs / 1000.0:F1}s",
        _ => $"{DurationMs / 60000.0:F1}m"
    };

    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("g");

    public string RelativeTime => (DateTime.UtcNow - Timestamp).TotalMinutes switch
    {
        < 1 => "just now",
        < 60 => $"{(int)(DateTime.UtcNow - Timestamp).TotalMinutes}m ago",
        < 1440 => $"{(int)(DateTime.UtcNow - Timestamp).TotalHours}h ago",
        _ => $"{(int)(DateTime.UtcNow - Timestamp).TotalDays}d ago"
    };
}
