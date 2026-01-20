using Grigori.Contracts.Dtos.GitHub;

namespace Grigori.Contracts.Interfaces;

/// <summary>
/// Service for GitHub integration using Personal Access Token (PAT) authentication.
/// All GitHub-dependent features should check IsAuthenticated before attempting operations.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Gets whether a valid GitHub token is configured.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Event raised when authentication state changes.
    /// </summary>
    event Action? AuthStateChanged;

    /// <summary>
    /// Configures the GitHub PAT. The token is validated and stored securely.
    /// </summary>
    /// <param name="token">The GitHub Personal Access Token</param>
    /// <returns>Result indicating success or failure with error message</returns>
    Task<GitHubAuthResult> ConfigureTokenAsync(string token);

    /// <summary>
    /// Clears the stored GitHub token.
    /// </summary>
    void ClearToken();

    /// <summary>
    /// Gets the authenticated user's information.
    /// </summary>
    Task<GitHubUserDto?> GetCurrentUserAsync();

    /// <summary>
    /// Gets repositories accessible to the authenticated user.
    /// </summary>
    /// <param name="limit">Maximum number of repositories to return</param>
    Task<List<GitHubRepositoryDto>> GetRepositoriesAsync(int limit = 30);

    /// <summary>
    /// Gets recent commits for a repository.
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repo">Repository name</param>
    /// <param name="limit">Maximum number of commits to return</param>
    Task<List<GitHubCommitDto>> GetRecentCommitsAsync(string owner, string repo, int limit = 30);

    /// <summary>
    /// Gets open pull requests for a repository.
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repo">Repository name</param>
    /// <param name="limit">Maximum number of PRs to return</param>
    Task<List<GitHubPullRequestDto>> GetPullRequestsAsync(string owner, string repo, int limit = 30);

    /// <summary>
    /// Gets branches for a repository.
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repo">Repository name</param>
    Task<List<GitHubBranchDto>> GetBranchesAsync(string owner, string repo);
}
