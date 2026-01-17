namespace Grigori.Contracts.Dtos.Search;

public record CodeChunkDto
{
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public required float Score { get; init; }
}
