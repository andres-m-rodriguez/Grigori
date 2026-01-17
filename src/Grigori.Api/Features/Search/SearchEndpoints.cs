using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Grigori.Api.Features.Search;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search")
            .WithTags("Search")
            .WithOpenApi();

        group.MapPost("/", async (
            [FromBody] SearchRequestDto request,
            [FromServices] ISearchService searchService,
            CancellationToken cancellationToken) =>
        {
            var result = await searchService.SearchAsync(request, cancellationToken);

            return result.Match(
                success => Results.Ok(success),
                error => Results.Problem(
                    detail: error.Message,
                    statusCode: error.Code switch
                    {
                        Contracts.Results.GrigoriErrorCode.InvalidQuery => StatusCodes.Status400BadRequest,
                        Contracts.Results.GrigoriErrorCode.SearchFailed => StatusCodes.Status500InternalServerError,
                        _ => StatusCodes.Status500InternalServerError
                    }
                )
            );
        })
        .WithName("Search")
        .WithDescription("Semantic search across indexed codebase");

        group.MapGet("/cached/{query}", (
            string query,
            [FromServices] ISearchService searchService) =>
        {
            var isCached = searchService.IsQueryCached(query);
            return Results.Ok(new { query, cached = isCached });
        })
        .WithName("CheckCache")
        .WithDescription("Check if a query is cached");

        group.MapDelete("/cache", (
            [FromServices] ISearchService searchService) =>
        {
            searchService.ClearCache();
            return Results.Ok(new { message = "Cache cleared" });
        })
        .WithName("ClearCache")
        .WithDescription("Clear the query cache");
    }
}
