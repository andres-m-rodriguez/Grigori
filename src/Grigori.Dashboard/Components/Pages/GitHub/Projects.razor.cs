using Grigori.Contracts.Dtos.GitHub;
using Grigori.Contracts.Interfaces;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Grigori.Dashboard.Components.Pages.GitHub;

public partial class Projects : ComponentBase
{
    [Inject] private IGitHubService GitHubService { get; set; } = null!;
    [Inject] private IProjectRepository ProjectRepository { get; set; } = null!;
    [Inject] private IGitHubIndexingService GitHubIndexingService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<GitHubRepositoryDto> _repositories = [];
    private Dictionary<string, ProjectDto> _projectsByRepoName = new();
    private HashSet<string> _indexingRepos = new();
    private GitHubUserDto? _user;
    private bool _loading = true;

    private string _searchQuery = "";
    private string _languageFilter = "";
    private string _visibilityFilter = "all";
    private string _indexFilter = "all";
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
        .Where(r => _indexFilter == "all" ||
                    (_indexFilter == "indexed" && GetProjectStatus(r.FullName) == ProjectStatusDto.Indexed) ||
                    (_indexFilter == "not-indexed" && GetProjectStatus(r.FullName) == null))
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
        var projectsTask = ProjectRepository.GetAllAsync();

        await Task.WhenAll(userTask, reposTask, projectsTask);

        _user = userTask.Result;
        _repositories = reposTask.Result;

        if (projectsTask.Result.IsSuccess)
        {
            _projectsByRepoName = projectsTask.Result.Value
                .ToDictionary(p => p.GitHubRepoFullName, StringComparer.OrdinalIgnoreCase);
        }
    }

    private ProjectStatusDto? GetProjectStatus(string repoFullName)
    {
        return _projectsByRepoName.TryGetValue(repoFullName, out var project) ? project.Status : null;
    }

    private ProjectDto? GetProject(string repoFullName)
    {
        return _projectsByRepoName.TryGetValue(repoFullName, out var project) ? project : null;
    }

    private bool IsIndexing(string repoFullName) => _indexingRepos.Contains(repoFullName);

    private async Task IndexRepositoryAsync(GitHubRepositoryDto repo)
    {
        if (_indexingRepos.Contains(repo.FullName))
            return;

        _indexingRepos.Add(repo.FullName);
        StateHasChanged();

        try
        {
            // Register the project first if it doesn't exist
            var registerResult = await GitHubIndexingService.RegisterProjectAsync(repo.FullName);
            if (!registerResult.IsSuccess)
            {
                Snackbar.Add($"Failed to register project: {registerResult.Error.Message}", Severity.Error);
                return;
            }

            var project = registerResult.Value;
            _projectsByRepoName[repo.FullName] = project;
            StateHasChanged();

            // Start indexing
            var indexResult = await GitHubIndexingService.IndexRepositoryAsync(project.Id);
            if (!indexResult.IsSuccess)
            {
                Snackbar.Add($"Indexing failed: {indexResult.Error.Message}", Severity.Error);
                // Refresh project status
                var refreshResult = await ProjectRepository.GetByIdAsync(project.Id);
                if (refreshResult.IsSuccess)
                    _projectsByRepoName[repo.FullName] = refreshResult.Value;
                return;
            }

            // Refresh project status after indexing
            var finalResult = await ProjectRepository.GetByIdAsync(project.Id);
            if (finalResult.IsSuccess)
                _projectsByRepoName[repo.FullName] = finalResult.Value;

            Snackbar.Add($"Successfully indexed {repo.Name}: {indexResult.Value.FilesIndexed} files, {indexResult.Value.ChunksCreated} chunks", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Error: {ex.Message}", Severity.Error);
        }
        finally
        {
            _indexingRepos.Remove(repo.FullName);
            StateHasChanged();
        }
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

    private static (string Icon, string Color, string Text) GetStatusDisplay(ProjectStatusDto? status)
    {
        return status switch
        {
            ProjectStatusDto.Indexed => (Icons.Material.Filled.CheckCircle, "var(--color-success-fg)", "Indexed"),
            ProjectStatusDto.Indexing => (Icons.Material.Filled.Sync, "var(--color-attention-fg)", "Indexing"),
            ProjectStatusDto.Failed => (Icons.Material.Filled.Error, "var(--color-danger-fg)", "Failed"),
            ProjectStatusDto.NotIndexed => (Icons.Material.Filled.Schedule, "var(--color-fg-muted)", "Not Indexed"),
            _ => (Icons.Material.Filled.HelpOutline, "var(--color-fg-muted)", "Not Indexed")
        };
    }
}
