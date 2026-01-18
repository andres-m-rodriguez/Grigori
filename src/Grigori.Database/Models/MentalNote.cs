namespace Grigori.Database.Models;

public record MentalNote
{
    public required long Id { get; init; }
    public required string ProjectName { get; init; }
    public required int Category { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public string? Tags { get; init; }
    public int Priority { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
