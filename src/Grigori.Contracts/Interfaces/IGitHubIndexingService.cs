using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

public interface IGitHubIndexingService
{
    Task<Result<GitHubIndexingResultDto, GrigoriError>> IndexRepositoryAsync(
        long projectId,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectDto, GrigoriError>> RegisterProjectAsync(
        string repoFullName,
        CancellationToken cancellationToken = default);
}

public record GitHubIndexingResultDto
{
    public required long ProjectId { get; init; }
    public required string RepoFullName { get; init; }
    public required int FilesIndexed { get; init; }
    public required int ChunksCreated { get; init; }
    public required long DurationMs { get; init; }
    public required string CommitSha { get; init; }
}
