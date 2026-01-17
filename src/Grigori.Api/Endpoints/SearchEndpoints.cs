using Grigori.Core.Search;

namespace Grigori.Api.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapPost("/search", async (SearchRequest request, SemanticSearch search, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrEmpty(request.Query))
            {
                return Results.BadRequest(new { error = "Query is required" });
            }

            var limit = request.Limit ?? 5;

            // Parse file extensions if provided
            List<string>? fileExtensions = null;
            if (!string.IsNullOrEmpty(request.FileTypes))
            {
                fileExtensions = request.FileTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ext => ext.StartsWith('.') ? ext : $".{ext}")
                    .ToList();
            }

            var results = await search.SearchAsync(request.Query, limit, fileExtensions, cancellationToken);

            return Results.Ok(new SearchResponse
            {
                Success = true,
                Results = results.Select(r => new CodeMatchDto
                {
                    FilePath = r.Chunk.FilePath,
                    StartLine = r.Chunk.StartLine,
                    EndLine = r.Chunk.EndLine,
                    Content = r.Chunk.Content,
                    Score = r.Score
                }).ToList()
            });
        })
        .WithName("SearchCode")
        .WithOpenApi();
    }
}

public record SearchRequest
{
    public string? Query { get; init; }
    public int? Limit { get; init; }
    public string? FileTypes { get; init; }
}

public record SearchResponse
{
    public bool Success { get; init; }
    public List<CodeMatchDto> Results { get; init; } = [];
}

public record CodeMatchDto
{
    public string FilePath { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string Content { get; init; } = string.Empty;
    public float Score { get; init; }
}
