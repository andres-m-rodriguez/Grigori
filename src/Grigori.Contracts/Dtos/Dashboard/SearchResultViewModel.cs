namespace Grigori.Contracts.Dtos.Dashboard;

public record SearchResultViewModel(string FilePath, int StartLine, int EndLine, string Content, float Score)
{
    public string FormattedScore => $"{Score:P0}";

    public string LineRange => StartLine == EndLine
        ? $"Line {StartLine}"
        : $"Lines {StartLine}-{EndLine}";

    public string FileName => Path.GetFileName(FilePath);

    public string FileExtension => Path.GetExtension(FilePath).TrimStart('.').ToLowerInvariant();

    public string ProjectName => FilePath.Contains('/')
        ? FilePath.Split('/')[0]
        : Path.GetDirectoryName(FilePath)?.Split(Path.DirectorySeparatorChar).FirstOrDefault() ?? "";

    public string RelativePath => FilePath.Contains('/') && FilePath.IndexOf('/') < FilePath.Length - 1
        ? FilePath[(FilePath.IndexOf('/') + 1)..]
        : FilePath;

    public string Language => FileExtension switch
    {
        "cs" => "csharp",
        "js" => "javascript",
        "ts" => "typescript",
        "py" => "python",
        "rb" => "ruby",
        "go" => "go",
        "rs" => "rust",
        "java" => "java",
        "cpp" or "cc" or "cxx" => "cpp",
        "c" or "h" => "c",
        "html" or "htm" => "html",
        "css" => "css",
        "json" => "json",
        "xml" => "xml",
        "yaml" or "yml" => "yaml",
        "md" => "markdown",
        "sql" => "sql",
        "sh" or "bash" => "bash",
        "ps1" => "powershell",
        "razor" => "cshtml-razor",
        _ => "plaintext"
    };
}
