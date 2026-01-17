using System.ComponentModel;
using System.Text.Json;
using Grigori.Mcp.Features.Benchmark.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Mcp.Features.Benchmark.Endpoints;

[McpServerToolType]
public class BenchmarkEndpoints
{
    private readonly BenchmarkService _benchmarkService;
    private readonly ILogger<BenchmarkEndpoints> _logger;

    public BenchmarkEndpoints(BenchmarkService benchmarkService, ILogger<BenchmarkEndpoints> logger)
    {
        _benchmarkService = benchmarkService;
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
            return "Error: Query is required";
        }

        if (string.IsNullOrEmpty(directory))
        {
            return "Error: Directory is required";
        }

        if (!Directory.Exists(directory))
        {
            return $"Error: Directory not found: {directory}";
        }

        _logger.LogInformation("Running benchmark: query='{Query}', directory='{Directory}'", query, directory);

        try
        {
            var result = await _benchmarkService.RunBenchmarkAsync(query, directory, cancellationToken);

            var response = new
            {
                semantic = new
                {
                    durationMs = result.SemanticDurationMs,
                    resultCount = result.SemanticResultCount,
                    tokenEstimate = result.SemanticTokenEstimate
                },
                grep = new
                {
                    durationMs = result.GrepDurationMs,
                    resultCount = result.GrepResultCount,
                    tokenEstimate = result.GrepTokenEstimate
                },
                comparison = new
                {
                    speedupFactor = result.SpeedupFactor,
                    tokenSavings = result.GrepTokenEstimate - result.SemanticTokenEstimate
                }
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Benchmark failed");
            return $"Error: Benchmark failed: {ex.Message}";
        }
    }
}
