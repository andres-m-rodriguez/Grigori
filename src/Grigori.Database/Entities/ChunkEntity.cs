using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace Grigori.Database.Entities;

[Table("chunks")]
public class ChunkEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [Column("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [Column("start_line")]
    public int StartLine { get; set; }

    [Column("end_line")]
    public int EndLine { get; set; }

    [Required]
    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Required]
    [Column("content_hash")]
    public string ContentHash { get; set; } = string.Empty;

    [Required]
    [Column("embedding", TypeName = "vector(384)")]
    public Vector Embedding { get; set; } = null!;

    [Column("features")]
    public string? Features { get; set; }

    [Column("indexed_at")]
    public DateTime IndexedAt { get; set; }
}
