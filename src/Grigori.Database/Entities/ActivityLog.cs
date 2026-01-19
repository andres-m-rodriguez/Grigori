namespace Grigori.Database.Entities;

public class ActivityLog
{
    public long Id { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
}
