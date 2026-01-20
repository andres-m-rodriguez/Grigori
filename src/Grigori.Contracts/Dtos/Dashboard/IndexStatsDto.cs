namespace Grigori.Contracts.Dtos.Dashboard;

public record IndexStatsDto(int TotalChunks, int TotalFiles, long IndexSizeBytes, bool VectorIndexEnabled, int VectorCount, DateTime? LastUpdated)
{
    public string FormattedSize => IndexSizeBytes switch
    {
        < 1024 => $"{IndexSizeBytes} B",
        < 1024 * 1024 => $"{IndexSizeBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{IndexSizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{IndexSizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string FormattedLastUpdated => LastUpdated?.ToString("g") ?? "Never";
}
