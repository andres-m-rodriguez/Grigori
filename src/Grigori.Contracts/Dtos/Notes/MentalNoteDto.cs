namespace Grigori.Contracts.Dtos.Notes;

/// <summary>
/// Data transfer object for mental notes.
/// </summary>
public record MentalNoteDto
{
    public required long Id { get; init; }
    public required string ProjectName { get; init; }
    public required NoteCategory Category { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public List<string> Tags { get; init; } = [];
    public int Priority { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a new mental note.
/// </summary>
public record CreateMentalNoteRequest
{
    public required string ProjectName { get; init; }
    public required NoteCategory Category { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public List<string> Tags { get; init; } = [];
    public int Priority { get; init; } = 0;
}

/// <summary>
/// Request to update an existing mental note.
/// </summary>
public record UpdateMentalNoteRequest
{
    public NoteCategory? Category { get; init; }
    public string? Title { get; init; }
    public string? Content { get; init; }
    public List<string>? Tags { get; init; }
    public int? Priority { get; init; }
}
