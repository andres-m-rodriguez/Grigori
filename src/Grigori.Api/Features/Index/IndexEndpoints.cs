using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Grigori.Api.Features.Index;

public static class IndexEndpoints
{
    public static void MapIndexEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/index")
            .WithTags("Index")
            .WithOpenApi();

        group.MapPost("/directory", async (
            [FromBody] IndexRequestDto request,
            [FromServices] IIndexService indexService,
            CancellationToken cancellationToken) =>
        {
            var result = await indexService.IndexDirectoryAsync(request, cancellationToken);

            return result.Match(
                success => Results.Ok(success),
                error => Results.Problem(
                    detail: error.Message,
                    statusCode: error.Code switch
                    {
                        Contracts.Results.GrigoriErrorCode.DirectoryNotFound => StatusCodes.Status404NotFound,
                        Contracts.Results.GrigoriErrorCode.IndexingFailed => StatusCodes.Status500InternalServerError,
                        _ => StatusCodes.Status500InternalServerError
                    }
                )
            );
        })
        .WithName("IndexDirectory")
        .WithDescription("Index a directory for semantic code search");

        group.MapPost("/file", async (
            [FromQuery] string path,
            [FromServices] IIndexService indexService,
            CancellationToken cancellationToken) =>
        {
            var result = await indexService.IndexFileAsync(path, cancellationToken);

            return result.Match(
                success => Results.Ok(success),
                error => Results.Problem(
                    detail: error.Message,
                    statusCode: error.Code switch
                    {
                        Contracts.Results.GrigoriErrorCode.FileNotFound => StatusCodes.Status404NotFound,
                        Contracts.Results.GrigoriErrorCode.IndexingFailed => StatusCodes.Status500InternalServerError,
                        _ => StatusCodes.Status500InternalServerError
                    }
                )
            );
        })
        .WithName("IndexFile")
        .WithDescription("Index a single file for semantic code search");

        group.MapDelete("/file", async (
            [FromQuery] string path,
            [FromServices] IIndexService indexService,
            CancellationToken cancellationToken) =>
        {
            var result = await indexService.RemoveFileAsync(path, cancellationToken);

            return result.Match(
                success => Results.Ok(new { removed = success, path }),
                error => Results.Problem(detail: error.Message, statusCode: StatusCodes.Status500InternalServerError)
            );
        })
        .WithName("RemoveFile")
        .WithDescription("Remove a file from the index");
    }
}
