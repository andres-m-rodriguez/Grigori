using Grigori.Contracts.Dtos.Metrics;
using Grigori.Contracts.Interfaces;
using Grigori.Database;
using Grigori.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.Mcp.Dashboard.Services;

public class DashboardService
{
    private readonly GrigoriDbContext _dbContext;
    private readonly HnswIndex _hnswIndex;
    private readonly IServiceProvider _serviceProvider;

    // Lazy-load embedding provider to avoid blocking page render
    private IEmbeddingProvider? _embeddingProvider;
    private IEmbeddingProvider EmbeddingProvider =>
        _embeddingProvider ??= _serviceProvider.GetRequiredService<IEmbeddingProvider>();

    public DashboardService(
        GrigoriDbContext dbContext,
        HnswIndex hnswIndex,
        IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _hnswIndex = hnswIndex;
        _serviceProvider = serviceProvider;
    }

    public async Task<IndexStats> GetIndexStatsAsync()
    {
        var chunks = await _dbContext.GetAllChunksAsync();

        var uniqueFiles = chunks
            .Select(c => c.FilePath)
            .Distinct()
            .Count();

        var totalSize = chunks.Sum(c => c.Content?.Length ?? 0);

        return new IndexStats
        {
            TotalChunks = chunks.Count,
            TotalFiles = uniqueFiles,
            IndexSizeBytes = totalSize,
            HnswBuilt = _hnswIndex.IsBuilt,
            HnswVectorCount = _hnswIndex.Count,
            LastUpdated = chunks.Count > 0
                ? chunks.Max(c => c.IndexedAt)
                : null
        };
    }

    public async Task<List<IndexedProject>> GetIndexedProjectsAsync()
    {
        var chunks = await _dbContext.GetAllChunksAsync();

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

    public async Task<List<SearchResult>> SearchAsync(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Generate embedding for query (lazy-loads embedding provider on first search)
        var embeddingResult = await EmbeddingProvider.GetEmbeddingAsync(
            query,
            EmbeddingInputType.Query);

        if (embeddingResult.IsFailure)
            return [];

        var queryEmbedding = embeddingResult.Value;

        // Search using HNSW if available
        if (_hnswIndex.IsBuilt)
        {
            var searchResults = _hnswIndex.Search(queryEmbedding, limit);

            // Fetch chunk details
            var chunks = await _dbContext.GetAllChunksAsync();
            var chunkDict = chunks.ToDictionary(c => c.Id);

            return searchResults
                .Where(r => chunkDict.ContainsKey(r.Id))
                .Select(r =>
                {
                    var chunk = chunkDict[r.Id];
                    return new SearchResult
                    {
                        FilePath = chunk.FilePath,
                        StartLine = chunk.StartLine,
                        EndLine = chunk.EndLine,
                        Content = chunk.Content,
                        Score = HnswIndex.DistanceToSimilarity(r.Distance)
                    };
                })
                .ToList();
        }

        // Fallback to linear scan
        var allChunks = await _dbContext.GetAllChunksAsync();
        return allChunks
            .Select(c => new
            {
                Chunk = c,
                Embedding = DeserializeEmbedding(c.Embedding)
            })
            .Select(x => new SearchResult
            {
                FilePath = x.Chunk.FilePath,
                StartLine = x.Chunk.StartLine,
                EndLine = x.Chunk.EndLine,
                Content = x.Chunk.Content,
                Score = CosineSimilarity(queryEmbedding, x.Embedding)
            })
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    private static float[] DeserializeEmbedding(byte[] bytes)
    {
        // Detect format - common dimensions for float32
        int[] commonDimensions = [384, 768, 1024, 1536];
        var floatCount = bytes.Length / sizeof(float);

        if (Array.Exists(commonDimensions, d => d == floatCount))
        {
            // Float32 format
            var embedding = new float[floatCount];
            Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
            return embedding;
        }

        // Int8 quantized format
        var scale = BitConverter.ToSingle(bytes, 0);
        var zeroPoint = BitConverter.ToSingle(bytes, 4);
        var result = new float[bytes.Length - 8];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (bytes[8 + i] - zeroPoint) * scale;
        }

        return result;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0f;

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0 ? dot / denom : 0f;
    }

    public async Task<List<ActivityLogDto>> GetRecentActivityAsync(int limit = 20)
    {
        var logs = await _dbContext.GetRecentActivityLogsAsync(limit);
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
        var history = await _dbContext.GetRecentSearchHistoryAsync(limit);
        return history.Select(h => new SearchHistoryDto
        {
            Id = h.Id,
            Query = h.Query,
            ResultCount = h.ResultCount,
            DurationMs = h.DurationMs,
            CacheHit = h.CacheHit,
            UsedHnsw = h.UsedHnsw,
            Timestamp = h.Timestamp
        }).ToList();
    }

    public async Task<List<PerformanceDataPointDto>> GetPerformanceMetricsAsync(int hoursBack = 24)
    {
        var metrics = await _dbContext.GetHourlyPerformanceMetricsAsync(hoursBack);
        return metrics.Select(m => new PerformanceDataPointDto
        {
            Timestamp = m.Hour,
            AvgSearchTimeMs = m.AvgSearchTimeMs,
            AvgIndexTimeMs = m.AvgIndexTimeMs
        }).ToList();
    }
}

public class IndexStats
{
    public int TotalChunks { get; set; }
    public int TotalFiles { get; set; }
    public long IndexSizeBytes { get; set; }
    public bool HnswBuilt { get; set; }
    public int HnswVectorCount { get; set; }
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
