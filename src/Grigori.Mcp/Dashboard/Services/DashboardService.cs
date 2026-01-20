using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Grigori.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.Mcp.Dashboard.Services;

public class DashboardService
{
    private readonly GrigoriDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;

    // Lazy-load services to avoid blocking page render
    private ISearchService? _searchService;
    private ISearchService SearchService =>
        _searchService ??= _serviceProvider.GetRequiredService<ISearchService>();

    public DashboardService(
        GrigoriDbContext dbContext,
        IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
    }

    public async Task<IndexStats> GetIndexStatsAsync()
    {
        var totalChunks = await _dbContext.Chunks.CountAsync();
        var uniqueFiles = await _dbContext.Chunks.Select(c => c.FilePath).Distinct().CountAsync();
        var totalSize = await _dbContext.Chunks.SumAsync(c => c.Content.Length);
        var lastUpdated = totalChunks > 0
            ? await _dbContext.Chunks.MaxAsync(c => c.IndexedAt)
            : (DateTime?)null;

        return new IndexStats
        {
            TotalChunks = totalChunks,
            TotalFiles = uniqueFiles,
            IndexSizeBytes = totalSize,
            VectorIndexEnabled = true, // pgvector HNSW is always enabled
            VectorCount = totalChunks,
            LastUpdated = lastUpdated
        };
    }

    public async Task<List<IndexedProject>> GetIndexedProjectsAsync()
    {
        var chunks = await _dbContext.Chunks.ToListAsync();

        if (chunks.Count == 0)
            return [];

        // Group files by their root project directory
        var filesByProject = chunks
            .GroupBy(c => GetProjectRoot(c.FilePath))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new IndexedProject
            {
                Path = g.Key,
                Name = Path.GetFileName(g.Key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                FileCount = g.Select(c => c.FilePath).Distinct().Count(),
                ChunkCount = g.Count(),
                LastIndexed = g.Max(c => c.IndexedAt),
                FileExtensions = g
                    .Select(c => Path.GetExtension(c.FilePath).ToLowerInvariant())
                    .Where(ext => !string.IsNullOrEmpty(ext))
                    .Distinct()
                    .OrderBy(ext => ext)
                    .ToList()
            })
            .OrderBy(p => p.Name)
            .ToList();

        return filesByProject;
    }

    private static string GetProjectRoot(string filePath)
    {
        // Normalize path separators
        var normalizedPath = filePath.Replace('/', Path.DirectorySeparatorChar);

        // Common project indicators
        string[] projectIndicators = [".git", ".sln", ".slnx", ".csproj", "package.json", "Cargo.toml", "go.mod", "pyproject.toml", ".gitignore"];

        var directory = Path.GetDirectoryName(normalizedPath);
        var bestMatch = directory;

        while (!string.IsNullOrEmpty(directory))
        {
            // Check if this directory looks like a project root
            var dirName = Path.GetFileName(directory);

            // Skip common non-project directories
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

    public async Task<List<SearchResult>> SearchAsync(string query, int limit = 10, string searchMode = "hybrid")
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Use the shared SearchService for consistent search behavior
        var request = new SearchRequestDto
        {
            Query = query,
            Limit = limit,
            SearchMode = searchMode,
            OutputMode = "full"
        };

        var result = await SearchService.SearchAsync(request);

        if (result.IsFailure)
            return [];

        return result.Value.Results.Select(r => new SearchResult
        {
            FilePath = r.FilePath,
            StartLine = r.StartLine,
            EndLine = r.EndLine,
            Content = r.Content,
            Score = r.Score
        }).ToList();
    }

    public async Task<List<ActivityLogDto>> GetRecentActivityAsync(int limit = 20)
    {
        var logs = await _dbContext.ActivityLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();

        return logs.Select(l => new ActivityLogDto
        {
            Id = l.Id,
            ActivityType = l.ActivityType,
            Description = l.Description,
            DurationMs = l.DurationMs,
            Timestamp = l.Timestamp,
            Details = l.Details
        }).ToList();
    }

    public async Task<List<SearchHistoryDto>> GetSearchHistoryAsync(int limit = 20)
    {
        var history = await _dbContext.SearchHistory
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToListAsync();

        return history.Select(h => new SearchHistoryDto
        {
            Id = h.Id,
            Query = h.Query,
            ResultCount = h.ResultCount,
            DurationMs = h.DurationMs,
            CacheHit = h.CacheHit,
            UsedHnsw = h.UsedPgvector,
            Timestamp = h.Timestamp
        }).ToList();
    }

    public async Task<List<PerformanceDataPointDto>> GetPerformanceMetricsAsync(int hoursBack = 24)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hoursBack);

