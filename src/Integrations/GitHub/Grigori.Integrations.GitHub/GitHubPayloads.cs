namespace Grigori.Integrations.GitHub;

// Only the fields Grigori actually reads. GitHub's pull_request payload carries a couple
// hundred more; binding them all would turn every field GitHub ships into churn here, and
// none of them survive translation into Grigori's vocabulary anyway.

internal sealed record GitHubPullRequestEvent(
    string Action,
    GitHubPullRequest PullRequest,
    GitHubRepository Repository);

internal sealed record GitHubPullRequest(
    long Number,
    string Title,
    // The PR description. Null when the author left it empty, which is common enough that
    // callers must handle it rather than treating it as a malformed payload.
    string? Body,
    string HtmlUrl,
    bool Draft,
    DateTimeOffset CreatedAt,
    GitHubAccount User,
    GitHubBranch Head,
    GitHubBranch Base);

internal sealed record GitHubBranch(string Ref, string Sha);

internal sealed record GitHubAccount(string Login);

internal sealed record GitHubRepository(string FullName);
