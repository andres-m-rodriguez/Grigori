using System.Diagnostics;
using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Contracts.Results;
using Grigori.Database;
using Grigori.Infrastructure.Chunking;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Api.Features.Index;

public class IndexService : IIndexService
{
    private readonly IChunkRepository _chunkRepository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IMetricsService _metricsService;
    private readonly ChunkingService _chunkingService;
    private readonly GrigoriOptions _options;
    private readonly ILogger<IndexService> _logger;

    public IndexService(
        IChunkRepository chunkRepository,
        IEmbeddingProvider embeddingProvider,
        IMetricsService metricsService,
        ChunkingService chunkingService,
        IOptions<GrigoriOptions> options,
        ILogger<IndexService> logger)
    {
        _chunkRepository = chunkRepository;
        _embeddingProvider = embeddingProvider;
        _metricsService = metricsService;
        _chunkingService = chunkingService;
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

            var files = Directory.EnumerateFiles(request.Path, "*", SearchOption.AllDirectories)
                .Where(file => IsCodeFile(file))
                .Where(file =>
                {
                    var relativePath = Path.GetRelativePath(request.Path, file).Replace('\\', '/');
                    return !excludeMatcher.Match(relativePath).HasMatches;
                })
                .ToList();

            _logger.LogInformation("Indexing {FileCount} files from {Directory}", files.Count, request.Path);

            var totalFilesIndexed = 0;
            var totalChunksIndexed = 0;

            foreach (var file in files)
            {
                try
                {
                    var result = await IndexFileCoreAsync(file, cancellationToken);
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
                _metricsService.RecordIndexing(stopwatch.ElapsedMilliseconds, totalFilesIndexed, totalChunksIndexed);
            }

            return new IndexResultDto
            {
                Success = true,
                FilesIndexed = totalFilesIndexed,
                ChunksCreated = totalChunksIndexed,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = $"Indexed {totalFilesIndexed} files with {totalChunksIndexed} chunks"
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
            var chunkCount = await IndexFileCoreAsync(filePath, cancellationToken);
            if (chunkCount.IsFailure)
            {
                return chunkCount.Error;
            }

            stopwatch.Stop();
            _metricsService.RecordIndexing(stopwatch.ElapsedMilliseconds, 1, chunkCount.Value);

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

    private async Task<Result<int, GrigoriError>> IndexFileCoreAsync(string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var contentHash = GrigoriDbContext.ComputeHash(content);

        if (await _chunkRepository.HasContentHashAsync(filePath, contentHash, cancellationToken))
        {
            _logger.LogDebug("File already indexed: {FilePath}", filePath);
            return 0;
        }

        await _chunkRepository.DeleteByFilePathAsync(filePath, cancellationToken);

        var chunks = _chunkingService.ChunkFile(filePath, content);
        if (chunks.Count == 0)
        {
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

        _logger.LogInformation("Indexed {ChunkCount} chunks from {FilePath}", chunks.Count, filePath);
        return chunks.Count;
    }

    private bool IsCodeFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return _options.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
