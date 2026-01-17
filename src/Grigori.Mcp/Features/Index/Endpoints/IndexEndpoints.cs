using System.ComponentModel;
using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Mcp.Features.Index.Endpoints;

[McpServerToolType]
public class IndexEndpoints
{
    private readonly IIndexService _indexService;
    private readonly ILogger<IndexEndpoints> _logger;

    public IndexEndpoints(IIndexService indexService, ILogger<IndexEndpoints> logger)
    {
        _indexService = indexService;
        _logger = logger;
    }

    [McpServerTool(Name = "index_directory")]
    [Description("Index a directory for semantic code search. Scans all code files and creates embeddings for fast semantic search.")]
    public async Task<string> IndexDirectoryAsync(
        [Description("Absolute path to the directory to index")] string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "Error: Path is required";
        }

        _logger.LogInformation("Indexing directory: {Path}", path);

        var request = new IndexRequestDto { Path = path };
        var result = await _indexService.IndexDirectoryAsync(request, cancellationToken);

        return result.Match(
            success => success.Message ?? $"Successfully indexed {success.FilesIndexed} files with {success.ChunksCreated} chunks in {success.DurationMs}ms",
            error => $"Error: {error.Message}"
        );
    }
}
