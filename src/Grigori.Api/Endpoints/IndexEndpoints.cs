using Grigori.Core.Search;

namespace Grigori.Api.Endpoints;

public static class IndexEndpoints
{
    public static void MapIndexEndpoints(this WebApplication app)
    {
        app.MapPost("/index", async (IndexRequest request, SemanticSearch search, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrEmpty(request.Path))
            {
                return Results.BadRequest(new { error = "Path is required" });
            }

            if (!Directory.Exists(request.Path))
            {
                return Results.NotFound(new { error = $"Directory not found: {request.Path}" });
            }

            await search.IndexDirectoryAsync(request.Path, cancellationToken);

            var fileCount = Directory.EnumerateFiles(request.Path, "*", SearchOption.AllDirectories).Count();
            return Results.Ok(new IndexResponse
            {
                Success = true,
                Message = $"Successfully indexed directory: {request.Path}",
                FilesIndexed = fileCount
            });
        })
        .WithName("IndexDirectory")
        .WithOpenApi();
    }
}

public record IndexRequest
{
    public string? Path { get; init; }
}

public record IndexResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int FilesIndexed { get; init; }
}
