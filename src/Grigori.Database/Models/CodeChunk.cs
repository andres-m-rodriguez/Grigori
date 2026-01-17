namespace Grigori.Database.Models;

public record CodeChunk
{
    public required long Id { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public required string ContentHash { get; init; }
    public required byte[] Embedding { get; init; }
    public required DateTime IndexedAt { get; init; }
    public string? Features { get; init; }
}
