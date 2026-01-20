namespace Grigori.Contracts.Dtos.Metrics;

public record PerformanceDataPointDto(DateTime Timestamp, double AvgSearchTimeMs, double AvgIndexTimeMs)
{
    public string FormattedHour => Timestamp.ToLocalTime().ToString("HH:mm");
}
