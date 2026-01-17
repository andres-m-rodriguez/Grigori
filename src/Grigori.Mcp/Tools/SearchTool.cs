using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grigori.Core.Search;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Mcp.Tools;

[McpServerToolType]
public class SearchTool
{
    private readonly SemanticSearch _search;
    private readonly ILogger<SearchTool> _logger;

    public SearchTool(SemanticSearch search, ILogger<SearchTool> logger)
    {
        _search = search;
        _logger = logger;
    }

    [McpServerTool(Name = "search_code")]
    [Description("Semantic search across indexed codebase. Returns relevant code chunks matching the natural language query.")]
    public async Task<string> SearchCodeAsync(
        [Description("Natural language query describing what you're looking for")] string query,
        [Description("Maximum number of results to return (default: 5)")] int limit = 5,
        [Description("Output mode: 'full' (default) returns all content and metadata, 'compact' strips redundant context prefix lines, 'summary' returns only first meaningful code line + metadata, 'paths' returns only file paths, line numbers, and scores without content")] string outputMode = "full",
        [Description("Optional comma-separated list of file extensions to filter results (e.g., 'cs,ts' or '.cs,.ts'). When provided, only files with these extensions are searched.")] string? fileTypes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query))
        {
            return "Error: Query is required";
        }

        // Normalize output mode to lowercase for comparison
        var mode = (outputMode ?? "full").ToLowerInvariant();
        if (mode != "full" && mode != "compact" && mode != "summary" && mode != "paths")
        {
            return $"Error: Invalid outputMode '{outputMode}'. Valid options are: full, compact, summary, paths";
        }

        // Parse file extensions
        List<string>? fileExtensions = null;
        if (!string.IsNullOrEmpty(fileTypes))
        {
            fileExtensions = fileTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ext => ext.StartsWith('.') ? ext : $".{ext}")
                .ToList();
        }

        _logger.LogInformation("Searching for: {Query} (limit: {Limit}, mode: {Mode}, fileTypes: {FileTypes})",
            query, limit, mode, fileExtensions != null ? string.Join(",", fileExtensions) : "all");

        try
        {
            // Check cache status before search (will be true if query embedding is cached)
            var cacheHit = _search.IsQueryCached(query);

            // Start timing the search
            var stopwatch = Stopwatch.StartNew();
            var results = await _search.SearchAsync(query, limit, fileExtensions, cancellationToken);
            stopwatch.Stop();

            object matches = mode switch
            {
                "paths" => results.Select(r => new
                {
                    filePath = r.Chunk.FilePath,
                    startLine = r.Chunk.StartLine,
                    endLine = r.Chunk.EndLine,
                    score = r.Score
                }).ToList(),

                "summary" => results.Select(r => new
                {
                    filePath = r.Chunk.FilePath,
                    startLine = r.Chunk.StartLine,
                    endLine = r.Chunk.EndLine,
                    content = ExtractFirstMeaningfulLine(r.Chunk.Content),
                    score = r.Score
                }).ToList(),

                "compact" => results.Select(r => new
                {
                    filePath = r.Chunk.FilePath,
                    startLine = r.Chunk.StartLine,
                    endLine = r.Chunk.EndLine,
                    content = StripContextPrefix(r.Chunk.Content),
                    score = r.Score
                }).ToList(),

                _ => results.Select(r => new
                {
                    filePath = r.Chunk.FilePath,
                    startLine = r.Chunk.StartLine,
                    endLine = r.Chunk.EndLine,
                    content = r.Chunk.Content,
                    score = r.Score
                }).ToList()
            };

            var response = JsonSerializer.Serialize(new
            {
                success = true,
                count = results.Count,
                metrics = new
                {
                    durationMs = stopwatch.ElapsedMilliseconds,
                    cacheHit,
                    outputMode = mode,
                    tokenEstimate = 0 // Placeholder, calculated below
                },
                results = matches
            }, new JsonSerializerOptions { WriteIndented = true });

            // Calculate token estimate (total characters / 4) and update the response
            var tokenEstimate = response.Length / 4;
            response = JsonSerializer.Serialize(new
            {
                success = true,
                count = results.Count,
                metrics = new
                {
                    durationMs = stopwatch.ElapsedMilliseconds,
                    cacheHit,
                    outputMode = mode,
                    tokenEstimate
                },
                results = matches
            }, new JsonSerializerOptions { WriteIndented = true });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", query);
            return $"Error: Search failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Strips the context prefix lines (// File:, // Lines:) from content.
    /// </summary>
    private static string StripContextPrefix(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Pattern matches lines starting with // File: or // Lines: at the beginning of content
        var pattern = @"^(//\s*File:.*\r?\n|//\s*Lines:.*\r?\n)+";
        return Regex.Replace(content, pattern, "", RegexOptions.Multiline).TrimStart();
    }

    /// <summary>
    /// Extracts the first meaningful line of code (non-empty, non-comment).
    /// </summary>
    private static string ExtractFirstMeaningfulLine(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Skip single-line comments (C-style, C#, Python, etc.)
            if (trimmed.StartsWith("//") ||
                trimmed.StartsWith("#") ||
                trimmed.StartsWith("*") ||
                trimmed.StartsWith("/*") ||
                trimmed.StartsWith("'''") ||
                trimmed.StartsWith("\"\"\""))
                continue;

            // Skip XML doc comments
            if (trimmed.StartsWith("///"))
                continue;

            // Found a meaningful line
            return trimmed;
        }

        // If no meaningful line found, return first non-empty line
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed;
        }

        return string.Empty;
    }
}
