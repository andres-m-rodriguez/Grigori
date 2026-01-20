namespace Grigori.Contracts.Dtos.Dashboard;

public record IndexedProjectDto(string Path, string Name, int FileCount, int ChunkCount, DateTime LastIndexed, List<string> FileExtensions)
{
    public string FormattedLastIndexed => LastIndexed.ToString("g");

    public string ExtensionsSummary => FileExtensions.Count > 5
        ? string.Join(", ", FileExtensions.Take(5)) + $" +{FileExtensions.Count - 5} more"
        : string.Join(", ", FileExtensions);
}
