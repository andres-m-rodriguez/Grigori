using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

/// <summary>
/// Repository for metrics-related database operations.
/// </summary>
public interface IMetricsRepository
{
    /// <summary>
    /// Records a search query in the history.
    /// </summary>
    Task<Result<long, GrigoriError>> AddSearchHistoryAsync(
        string query,
        int resultCount,
        long durationMs,
        bool cacheHit,
        bool usedVectorSearch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an activity log entry.
    /// </summary>
    Task<Result<long, GrigoriError>> AddActivityLogAsync(
        string activityType,
        string description,
        long durationMs,
        string? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent activity logs.
    /// </summary>
    Task<Result<List<ActivityLogDto>, GrigoriError>> GetRecentActivityLogsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent search history.
    /// </summary>
    Task<Result<List<SearchHistoryDto>, GrigoriError>> GetRecentSearchHistoryAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets hourly performance metrics for the specified time period.
    /// </summary>
    Task<Result<List<PerformanceDataPointDto>, GrigoriError>> GetHourlyPerformanceMetricsAsync(
        int hoursBack = 24,
        CancellationToken cancellationToken = default);
}
