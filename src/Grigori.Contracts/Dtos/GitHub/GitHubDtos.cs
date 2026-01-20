namespace Grigori.Contracts.Dtos.GitHub;

/// <summary>
/// Result of a GitHub authentication attempt.
/// </summary>
public record GitHubAuthResult(bool Success, string? ErrorMessage = null, GitHubUserDto? User = null)
{
    public static GitHubAuthResult Succeeded(GitHubUserDto user) => new(true, null, user);
    public static GitHubAuthResult Failed(string error) => new(false, error);
}

/// <summary>
/// GitHub user information.
/// </summary>
public record GitHubUserDto(
    string Login,
    string? Name,
    string? AvatarUrl,
    string? Email,
    int PublicRepos,
    int PrivateRepos,
    DateTime CreatedAt
);

/// <summary>
/// GitHub repository information.
/// </summary>
public record GitHubRepositoryDto(
    long Id,
    string Name,
    string FullName,
    string? Description,
    string DefaultBranch,
    bool IsPrivate,
    string? Language,
    int StarCount,
    int ForkCount,
    int OpenIssuesCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PushedAt,
    string HtmlUrl,
    string CloneUrl
);

/// <summary>
/// GitHub commit information.
/// </summary>
public record GitHubCommitDto(
    string Sha,
    string ShortSha,
    string Message,
    string? AuthorName,
    string? AuthorEmail,
    string? AuthorAvatarUrl,
    DateTime CommittedAt,
    string HtmlUrl,
    int Additions,
    int Deletions,
    int ChangedFiles
)
{
    public string ShortMessage => Message.Length > 72
        ? Message[..69] + "..."
        : Message.Split('\n')[0];
}

/// <summary>
/// GitHub pull request information.
/// </summary>
public record GitHubPullRequestDto(
    int Number,
    string Title,
    string State,
    string? Body,
    string AuthorLogin,
    string? AuthorAvatarUrl,
    string HeadBranch,
    string BaseBranch,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? MergedAt,
    DateTime? ClosedAt,
    string HtmlUrl,
    int Additions,
    int Deletions,
    int ChangedFiles,
    int CommitCount,
    bool IsDraft
);

/// <summary>
/// GitHub branch information.
/// </summary>
public record GitHubBranchDto(
    string Name,
    string CommitSha,
    bool IsProtected,
    bool IsDefault
);
