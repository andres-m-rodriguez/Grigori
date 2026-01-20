using Grigori.Contracts.Dtos.Dashboard;
using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.Dashboard.Services;

public class DashboardService
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly IMetricsRepository _metricsRepository;
    private readonly IServiceProvider _serviceProvider;

    private ISearchService? _searchService;
    private ISearchService SearchService =>
        _searchService ??= _serviceProvider.GetRequiredService<ISearchService>();

    public DashboardService(
        IDashboardRepository dashboardRepository,
        IMetricsRepository metricsRepository,
        IServiceProvider serviceProvider)
    {
        _dashboardRepository = dashboardRepository;
        _metricsRepository = metricsRepository;
        _serviceProvider = serviceProvider;
    }

    public async Task<IndexStatsDto> GetIndexStatsAsync()
    {
        var result = await _dashboardRepository.GetIndexStatsAsync();
        return result.IsSuccess
            ? result.Value
            : new IndexStatsDto(0, 0, 0, true, 0, null);
    }

    public async Task<List<IndexedProjectDto>> GetIndexedProjectsAsync()
    {
        var result = await _dashboardRepository.GetIndexedProjectsAsync();
        return result.IsSuccess ? result.Value : [];
    }

    public async Task<List<SearchResultViewModel>> SearchAsync(string query, int limit = 10, string searchMode = "hybrid")
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var request = new SearchRequestDto(query, limit, "full", null, searchMode);
        var result = await SearchService.SearchAsync(request);

        if (result.IsFailure)
            return [];

        return result.Value.Results
            .Select(r => new SearchResultViewModel(r.FilePath, r.StartLine, r.EndLine, r.Content, r.Score))
            .ToList();
    }

    public async Task<List<ActivityLogDto>> GetRecentActivityAsync(int limit = 20)
    {
        var result = await _metricsRepository.GetRecentActivityLogsAsync(limit);
        return result.IsSuccess ? result.Value : [];
    }

    public async Task<List<SearchHistoryDto>> GetSearchHistoryAsync(int limit = 20)
    {
        var result = await _metricsRepository.GetRecentSearchHistoryAsync(limit);
        return result.IsSuccess ? result.Value : [];
    }

    public async Task<List<PerformanceDataPointDto>> GetPerformanceMetricsAsync(int hoursBack = 24)
    {
        var result = await _metricsRepository.GetHourlyPerformanceMetricsAsync(hoursBack);
        return result.IsSuccess ? result.Value : [];
    }
}
