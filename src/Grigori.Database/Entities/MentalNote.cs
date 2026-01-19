namespace Grigori.Database.Entities;

public class MentalNote
{
    public long Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
