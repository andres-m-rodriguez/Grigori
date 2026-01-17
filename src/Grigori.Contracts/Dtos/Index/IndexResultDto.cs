namespace Grigori.Contracts.Dtos.Index;

public record IndexResultDto
{
    public required bool Success { get; init; }
    public required int FilesIndexed { get; init; }
    public required int ChunksCreated { get; init; }
    public required long DurationMs { get; init; }
    public string? Message { get; init; }
}
