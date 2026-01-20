using System.ComponentModel;
using System.Text.Json;
using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Mcp.Features.Search.Endpoints;

[McpServerToolType]
public class SearchEndpoints
{
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchEndpoints> _logger;

    public SearchEndpoints(ISearchService searchService, ILogger<SearchEndpoints> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    [McpServerTool(Name = "search_code")]
    [Description("Semantic search across indexed codebase. Returns relevant code chunks matching the natural language query.")]
    public async Task<string> SearchCodeAsync(
        [Description("Natural language query describing what you're looking for")] string query,
        [Description("Maximum number of results to return (default: 5)")] int limit = 5,
        [Description("Output mode: 'full' (default) returns all content and metadata, 'compact' strips redundant context prefix lines, 'summary' returns only first meaningful code line + metadata, 'paths' returns only file paths, line numbers, and scores without content")] string outputMode = "full",
        [Description("Optional comma-separated list of file extensions to filter results (e.g., 'cs,ts' or '.cs,.ts'). When provided, only files with these extensions are searched.")] string? fileTypes = null,
        [Description("Search mode: 'hybrid' (default) combines semantic and keyword search for best results, 'semantic' uses only vector similarity, 'lexical' uses only BM25 keyword matching")] string searchMode = "hybrid",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query))
        {
            return "Error: Query is required";
        }

        var mode = (outputMode ?? "full").ToLowerInvariant();
        if (mode != "full" && mode != "compact" && mode != "summary" && mode != "paths")
        {
            return $"Error: Invalid outputMode '{outputMode}'. Valid options are: full, compact, summary, paths";
        }

        var search = (searchMode ?? "hybrid").ToLowerInvariant();
        if (search != "hybrid" && search != "semantic" && search != "lexical")
        {
            return $"Error: Invalid searchMode '{searchMode}'. Valid options are: hybrid, semantic, lexical";
        }

        _logger.LogInformation("Searching for: {Query} (limit: {Limit}, mode: {Mode}, searchMode: {SearchMode}, fileTypes: {FileTypes})",
            query, limit, mode, search, fileTypes ?? "all");

        var request = new SearchRequestDto(query, limit, mode, fileTypes, search);

        var result = await _searchService.SearchAsync(request, cancellationToken);

        return result.Match(
            success => JsonSerializer.Serialize(new
            {
                success = true,
                count = success.Count,
                metrics = new
                {
                    durationMs = success.Metrics.DurationMs,
                    cacheHit = success.Metrics.CacheHit,
                    outputMode = success.Metrics.OutputMode,
                    searchMode = success.Metrics.SearchMode,
                    tokenEstimate = success.Metrics.TokenEstimate
                },
                results = mode == "paths"
                    ? (object)success.Results.Select(r => new { filePath = r.FilePath, startLine = r.StartLine, endLine = r.EndLine, score = r.Score }).ToList()
                    : success.Results.Select(r => new { filePath = r.FilePath, startLine = r.StartLine, endLine = r.EndLine, content = r.Content, score = r.Score }).ToList()
            }, new JsonSerializerOptions { WriteIndented = true }),
            error => $"Error: {error.Message}"
        );
    }
}