        // Get activity logs for the time period
        var logs = await _dbContext.ActivityLogs
            .Where(l => l.Timestamp >= cutoff)
            .ToListAsync();

        // Group by hour and calculate averages in memory
        var metricsLookup = logs
            .GroupBy(l => new DateTime(l.Timestamp.Year, l.Timestamp.Month, l.Timestamp.Day, l.Timestamp.Hour, 0, 0, DateTimeKind.Utc))
            .ToDictionary(
                g => g.Key.ToString("yyyy-MM-dd HH:00"),
                g => (
                    AvgSearchTimeMs: g.Where(x => x.ActivityType == "search").Select(x => x.DurationMs).DefaultIfEmpty(0).Average(),
                    AvgIndexTimeMs: g.Where(x => x.ActivityType == "index").Select(x => x.DurationMs).DefaultIfEmpty(0).Average()
                ));

        // Generate all hours in the range
        var result = new List<PerformanceDataPointDto>();
        var now = DateTime.UtcNow;
        var startHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc)
            .AddHours(-hoursBack + 1);

        for (int i = 0; i < hoursBack; i++)
        {
            var hour = startHour.AddHours(i);
            var key = hour.ToString("yyyy-MM-dd HH:00");

            if (metricsLookup.TryGetValue(key, out var data))
            {
                result.Add(new PerformanceDataPointDto
                {
                    Timestamp = hour,
                    AvgSearchTimeMs = data.AvgSearchTimeMs,
                    AvgIndexTimeMs = data.AvgIndexTimeMs
                });
            }
            else
            {
                result.Add(new PerformanceDataPointDto
                {
                    Timestamp = hour,
                    AvgSearchTimeMs = 0,
                    AvgIndexTimeMs = 0
                });
            }
        }

        return result;
    }
}

public class IndexStats
{
    public int TotalChunks { get; set; }
    public int TotalFiles { get; set; }
    public long IndexSizeBytes { get; set; }
    public bool VectorIndexEnabled { get; set; }
    public int VectorCount { get; set; }
    public DateTime? LastUpdated { get; set; }

    public string FormattedSize => IndexSizeBytes switch
    {
        < 1024 => $"{IndexSizeBytes} B",
        < 1024 * 1024 => $"{IndexSizeBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{IndexSizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{IndexSizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string FormattedLastUpdated => LastUpdated?.ToString("g") ?? "Never";
}

public class SearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }

    public string FormattedScore => $"{Score:P0}";
    public string LineRange => StartLine == EndLine
        ? $"Line {StartLine}"
        : $"Lines {StartLine}-{EndLine}";

    public string FileName => Path.GetFileName(FilePath);
    public string FileExtension => Path.GetExtension(FilePath).TrimStart('.').ToLowerInvariant();
    public string ProjectName => FilePath.Contains('/') ? FilePath.Split('/')[0] : Path.GetDirectoryName(FilePath)?.Split(Path.DirectorySeparatorChar).FirstOrDefault() ?? "";
    public string RelativePath => FilePath.Contains('/') && FilePath.IndexOf('/') < FilePath.Length - 1
        ? FilePath[(FilePath.IndexOf('/') + 1)..]
        : FilePath;

    // Language for syntax highlighting based on file extension
    public string Language => FileExtension switch
    {
        "cs" => "csharp",
        "js" => "javascript",
        "ts" => "typescript",
        "py" => "python",
        "rb" => "ruby",
        "go" => "go",
        "rs" => "rust",
        "java" => "java",
        "cpp" or "cc" or "cxx" => "cpp",
        "c" or "h" => "c",
        "html" or "htm" => "html",
        "css" => "css",
        "json" => "json",
        "xml" => "xml",
        "yaml" or "yml" => "yaml",
        "md" => "markdown",
        "sql" => "sql",
        "sh" or "bash" => "bash",
        "ps1" => "powershell",
        "razor" => "cshtml-razor",
        _ => "plaintext"
    };
}

public class IndexedProject
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public int ChunkCount { get; set; }
    public DateTime LastIndexed { get; set; }
    public List<string> FileExtensions { get; set; } = [];

    public string FormattedLastIndexed => LastIndexed.ToString("g");
    public string ExtensionsSummary => FileExtensions.Count > 5
        ? string.Join(", ", FileExtensions.Take(5)) + $" +{FileExtensions.Count - 5} more"
        : string.Join(", ", FileExtensions);
}
