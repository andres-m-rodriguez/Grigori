namespace Grigori.Contracts.Dtos.Metrics;

public record ActivityLogDto
{
    public required long Id { get; init; }
    public required string ActivityType { get; init; }
    public required string Description { get; init; }
    public required long DurationMs { get; init; }
    public required DateTime Timestamp { get; init; }
    public string? Details { get; init; }

    public string FormattedDuration => DurationMs switch
    {
        < 1000 => $"{DurationMs}ms",
        < 60000 => $"{DurationMs / 1000.0:F1}s",
        _ => $"{DurationMs / 60000.0:F1}m"
    };

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
}
