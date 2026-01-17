using System.ComponentModel;
using System.Text.Json;
using Grigori.Core.Metrics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Grigori.Mcp.Tools;

[McpServerToolType]
public class MetricsTool
{
    private readonly SearchMetrics _metrics;
    private readonly ILogger<MetricsTool> _logger;
    private readonly DateTime _startTime;

    public MetricsTool(SearchMetrics metrics, ILogger<MetricsTool> logger)
    {
        _metrics = metrics;
        _logger = logger;
        _startTime = DateTime.UtcNow;
    }

    [McpServerTool(Name = "get_metrics")]
    [Description("Retrieve accumulated metrics from the Grigori MCP server including search stats, indexing stats, embedding stats, and uptime.")]
    public string GetMetrics(
        [Description("If true, clears metrics after reading (default: false)")] bool reset = false)
    {
        _logger.LogInformation("Retrieving metrics (reset: {Reset})", reset);

        try
        {
            var snapshot = _metrics?.GetSnapshot();
            var uptime = DateTime.UtcNow - _startTime;

            var response = new
            {
                success = true,
                uptime = new
                {
                    totalSeconds = uptime.TotalSeconds,
                    formatted = FormatUptime(uptime)
                },
                searchStats = snapshot != null ? new
                {
                    totalSearches = snapshot.TotalSearches,
                    cacheHits = snapshot.CacheHits,
                    cacheMisses = snapshot.CacheMisses,
                    hnswSearches = snapshot.HnswSearches,
                    averageSearchTimeMs = snapshot.AverageSearchTimeMs,
                    averageResultsReturned = snapshot.AverageResultsReturned,
                    cacheHitRate = snapshot.CacheHitRate,
                    hnswUsageRate = snapshot.HnswIndexUsageRate
                } : null,
                indexingStats = snapshot != null ? new
                {
                    totalOperations = snapshot.TotalIndexingOperations,
                    totalFilesIndexed = snapshot.FilesIndexedCount,
                    totalChunksIndexed = snapshot.ChunksIndexedCount,
                    totalIndexingTimeMs = snapshot.TotalIndexingTimeMs,
                    averageChunksPerFile = snapshot.AverageChunksPerFile
                } : null,
                embeddingStats = snapshot != null ? new
                {
                    totalEmbeddingsGenerated = snapshot.EmbeddingsGeneratedCount,
                    totalEmbeddingTimeMs = snapshot.TotalEmbeddingTimeMs,
                    averageEmbeddingTimeMs = snapshot.AverageEmbeddingTimeMs
                } : null,
                metricsReset = reset
            };

            if (reset && _metrics != null)
            {
                _metrics.Reset();
                _logger.LogInformation("Metrics have been reset");
            }

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve metrics");
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true });
        }
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
