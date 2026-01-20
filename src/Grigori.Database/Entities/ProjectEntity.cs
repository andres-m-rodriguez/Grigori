namespace Grigori.Database.Entities;

public class ProjectEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GitHubRepoFullName { get; set; } = string.Empty;
    public long GitHubRepoId { get; set; }
    public string? Description { get; set; }
    public string? DefaultBranch { get; set; }
    public string? Language { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.NotIndexed;
    public DateTime? LastIndexedAt { get; set; }
    public int FileCount { get; set; }
    public int ChunkCount { get; set; }
    public string? LastIndexedCommitSha { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
