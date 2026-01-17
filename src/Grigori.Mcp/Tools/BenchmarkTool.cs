using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Grigori.Core.Search;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Mcp.Tools;

[McpServerToolType]
public class BenchmarkTool
{
    private readonly SemanticSearch _search;
    private readonly ILogger<BenchmarkTool> _logger;

    public BenchmarkTool(SemanticSearch search, ILogger<BenchmarkTool> logger)
    {
        _search = search;
        _logger = logger;
    }

    [McpServerTool(Name = "benchmark_search")]
    [Description("Compares semantic search with traditional grep search. Runs both approaches and returns timing, result counts, and token estimates.")]
    public async Task<string> BenchmarkSearchAsync(
        [Description("The search query to benchmark")] string query,
        [Description("Absolute path to the directory to search")] string directory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query))
        {
            return JsonSerializer.Serialize(new { error = "Query is required" });
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return JsonSerializer.Serialize(new { error = $"Directory not found: {directory}" });
        }

        _logger.LogInformation("Benchmarking search for query: {Query} in directory: {Directory}", query, directory);

        // Run semantic search
        var semanticResult = await RunSemanticSearchAsync(query, cancellationToken);

        // Run traditional search
        var traditionalResult = await RunTraditionalSearchAsync(query, directory, cancellationToken);

        // Calculate comparison metrics
        double speedupFactor = 0;
        double tokenReduction = 0;

        if (traditionalResult.DurationMs > 0 && semanticResult.DurationMs > 0)
        {
            speedupFactor = Math.Round((double)traditionalResult.DurationMs / semanticResult.DurationMs, 1);
        }

        if (traditionalResult.TokenEstimate > 0 && semanticResult.TokenEstimate > 0)
        {
            tokenReduction = Math.Round((1 - (double)semanticResult.TokenEstimate / traditionalResult.TokenEstimate) * 100, 1);
        }

        var response = new
        {
            query,
            directory,
            semantic = new
            {
                durationMs = semanticResult.DurationMs,
                resultCount = semanticResult.ResultCount,
                tokenEstimate = semanticResult.TokenEstimate
            },
            traditional = new
            {
                durationMs = traditionalResult.DurationMs,
                matchingFiles = traditionalResult.MatchingFiles,
                matchingLines = traditionalResult.MatchingLines,
                tokenEstimate = traditionalResult.TokenEstimate,
                tool = traditionalResult.Tool,
                error = traditionalResult.Error
            },
            comparison = new
            {
                speedupFactor,
                tokenReduction
            }
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<SemanticSearchResult> RunSemanticSearchAsync(string query, CancellationToken cancellationToken)
    {
        var result = new SemanticSearchResult();

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var searchResults = await _search.SearchAsync(query, 10, fileExtensions: null, cancellationToken);
            stopwatch.Stop();

            result.DurationMs = stopwatch.ElapsedMilliseconds;
            result.ResultCount = searchResults.Count;

            // Estimate tokens from result content
            var totalContent = new StringBuilder();
            foreach (var r in searchResults)
            {
                totalContent.AppendLine(r.Chunk.FilePath);
                totalContent.AppendLine(r.Chunk.Content);
            }
            result.TokenEstimate = totalContent.Length / 4;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed");
            result.Error = ex.Message;
        }

        return result;
    }

    private async Task<TraditionalSearchResult> RunTraditionalSearchAsync(string query, string directory, CancellationToken cancellationToken)
    {
        var result = new TraditionalSearchResult();

        // Try ripgrep first, then fall back to findstr on Windows
        var (tool, args) = GetSearchCommand(query, directory);
        result.Tool = tool;

        try
        {
            var stopwatch = Stopwatch.StartNew();

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = tool,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait with timeout
            var completed = await WaitForExitAsync(process, TimeSpan.FromSeconds(30), cancellationToken);
            stopwatch.Stop();

            if (!completed)
            {
                try { process.Kill(); } catch { }
                result.Error = "Search timed out after 30 seconds";
                return result;
            }

            result.DurationMs = stopwatch.ElapsedMilliseconds;

            var output = outputBuilder.ToString();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            result.MatchingLines = lines.Length;
            result.MatchingFiles = lines
                .Select(line => ExtractFilePath(line, tool))
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .Count();
            result.TokenEstimate = output.Length / 4;

            // Check if ripgrep was not found
            var error = errorBuilder.ToString();
            if (process.ExitCode != 0 && process.ExitCode != 1 && !string.IsNullOrWhiteSpace(error))
            {
                result.Error = error.Trim();
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            // File not found - tool doesn't exist
            if (tool == "rg")
            {
                _logger.LogWarning("ripgrep (rg) not found, falling back to findstr");
                return await RunFallbackSearchAsync(query, directory, cancellationToken);
            }
            result.Error = $"{tool} not found on this system";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Traditional search failed with {Tool}", tool);
            result.Error = ex.Message;
        }

        return result;
    }

    private async Task<TraditionalSearchResult> RunFallbackSearchAsync(string query, string directory, CancellationToken cancellationToken)
    {
        var result = new TraditionalSearchResult { Tool = "findstr" };

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Use findstr on Windows with /S for recursive and /I for case-insensitive
            var args = $"/S /I /N \"{query}\" *.cs *.ts *.js *.py *.go *.rs *.java *.tsx *.jsx";

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "findstr",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };

            var outputBuilder = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();

            var completed = await WaitForExitAsync(process, TimeSpan.FromSeconds(30), cancellationToken);
            stopwatch.Stop();

            if (!completed)
            {
                try { process.Kill(); } catch { }
                result.Error = "Search timed out after 30 seconds";
                return result;
            }

            result.DurationMs = stopwatch.ElapsedMilliseconds;

            var output = outputBuilder.ToString();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            result.MatchingLines = lines.Length;
            result.MatchingFiles = lines
                .Select(line => ExtractFilePath(line, "findstr"))
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .Count();
            result.TokenEstimate = output.Length / 4;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback search with findstr failed");
            result.Error = ex.Message;
        }

        return result;
    }

    private static (string tool, string args) GetSearchCommand(string query, string directory)
    {
        // Escape the query for command line
        var escapedQuery = query.Replace("\"", "\\\"");

        // Try ripgrep first - it's faster and more reliable
        // -n for line numbers, -i for case insensitive, --type for file types
        var rgArgs = $"-n -i --type cs --type ts --type js --type py --type go --type rust --type java \"{escapedQuery}\" \"{directory}\"";

        return ("rg", rgArgs);
    }

    private static string ExtractFilePath(string line, string tool)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        // ripgrep format: "path:line:content" or "path:line:column:content"
        // findstr format: "path:line:content"
        var colonIndex = line.IndexOf(':');
        if (colonIndex > 0)
        {
            // For Windows paths like C:\path, need to handle drive letter
            if (colonIndex == 1 && line.Length > 2 && line[2] == '\\')
            {
                // This is a Windows path, find the next colon
                colonIndex = line.IndexOf(':', 2);
            }

            if (colonIndex > 0)
            {
                return line.Substring(0, colonIndex);
            }
        }

        return line;
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private class SemanticSearchResult
    {
        public long DurationMs { get; set; }
        public int ResultCount { get; set; }
        public int TokenEstimate { get; set; }
        public string? Error { get; set; }
    }

    private class TraditionalSearchResult
    {
        public long DurationMs { get; set; }
        public int MatchingFiles { get; set; }
        public int MatchingLines { get; set; }
        public int TokenEstimate { get; set; }
        public string? Tool { get; set; }
        public string? Error { get; set; }
    }
}
