using Grigori.Contracts.Dtos.GitHub;
using Grigori.Contracts.Interfaces;
using Microsoft.AspNetCore.Components;

namespace Grigori.Dashboard.Components.Pages.GitHub;

public partial class Projects : ComponentBase
{
    [Inject] private IGitHubService GitHubService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private List<GitHubRepositoryDto> _repositories = [];
    private GitHubUserDto? _user;
    private bool _loading = true;

    private string _searchQuery = "";
    private string _languageFilter = "";
    private string _visibilityFilter = "all";
    private int _currentPage = 1;
    private const int PageSize = 9;

    private List<string> Languages => _repositories
        .Where(r => !string.IsNullOrEmpty(r.Language))
        .Select(r => r.Language!)
        .Distinct()
        .OrderBy(l => l)
        .ToList();

    private List<GitHubRepositoryDto> FilteredRepositories => _repositories
        .Where(r => string.IsNullOrEmpty(_searchQuery) ||
                    r.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    (r.Description?.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ?? false))
        .Where(r => string.IsNullOrEmpty(_languageFilter) || r.Language == _languageFilter)
        .Where(r => _visibilityFilter == "all" ||
                    (_visibilityFilter == "private" && r.IsPrivate) ||
                    (_visibilityFilter == "public" && !r.IsPrivate))
        .ToList();

    private List<GitHubRepositoryDto> PaginatedRepositories => FilteredRepositories
        .Skip((_currentPage - 1) * PageSize)
        .Take(PageSize)
        .ToList();

    private int TotalPages => (int)Math.Ceiling(FilteredRepositories.Count / (double)PageSize);

    protected override async Task OnInitializedAsync()
    {
        if (GitHubService.IsAuthenticated)
        {
            await LoadDataAsync();
        }
        _loading = false;
    }

    private async Task LoadDataAsync()
    {
        var userTask = GitHubService.GetCurrentUserAsync();
        var reposTask = GitHubService.GetRepositoriesAsync(100);

        await Task.WhenAll(userTask, reposTask);

        _user = userTask.Result;
        _repositories = reposTask.Result;
    }

    private void OnPageChanged(int page)
    {
        _currentPage = page;
    }

    private void NavigateToProject(GitHubRepositoryDto repo)
    {
        var parts = repo.FullName.Split('/');
        Navigation.NavigateTo($"/github/projects/{parts[0]}/{parts[1]}");
    }

    private static string TruncateDescription(string description)
    {
        return description.Length > 100 ? description[..97] + "..." : description;
    }

    private static string FormatDate(DateTime date)
    {
        var diff = DateTime.UtcNow - date;
        return diff.TotalDays switch
        {
            < 1 => diff.TotalHours < 1 ? $"{(int)diff.TotalMinutes}m ago" : $"{(int)diff.TotalHours}h ago",
            < 7 => $"{(int)diff.TotalDays}d ago",
            < 30 => $"{(int)(diff.TotalDays / 7)}w ago",
            _ => date.ToString("MMM d, yyyy")
        };
    }
}
