using System.Diagnostics;
using System.Text.RegularExpressions;
using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Mcp.Features.Benchmark.Services;

public class BenchmarkService
{
    private readonly ISearchService _searchService;
    private readonly GrigoriOptions _options;
    private readonly ILogger<BenchmarkService> _logger;

    public BenchmarkService(
        ISearchService searchService,
        IOptions<GrigoriOptions> options,
        ILogger<BenchmarkService> logger)
    {
        _searchService = searchService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BenchmarkResult> RunBenchmarkAsync(
        string query,
        string directory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running benchmark: query='{Query}', directory='{Directory}'", query, directory);

        // Run semantic search
        var semanticStopwatch = Stopwatch.StartNew();
        var searchRequest = new SearchRequestDto
        {
            Query = query,
            Limit = 10,
            OutputMode = "full"
        };
        var semanticResult = await _searchService.SearchAsync(searchRequest, cancellationToken);
        semanticStopwatch.Stop();

        // Run grep/ripgrep search
        var grepStopwatch = Stopwatch.StartNew();
        var grepResults = await RunGrepSearchAsync(query, directory, cancellationToken);
        grepStopwatch.Stop();

        var semanticResultCount = semanticResult.IsSuccess ? semanticResult.Value.Count : 0;
        var semanticContent = semanticResult.IsSuccess
            ? string.Join("\n", semanticResult.Value.Results.Select(r => r.Content))
            : "";

        var grepContent = string.Join("\n", grepResults);

        return new BenchmarkResult
        {
            SemanticDurationMs = semanticStopwatch.ElapsedMilliseconds,
            SemanticResultCount = semanticResultCount,
            SemanticTokenEstimate = semanticContent.Length / 4,
            GrepDurationMs = grepStopwatch.ElapsedMilliseconds,
            GrepResultCount = grepResults.Count,
            GrepTokenEstimate = grepContent.Length / 4,
            SpeedupFactor = grepStopwatch.ElapsedMilliseconds > 0
                ? Math.Round((double)grepStopwatch.ElapsedMilliseconds / Math.Max(1, semanticStopwatch.ElapsedMilliseconds), 2)
                : 0
        };
    }

    private async Task<List<string>> RunGrepSearchAsync(string query, string directory, CancellationToken cancellationToken)
    {
        var results = new List<string>();

        if (!Directory.Exists(directory))
        {
            return results;
        }

        try
        {
            // Extract keywords from query for grep
            var keywords = ExtractKeywords(query);
            if (keywords.Count == 0)
            {
                keywords.Add(query);
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!IsCodeFile(file))
                    continue;

                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    foreach (var keyword in keywords)
                    {
                        if (content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add($"{file}: contains '{keyword}'");
                            break;
                        }
                    }
                }
                catch
                {
                    // Skip unreadable files
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Grep search failed for directory: {Directory}", directory);
        }

        return results;
    }

    private static List<string> ExtractKeywords(string query)
    {
        var keywords = new List<string>();

        // Extract words longer than 3 characters, excluding common stop words
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "are", "but", "not", "you", "all", "can", "had",
            "her", "was", "one", "our", "out", "has", "have", "been", "would",
            "there", "their", "what", "about", "which", "when", "make", "like",
            "time", "just", "know", "take", "come", "could", "good", "some", "them",
            "see", "other", "than", "then", "now", "look", "only", "new", "with",
            "also", "from", "first", "may", "any", "where", "how", "find", "search"
        };

        var matches = Regex.Matches(query, @"\b[a-zA-Z_][a-zA-Z0-9_]{2,}\b");
        foreach (Match match in matches)
        {
            var word = match.Value;
            if (!stopWords.Contains(word))
            {
                keywords.Add(word);
            }
        }

        return keywords.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
    }

    private bool IsCodeFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return _options.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}

public record BenchmarkResult
{
    public long SemanticDurationMs { get; init; }
    public int SemanticResultCount { get; init; }
    public int SemanticTokenEstimate { get; init; }
    public long GrepDurationMs { get; init; }
    public int GrepResultCount { get; init; }
    public int GrepTokenEstimate { get; init; }
    public double SpeedupFactor { get; init; }
}
