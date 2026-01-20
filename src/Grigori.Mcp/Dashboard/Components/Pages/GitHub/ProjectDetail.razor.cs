using Grigori.Contracts.Dtos.GitHub;
using Grigori.Contracts.Interfaces;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Grigori.Mcp.Dashboard.Components.Pages.GitHub;

public partial class ProjectDetail : ComponentBase
{
    [Parameter] public string Owner { get; set; } = "";
    [Parameter] public string Repo { get; set; } = "";

    [Inject] private IGitHubService GitHubService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private GitHubRepositoryDto? _repository;
    private List<GitHubCommitDto> _commits = [];
    private List<GitHubPullRequestDto> _pullRequests = [];
    private List<GitHubBranchDto> _branches = [];
    private bool _loading = true;
    private bool _loadingActivity = true;

    protected override async Task OnInitializedAsync()
    {
        if (GitHubService.IsAuthenticated)
        {
            await LoadRepositoryAsync();
        }
        _loading = false;
    }

    private async Task LoadRepositoryAsync()
    {
        var repos = await GitHubService.GetRepositoriesAsync(200);
        _repository = repos.FirstOrDefault(r =>
            r.FullName.Equals($"{Owner}/{Repo}", StringComparison.OrdinalIgnoreCase));

        if (_repository != null)
        {
            await LoadActivityAsync();
        }
    }

    private async Task LoadActivityAsync()
    {
        _loadingActivity = true;
        StateHasChanged();

        try
        {
            var commitsTask = GitHubService.GetRecentCommitsAsync(Owner, Repo, 30);
            var prsTask = GitHubService.GetPullRequestsAsync(Owner, Repo, 30);
            var branchesTask = GitHubService.GetBranchesAsync(Owner, Repo);

            await Task.WhenAll(commitsTask, prsTask, branchesTask);

            _commits = commitsTask.Result;
            _pullRequests = prsTask.Result;
            _branches = branchesTask.Result;
        }
        finally
        {
            _loadingActivity = false;
        }
    }

    private void NavigateBack()
    {
        Navigation.NavigateTo("/github/projects");
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

    private static Color GetPrStateColor(GitHubPullRequestDto pr)
    {
        if (pr.MergedAt.HasValue)
            return Color.Secondary;
        if (pr.ClosedAt.HasValue)
            return Color.Error;
        if (pr.IsDraft)
            return Color.Default;
        return Color.Success;
    }
}
