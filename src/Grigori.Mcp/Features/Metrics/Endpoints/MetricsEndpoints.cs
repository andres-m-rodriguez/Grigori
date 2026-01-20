using System.ComponentModel;
using System.Text.Json;
using Grigori.Contracts.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Mcp.Features.Metrics.Endpoints;

[McpServerToolType]
public class MetricsEndpoints
{
    private readonly IMetricsService _metricsService;
    private readonly ILogger<MetricsEndpoints> _logger;

    public MetricsEndpoints(IMetricsService metricsService, ILogger<MetricsEndpoints> logger)
    {
        _metricsService = metricsService;
        _logger = logger;
    }

    [McpServerTool(Name = "get_metrics")]
    [Description("Retrieve accumulated metrics from the Grigori MCP server including search stats, indexing stats, embedding stats, and uptime.")]
    public string GetMetrics(
        [Description("If true, clears metrics after reading (default: false)")] bool reset = false)
    {
        _logger.LogInformation("Getting metrics (reset: {Reset})", reset);

        var snapshot = _metricsService.GetSnapshot();

        var response = new
        {
            uptime = new
            {
                totalSeconds = snapshot.Uptime.TotalSeconds,
                formatted = FormatUptime(snapshot.Uptime)
            },
            search = new
            {
                totalSearches = snapshot.SearchStats.TotalSearches,
                cacheHits = snapshot.SearchStats.CacheHits,
                cacheMisses = snapshot.SearchStats.CacheMisses,
                cacheHitRate = snapshot.SearchStats.TotalSearches > 0
                    ? Math.Round((double)snapshot.SearchStats.CacheHits / (snapshot.SearchStats.CacheHits + snapshot.SearchStats.CacheMisses), 4)
                    : 0,
                vectorSearches = snapshot.SearchStats.VectorSearches,
                averageTimeMs = snapshot.SearchStats.AverageTimeMs,
                averageResultCount = snapshot.SearchStats.AverageResultCount
            },
            indexing = new
            {
                totalOperations = snapshot.IndexingStats.TotalOperations,
                filesIndexed = snapshot.IndexingStats.FilesIndexed,
                chunksCreated = snapshot.IndexingStats.ChunksCreated,
                totalTimeMs = snapshot.IndexingStats.TotalTimeMs
            },
            embedding = new
            {
                totalEmbeddings = snapshot.EmbeddingStats.TotalEmbeddings,
                totalTimeMs = snapshot.EmbeddingStats.TotalTimeMs,
                averageTimeMs = snapshot.EmbeddingStats.AverageTimeMs
            }
        };

        if (reset)
        {
            _metricsService.Reset();
            _logger.LogInformation("Metrics reset after reading");
        }

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
        if (uptime.TotalMinutes >= 1)
        {
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        }
        return $"{uptime.Seconds}s";
    }
}
