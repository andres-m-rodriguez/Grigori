namespace Grigori.Contracts.Dtos.Search;

public record SearchRequestDto
{
    public required string Query { get; init; }
    public int Limit { get; init; } = 5;
    public string OutputMode { get; init; } = "full";
    public string? FileTypes { get; init; }
}
