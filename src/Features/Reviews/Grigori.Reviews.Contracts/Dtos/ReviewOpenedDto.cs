namespace Grigori.Reviews.Contracts.Dtos;

/// <summary>
/// A Review entering the system for the first time, in Grigori's vocabulary rather than any
/// integration's. Nothing downstream of this record knows the phrase "pull request".
/// </summary>
/// <remarks>
/// A Review is the whole thing under review, not just its diff — the description is as much a
/// part of it as the branch it targets, and agents read it to decide what the work is for.
/// GitHub's own notion of a "review" (approve / request changes) is a Verdict here.
/// </remarks>
public sealed record ReviewOpenedDto(
    Origin Origin,
    string Title,
    string? Description,
    string Author,
    string SourceBranch,
    string TargetBranch,
    string HeadSha,
    bool IsDraft,
    string Url,
    DateTimeOffset OpenedAt);
