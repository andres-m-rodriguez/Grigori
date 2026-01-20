using Grigori.Contracts.Dtos.Dashboard;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Results;
using Grigori.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Grigori.DataAccess.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly GrigoriDbContext _dbContext;
    private readonly ILogger<DashboardRepository> _logger;

    public DashboardRepository(GrigoriDbContext dbContext, ILogger<DashboardRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<IndexStatsDto, GrigoriError>> GetIndexStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var totalChunks = await _dbContext.Chunks.CountAsync(cancellationToken);
            var uniqueFiles = await _dbContext.Chunks.Select(c => c.FilePath).Distinct().CountAsync(cancellationToken);
            var totalSize = totalChunks > 0
                ? await _dbContext.Chunks.SumAsync(c => (long)c.Content.Length, cancellationToken)
                : 0;
            var lastUpdated = totalChunks > 0
                ? await _dbContext.Chunks.MaxAsync(c => c.IndexedAt, cancellationToken)
                : (DateTime?)null;

            return new IndexStatsDto(totalChunks, uniqueFiles, totalSize, true, totalChunks, lastUpdated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get index stats");
            return GrigoriError.DatabaseError($"Failed to get index stats: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<IndexedProjectDto>, GrigoriError>> GetIndexedProjectsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var chunks = await _dbContext.Chunks
                .Select(c => new { c.FilePath, c.IndexedAt })
                .ToListAsync(cancellationToken);

            if (chunks.Count == 0)
                return new List<IndexedProjectDto>();

            var projects = chunks
                .GroupBy(c => GetProjectRoot(c.FilePath))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g => new IndexedProjectDto(
                    g.Key,
                    Path.GetFileName(g.Key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    g.Select(c => c.FilePath).Distinct().Count(),
                    g.Count(),
                    g.Max(c => c.IndexedAt),
                    g.Select(c => Path.GetExtension(c.FilePath).ToLowerInvariant())
                        .Where(ext => !string.IsNullOrEmpty(ext))
                        .Distinct()
                        .OrderBy(ext => ext)
                        .ToList()))
                .OrderBy(p => p.Name)
                .ToList();

            return projects;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get indexed projects");
            return GrigoriError.DatabaseError($"Failed to get indexed projects: {ex.Message}", ex);
        }
    }

    private static string GetProjectRoot(string filePath)
    {
        var normalizedPath = filePath.Replace('/', Path.DirectorySeparatorChar);
        var directory = Path.GetDirectoryName(normalizedPath);
        var bestMatch = directory;

        while (!string.IsNullOrEmpty(directory))
        {
            var dirName = Path.GetFileName(directory);

            if (dirName is "src" or "lib" or "app" or "apps" or "packages" or "projects")
            {
                var parent = Path.GetDirectoryName(directory);
                if (!string.IsNullOrEmpty(parent))
                {
                    bestMatch = parent;
                }
            }
            else if (!dirName.StartsWith('.'))
            {
                bestMatch = directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return bestMatch ?? filePath;
    }
}
