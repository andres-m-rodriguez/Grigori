namespace Grigori.Database.Entities;

public class CodeChunk
{
    public long Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public byte[] Embedding { get; set; } = [];
    public string? Features { get; set; }
    public DateTime IndexedAt { get; set; }
}
