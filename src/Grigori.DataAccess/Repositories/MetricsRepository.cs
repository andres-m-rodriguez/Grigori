using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Results;
using Grigori.Database;
using Grigori.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Grigori.DataAccess.Repositories;

public class MetricsRepository : IMetricsRepository
{
    private readonly IDbContextFactory<GrigoriDbContext> _dbContextFactory;
    private readonly ILogger<MetricsRepository> _logger;

    public MetricsRepository(IDbContextFactory<GrigoriDbContext> dbContextFactory, ILogger<MetricsRepository> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<Result<long, GrigoriError>> AddSearchHistoryAsync(
        string query,
        int resultCount,
        long durationMs,
        bool cacheHit,
        bool usedVectorSearch,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = new SearchHistoryEntity
            {
                Query = query,
                ResultCount = resultCount,
                DurationMs = durationMs,
                CacheHit = cacheHit,
                UsedPgvector = usedVectorSearch,
                Timestamp = DateTime.UtcNow
            };

            dbContext.SearchHistory.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            return entity.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add search history for query: {Query}", query);
            return GrigoriError.DatabaseError($"Failed to add search history: {ex.Message}", ex);
        }
    }

    public async Task<Result<long, GrigoriError>> AddActivityLogAsync(
        string activityType,
        string description,
        long durationMs,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = new ActivityLogEntity
            {
                ActivityType = activityType,
                Description = description,
                DurationMs = durationMs,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            dbContext.ActivityLogs.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            return entity.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add activity log: {Description}", description);
            return GrigoriError.DatabaseError($"Failed to add activity log: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<ActivityLogDto>, GrigoriError>> GetRecentActivityLogsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var logs = await dbContext.ActivityLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(limit)
                .Select(l => new ActivityLogDto(l.Id, l.ActivityType, l.Description, l.DurationMs, l.Timestamp, l.Details))
                .ToListAsync(cancellationToken);

            return logs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent activity logs");
            return GrigoriError.DatabaseError($"Failed to get activity logs: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<SearchHistoryDto>, GrigoriError>> GetRecentSearchHistoryAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var history = await dbContext.SearchHistory
                .OrderByDescending(h => h.Timestamp)
                .Take(limit)
                .Select(h => new SearchHistoryDto(h.Id, h.Query, h.ResultCount, h.DurationMs, h.CacheHit, h.UsedPgvector, h.Timestamp))
                .ToListAsync(cancellationToken);

            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent search history");
            return GrigoriError.DatabaseError($"Failed to get search history: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<PerformanceDataPointDto>, GrigoriError>> GetHourlyPerformanceMetricsAsync(
        int hoursBack = 24,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var cutoff = DateTime.UtcNow.AddHours(-hoursBack);

            var logs = await dbContext.ActivityLogs
                .Where(l => l.Timestamp >= cutoff)
                .ToListAsync(cancellationToken);

            var metricsLookup = logs
                .GroupBy(l => new DateTime(l.Timestamp.Year, l.Timestamp.Month, l.Timestamp.Day, l.Timestamp.Hour, 0, 0, DateTimeKind.Utc))
                .ToDictionary(
                    g => g.Key,
                    g => (
                        AvgSearchTimeMs: g.Where(x => x.ActivityType == "search").Select(x => x.DurationMs).DefaultIfEmpty(0).Average(),
                        AvgIndexTimeMs: g.Where(x => x.ActivityType == "index" || x.ActivityType == "indexing").Select(x => x.DurationMs).DefaultIfEmpty(0).Average()
                    ));

            var result = new List<PerformanceDataPointDto>();
            var now = DateTime.UtcNow;
            var startHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc)
                .AddHours(-hoursBack + 1);

            for (int i = 0; i < hoursBack; i++)
            {
                var hour = startHour.AddHours(i);
                if (metricsLookup.TryGetValue(hour, out var data))
                {
                    result.Add(new PerformanceDataPointDto(hour, data.AvgSearchTimeMs, data.AvgIndexTimeMs));
                }
                else
                {
                    result.Add(new PerformanceDataPointDto(hour, 0, 0));
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get hourly performance metrics");
            return GrigoriError.DatabaseError($"Failed to get performance metrics: {ex.Message}", ex);
        }
    }
}
