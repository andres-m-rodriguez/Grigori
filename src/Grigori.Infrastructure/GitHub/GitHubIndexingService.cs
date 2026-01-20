using System.Diagnostics;
using System.IO.Compression;
using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Results;
using Microsoft.Extensions.Logging;

namespace Grigori.Infrastructure.GitHub;

public class GitHubIndexingService : IGitHubIndexingService
{
    private readonly IGitHubService _gitHubService;
    private readonly IProjectRepository _projectRepository;
    private readonly IIndexService _indexService;
    private readonly ILogger<GitHubIndexingService> _logger;

    public GitHubIndexingService(
        IGitHubService gitHubService,
        IProjectRepository projectRepository,
        IIndexService indexService,
        ILogger<GitHubIndexingService> logger)
    {
        _gitHubService = gitHubService;
        _projectRepository = projectRepository;
        _indexService = indexService;
        _logger = logger;
    }

    public async Task<Result<ProjectDto, GrigoriError>> RegisterProjectAsync(
        string repoFullName,
        CancellationToken cancellationToken = default)
    {
        if (!_gitHubService.IsAuthenticated)
        {
            return GrigoriError.ValidationError("GitHub authentication required");
        }

        // Check if project already exists
        var existingResult = await _projectRepository.GetByGitHubRepoFullNameAsync(repoFullName, cancellationToken);
        if (existingResult.IsSuccess)
        {
            return existingResult.Value;
        }

        // Get repository info from GitHub
        var parts = repoFullName.Split('/');
        if (parts.Length != 2)
        {
            return GrigoriError.ValidationError("Invalid repository full name format. Expected: owner/repo");
        }

        var repos = await _gitHubService.GetRepositoriesAsync(100);
        var repo = repos.FirstOrDefault(r => r.FullName.Equals(repoFullName, StringComparison.OrdinalIgnoreCase));

        if (repo is null)
        {
            return GrigoriError.NotFound("Repository", repoFullName);
        }

        // Create project
        var input = new CreateProjectInput
        {
            Name = repo.Name,
            GitHubRepoFullName = repo.FullName,
            GitHubRepoId = repo.Id,
            Description = repo.Description,
            DefaultBranch = repo.DefaultBranch,
            Language = repo.Language
        };

        return await _projectRepository.CreateAsync(input, cancellationToken);
    }

    public async Task<Result<GitHubIndexingResultDto, GrigoriError>> IndexRepositoryAsync(
        long projectId,
        CancellationToken cancellationToken = default)
    {
        if (!_gitHubService.IsAuthenticated)
        {
            return GrigoriError.ValidationError("GitHub authentication required");
        }

        var sw = Stopwatch.StartNew();

        // Get project
        var projectResult = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (!projectResult.IsSuccess)
        {
            return projectResult.Error;
        }

        var project = projectResult.Value;
        var parts = project.GitHubRepoFullName.Split('/');
        var owner = parts[0];
        var repo = parts[1];

        _logger.LogInformation("Starting indexing for {RepoFullName}", project.GitHubRepoFullName);

        // Update status to indexing
        await _projectRepository.UpdateStatusAsync(projectId, ProjectStatusDto.Indexing, null, cancellationToken);

        string? tempDir = null;

        try
        {
            // Get latest commit SHA
            var commits = await _gitHubService.GetRecentCommitsAsync(owner, repo, 1);
            var commitSha = commits.FirstOrDefault()?.Sha ?? "unknown";

            // Download repository as zipball
            tempDir = Path.Combine(Path.GetTempPath(), $"grigori-index-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var downloadPath = Path.Combine(tempDir, "repo.zip");
            var extractPath = Path.Combine(tempDir, "extracted");

            var downloadSuccess = await _gitHubService.DownloadRepositoryZipAsync(
                owner, repo, project.DefaultBranch, downloadPath, cancellationToken);

            if (!downloadSuccess)
            {
                await _projectRepository.UpdateStatusAsync(projectId, ProjectStatusDto.Failed, "Failed to download repository", cancellationToken);
                return GrigoriError.IndexingFailed(project.GitHubRepoFullName, "Failed to download repository from GitHub");
            }

            // Extract
            ZipFile.ExtractToDirectory(downloadPath, extractPath);

            // Find the extracted folder (GitHub zips have a root folder like owner-repo-sha)
            var extractedFolders = Directory.GetDirectories(extractPath);
            if (extractedFolders.Length == 0)
            {
                await _projectRepository.UpdateStatusAsync(projectId, ProjectStatusDto.Failed, "No files in repository", cancellationToken);
                return GrigoriError.IndexingFailed(project.GitHubRepoFullName, "No files found in downloaded repository");
            }

            var repoPath = extractedFolders[0];

            // Index the repository
            var indexRequest = new IndexRequestDto(repoPath, project.Name);
            var indexResult = await _indexService.IndexDirectoryAsync(indexRequest, cancellationToken);

            if (!indexResult.IsSuccess)
            {
                await _projectRepository.UpdateStatusAsync(projectId, ProjectStatusDto.Failed, indexResult.Error.Message, cancellationToken);
                return indexResult.Error;
            }

            // Update project with stats
            await _projectRepository.UpdateIndexingStatsAsync(
                projectId,
                indexResult.Value.FilesIndexed,
                indexResult.Value.ChunksCreated,
                commitSha,
                cancellationToken);

            await _projectRepository.UpdateStatusAsync(projectId, ProjectStatusDto.Indexed, null, cancellationToken);

            sw.Stop();

            _logger.LogInformation(
                "Completed indexing for {RepoFullName}: {Files} files, {Chunks} chunks in {Duration}ms",
                project.GitHubRepoFullName,
                indexResult.Value.FilesIndexed,
                indexResult.Value.ChunksCreated,
                sw.ElapsedMilliseconds);

            return new GitHubIndexingResultDto
            {
                ProjectId = projectId,
                RepoFullName = project.GitHubRepoFullName,
                FilesIndexed = indexResult.Value.FilesIndexed,
                ChunksCreated = indexResult.Value.ChunksCreated,
                DurationMs = sw.ElapsedMilliseconds,
                CommitSha = commitSha
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index repository {RepoFullName}", project.GitHubRepoFullName);
            await _projectRepository.UpdateStatusAsync(projectId, ProjectStatusDto.Failed, ex.Message, cancellationToken);
            return GrigoriError.IndexingFailed(project.GitHubRepoFullName, ex.Message, ex);
        }
        finally
        {
            // Cleanup temp directory
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp directory: {TempDir}", tempDir);
                }
            }
        }
    }
}
