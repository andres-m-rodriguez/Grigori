using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Results;
using Grigori.Database;
using Grigori.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Grigori.DataAccess.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly IDbContextFactory<GrigoriDbContext> _dbContextFactory;
    private readonly ILogger<ProjectRepository> _logger;

    public ProjectRepository(
        IDbContextFactory<GrigoriDbContext> dbContextFactory,
        ILogger<ProjectRepository> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<Result<ProjectDto, GrigoriError>> CreateAsync(
        CreateProjectInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var exists = await dbContext.Projects
                .AnyAsync(p => p.GitHubRepoFullName == input.GitHubRepoFullName, cancellationToken);

            if (exists)
            {
                return GrigoriError.AlreadyExists("Project", input.GitHubRepoFullName);
            }

            var now = DateTime.UtcNow;
            var entity = new ProjectEntity
            {
                Name = input.Name,
                GitHubRepoFullName = input.GitHubRepoFullName,
                GitHubRepoId = input.GitHubRepoId,
                Description = input.Description,
                DefaultBranch = input.DefaultBranch,
                Language = input.Language,
                Status = ProjectStatus.NotIndexed,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.Projects.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created project {Name} ({RepoFullName})", input.Name, input.GitHubRepoFullName);

            return MapToDto(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project {RepoFullName}", input.GitHubRepoFullName);
            return GrigoriError.DatabaseError($"Failed to create project: {ex.Message}", ex);
        }
    }

    public async Task<Result<ProjectDto, GrigoriError>> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = await dbContext.Projects
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

            if (entity is null)
            {
                return GrigoriError.NotFound("Project", id.ToString());
            }

            return MapToDto(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get project by ID {Id}", id);
            return GrigoriError.DatabaseError($"Failed to get project: {ex.Message}", ex);
        }
    }

    public async Task<Result<ProjectDto, GrigoriError>> GetByGitHubRepoFullNameAsync(
        string repoFullName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = await dbContext.Projects
                .FirstOrDefaultAsync(p => p.GitHubRepoFullName == repoFullName, cancellationToken);

            if (entity is null)
            {
                return GrigoriError.NotFound("Project", repoFullName);
            }

            return MapToDto(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get project by repo {RepoFullName}", repoFullName);
            return GrigoriError.DatabaseError($"Failed to get project: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<ProjectDto>, GrigoriError>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entities = await dbContext.Projects
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync(cancellationToken);

            return entities.Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all projects");
            return GrigoriError.DatabaseError($"Failed to get projects: {ex.Message}", ex);
        }
    }

    public async Task<Result<ProjectDto, GrigoriError>> UpdateStatusAsync(
        long id,
        ProjectStatusDto status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = await dbContext.Projects
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

            if (entity is null)
            {
                return GrigoriError.NotFound("Project", id.ToString());
            }

            entity.Status = (ProjectStatus)status;
            entity.ErrorMessage = errorMessage;
            entity.UpdatedAt = DateTime.UtcNow;

            if (status == ProjectStatusDto.Indexed)
            {
                entity.LastIndexedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated project {Id} status to {Status}", id, status);

            return MapToDto(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update project status for {Id}", id);
            return GrigoriError.DatabaseError($"Failed to update project status: {ex.Message}", ex);
        }
    }

    public async Task<Result<ProjectDto, GrigoriError>> UpdateIndexingStatsAsync(
        long id,
        int fileCount,
        int chunkCount,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = await dbContext.Projects
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

            if (entity is null)
            {
                return GrigoriError.NotFound("Project", id.ToString());
            }

            entity.FileCount = fileCount;
            entity.ChunkCount = chunkCount;
            entity.LastIndexedCommitSha = commitSha;
            entity.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated indexing stats for project {Id}: {FileCount} files, {ChunkCount} chunks",
                id, fileCount, chunkCount);

            return MapToDto(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update indexing stats for project {Id}", id);
            return GrigoriError.DatabaseError($"Failed to update indexing stats: {ex.Message}", ex);
        }
    }

    public async Task<Result<bool, GrigoriError>> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var deleted = await dbContext.Projects
                .Where(p => p.Id == id)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted == 0)
            {
                return GrigoriError.NotFound("Project", id.ToString());
            }

            _logger.LogInformation("Deleted project {Id}", id);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete project {Id}", id);
            return GrigoriError.DatabaseError($"Failed to delete project: {ex.Message}", ex);
        }
    }

    public async Task<bool> ExistsByGitHubRepoFullNameAsync(
        string repoFullName,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Projects
            .AnyAsync(p => p.GitHubRepoFullName == repoFullName, cancellationToken);
    }

    private static ProjectDto MapToDto(ProjectEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        GitHubRepoFullName = entity.GitHubRepoFullName,
        GitHubRepoId = entity.GitHubRepoId,
        Description = entity.Description,
        DefaultBranch = entity.DefaultBranch,
        Language = entity.Language,
        Status = (ProjectStatusDto)entity.Status,
        LastIndexedAt = entity.LastIndexedAt,
        FileCount = entity.FileCount,
        ChunkCount = entity.ChunkCount,
        LastIndexedCommitSha = entity.LastIndexedCommitSha,
        ErrorMessage = entity.ErrorMessage,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
