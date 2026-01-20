using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Grigori.Database.Entities;

[Table("search_history")]
public class SearchHistoryEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [Column("query")]
    public string Query { get; set; } = string.Empty;

    [Column("result_count")]
    public int ResultCount { get; set; }

    [Column("duration_ms")]
    public long DurationMs { get; set; }

    [Column("cache_hit")]
    public bool CacheHit { get; set; }

    [Column("used_pgvector")]
    public bool UsedPgvector { get; set; }

    [Column("timestamp")]
    public DateTime Timestamp { get; set; }
}
