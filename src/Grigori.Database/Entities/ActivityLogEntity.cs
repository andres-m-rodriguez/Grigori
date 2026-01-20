using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Grigori.Database.Entities;

[Table("activity_log")]
public class ActivityLogEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [Column("activity_type")]
    public string ActivityType { get; set; } = string.Empty;

    [Required]
    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("duration_ms")]
    public long DurationMs { get; set; }

    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    [Column("details")]
    public string? Details { get; set; }
}
