using System.Diagnostics;
using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Grigori.Database;
using Grigori.Infrastructure.Chunking;
using Grigori.Infrastructure.Indexing;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Mcp.Features.Index.Services;

public class IndexService : IIndexService
{
    private readonly IChunkRepository _chunkRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IMetricsService _metricsService;
    private readonly ChunkingService _chunkingService;
    private readonly HnswIndex _hnswIndex;
    private readonly GrigoriOptions _options;
    private readonly ILogger<IndexService> _logger;

    public IndexService(
        IChunkRepository chunkRepository,
        IEmbeddingProvider embeddingProvider,
        IMetricsService metricsService,
        ChunkingService chunkingService,
        HnswIndex hnswIndex,
        IOptions<GrigoriOptions> options,
        ILogger<IndexService> logger)
    {
        _chunkRepository = chunkRepository;
        _embeddingProvider = embeddingProvider;
        _metricsService = metricsService;
        _chunkingService = chunkingService;
        _hnswIndex = hnswIndex;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<IndexResultDto, GrigoriError>> IndexDirectoryAsync(
        IndexRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(request.Path))
        {
            return GrigoriError.DirectoryNotFound(request.Path);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var excludeMatcher = new Matcher();
            foreach (var pattern in _options.ExcludePatterns)
            {
                excludeMatcher.AddInclude(pattern);
            }

            var allFiles = Directory.EnumerateFiles(request.Path, "*", SearchOption.AllDirectories);
            var files = new List<string>();
            var excludedCount = 0;

            foreach (var file in allFiles)
            {
                if (!IsCodeFile(file))
                    continue;

                var relativePath = Path.GetRelativePath(request.Path, file).Replace('\\', '/');

                if (excludeMatcher.Match(relativePath).HasMatches)
                {
                    excludedCount++;
                    continue;
                }

                files.Add(file);
            }

            _logger.LogInformation("Indexing {FileCount} files from {Directory} (excluded {ExcludedCount} files)",
                files.Count, request.Path, excludedCount);

            var totalFilesIndexed = 0;
            var totalChunksIndexed = 0;

            foreach (var file in files)
            {
                try
                {
                    // If ProjectName is set, use it to create a clean path like "ProjectName/relative/path"
                    var storedPath = file;
                    if (!string.IsNullOrEmpty(request.ProjectName))
                    {
                        var relativePath = Path.GetRelativePath(request.Path, file).Replace('\\', '/');
                        storedPath = $"{request.ProjectName}/{relativePath}";
                    }

                    var result = await IndexFileCoreAsync(file, storedPath, cancellationToken);
                    if (result.IsSuccess && result.Value > 0)
                    {
                        totalFilesIndexed++;
                        totalChunksIndexed += result.Value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to index file: {FilePath}", file);
                }
            }

            stopwatch.Stop();
            if (totalFilesIndexed > 0)
            {
                await _metricsService.RecordIndexingAsync(request.Path, stopwatch.ElapsedMilliseconds, totalFilesIndexed, totalChunksIndexed, cancellationToken);

                // Rebuild HNSW index if enabled
                if (_options.Hnsw.Enabled)
                {
                    await RebuildHnswIndexAsync(cancellationToken);
                }
            }

            return new IndexResultDto
            {
                Success = true,
                FilesIndexed = totalFilesIndexed,
                ChunksCreated = totalChunksIndexed,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = $"Indexed {totalFilesIndexed} files with {totalChunksIndexed} chunks from {request.Path}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Directory indexing failed: {Path}", request.Path);
            return GrigoriError.IndexingFailed(request.Path, ex.Message, ex);
        }
    }

    public async Task<Result<IndexResultDto, GrigoriError>> IndexFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return GrigoriError.FileNotFound(filePath);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var chunkCount = await IndexFileCoreAsync(filePath, filePath, cancellationToken);
            if (chunkCount.IsFailure)
            {
                return chunkCount.Error;
            }

            stopwatch.Stop();
            await _metricsService.RecordIndexingAsync(filePath, stopwatch.ElapsedMilliseconds, 1, chunkCount.Value, cancellationToken);

            // Rebuild HNSW index if enabled
            if (_options.Hnsw.Enabled && chunkCount.Value > 0)
            {
                await RebuildHnswIndexAsync(cancellationToken);
            }

            return new IndexResultDto
            {
                Success = true,
                FilesIndexed = 1,
                ChunksCreated = chunkCount.Value,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = $"Indexed {chunkCount.Value} chunks from {filePath}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File indexing failed: {FilePath}", filePath);
            return GrigoriError.IndexingFailed(filePath, ex.Message, ex);
        }
    }

    public async Task<Result<bool, GrigoriError>> RemoveFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var result = await _chunkRepository.DeleteByFilePathAsync(filePath, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        _logger.LogInformation("Removed from index: {FilePath}", filePath);
        return result.Value;
    }

    private async Task<Result<int, GrigoriError>> IndexFileCoreAsync(string filePath, string storedPath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var contentHash = GrigoriDbContext.ComputeHash(content);

        if (await _chunkRepository.HasContentHashAsync(storedPath, contentHash, cancellationToken))
        {
            _logger.LogDebug("File already indexed with same content: {FilePath}", storedPath);
            return 0;
        }

        await _chunkRepository.DeleteByFilePathAsync(storedPath, cancellationToken);

        // Use storedPath for the chunks so the database has clean paths
        var chunks = _chunkingService.ChunkFile(storedPath, content);
        if (chunks.Count == 0)
        {
            _logger.LogDebug("No chunks created for: {FilePath}", storedPath);
            return 0;
        }

        var contents = chunks.Select(c => c.Content).ToList();
        var embeddingStopwatch = Stopwatch.StartNew();
        var embeddingsResult = await _embeddingProvider.GetEmbeddingsAsync(contents, EmbeddingInputType.Document, cancellationToken);
        embeddingStopwatch.Stop();

        if (embeddingsResult.IsFailure)
        {
            return embeddingsResult.Error;
        }

        _metricsService.RecordEmbeddingGeneration(embeddingStopwatch.ElapsedMilliseconds, contents.Count);

        var insertResult = await _chunkRepository.InsertChunksBatchAsync(chunks, embeddingsResult.Value, cancellationToken);
        if (insertResult.IsFailure)
        {
            return insertResult.Error;
        }

        _logger.LogInformation("Indexed {ChunkCount} chunks from {FilePath}", chunks.Count, storedPath);
        return chunks.Count;
    }

    private bool IsCodeFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return _options.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private async Task RebuildHnswIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var embeddings = await _chunkRepository.GetAllEmbeddingsAsync(cancellationToken);
            var embeddingList = embeddings.ToList();

            if (embeddingList.Count == 0)
            {
                _logger.LogDebug("No embeddings found, skipping HNSW index rebuild");
                return;
            }

            _hnswIndex.BuildIndex(embeddingList);
            stopwatch.Stop();

            _logger.LogInformation("Rebuilt HNSW index with {Count} vectors in {ElapsedMs}ms",
                embeddingList.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild HNSW index");
        }
    }
}
