using System.Text.Json.Serialization;

namespace Grigori.Cli;

[JsonSerializable(typeof(IndexFilesRequest))]
[JsonSerializable(typeof(IndexFilesResponse))]
[JsonSerializable(typeof(List<FileContent>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class JsonContext : JsonSerializerContext
{
}

public record FileContent(string RelativePath, string Content);

public record IndexFilesRequest(string ProjectName, List<FileContent> Files);

public record IndexFilesResponse(
    bool Success,
    int FilesIndexed,
    int ChunksCreated,
    long DurationMs,
    string? Error
);
