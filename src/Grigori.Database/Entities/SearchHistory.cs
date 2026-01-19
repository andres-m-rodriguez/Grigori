namespace Grigori.Database.Entities;

public class SearchHistory
{
    public long Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public int ResultCount { get; set; }
    public long DurationMs { get; set; }
    public bool CacheHit { get; set; }
    public bool UsedHnsw { get; set; }
    public DateTime Timestamp { get; set; }
}
