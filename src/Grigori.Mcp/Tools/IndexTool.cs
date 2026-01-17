using System.ComponentModel;
using Grigori.Core.Search;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Mcp.Tools;

[McpServerToolType]
public class IndexTool
{
    private readonly SemanticSearch _search;
    private readonly ILogger<IndexTool> _logger;

    public IndexTool(SemanticSearch search, ILogger<IndexTool> logger)
    {
        _search = search;
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

        if (!Directory.Exists(path))
        {
            return $"Error: Directory not found: {path}";
        }

        _logger.LogInformation("Indexing directory: {Path}", path);

        try
        {
            await _search.IndexDirectoryAsync(path, cancellationToken);
            var fileCount = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
            return $"Successfully indexed directory: {path} ({fileCount} files)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index directory: {Path}", path);
            return $"Error: Failed to index: {ex.Message}";
        }
    }
}
