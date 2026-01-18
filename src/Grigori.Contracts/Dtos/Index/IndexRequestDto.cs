namespace Grigori.Contracts.Dtos.Index;

public record IndexRequestDto
{
    public required string Path { get; init; }

    /// <summary>
    /// Optional project name to use for file paths instead of deriving from the directory.
    /// When set, file paths will be stored as "ProjectName/relative/path" instead of full paths.
    /// </summary>
    public string? ProjectName { get; init; }
}
