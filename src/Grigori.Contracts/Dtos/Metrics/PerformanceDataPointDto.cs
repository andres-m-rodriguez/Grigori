namespace Grigori.Contracts.Dtos.Metrics;

public record PerformanceDataPointDto
{
    public required DateTime Timestamp { get; init; }
    public required double AvgSearchTimeMs { get; init; }
    public required double AvgIndexTimeMs { get; init; }

    public string FormattedHour => Timestamp.ToLocalTime().ToString("HH:mm");
}
