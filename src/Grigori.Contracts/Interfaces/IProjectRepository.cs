using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

public interface IProjectRepository
{
    Task<Result<ProjectDto, GrigoriError>> CreateAsync(
        CreateProjectInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectDto, GrigoriError>> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectDto, GrigoriError>> GetByGitHubRepoFullNameAsync(
        string repoFullName,
        CancellationToken cancellationToken = default);

    Task<Result<List<ProjectDto>, GrigoriError>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ProjectDto, GrigoriError>> UpdateStatusAsync(
        long id,
        ProjectStatusDto status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectDto, GrigoriError>> UpdateIndexingStatsAsync(
        long id,
        int fileCount,
        int chunkCount,
        string commitSha,
        CancellationToken cancellationToken = default);

    Task<Result<bool, GrigoriError>> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByGitHubRepoFullNameAsync(
        string repoFullName,
        CancellationToken cancellationToken = default);
}

public enum ProjectStatusDto
{
    NotIndexed = 0,
    Indexing = 1,
    Indexed = 2,
    Failed = 3
}

public record CreateProjectInput
{
    public required string Name { get; init; }
    public required string GitHubRepoFullName { get; init; }
    public required long GitHubRepoId { get; init; }
    public string? Description { get; init; }
    public string? DefaultBranch { get; init; }
    public string? Language { get; init; }
}

public record ProjectDto
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string GitHubRepoFullName { get; init; }
    public required long GitHubRepoId { get; init; }
    public string? Description { get; init; }
    public string? DefaultBranch { get; init; }
    public string? Language { get; init; }
    public required ProjectStatusDto Status { get; init; }
    public DateTime? LastIndexedAt { get; init; }
    public required int FileCount { get; init; }
    public required int ChunkCount { get; init; }
    public string? LastIndexedCommitSha { get; init; }
    public string? ErrorMessage { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
