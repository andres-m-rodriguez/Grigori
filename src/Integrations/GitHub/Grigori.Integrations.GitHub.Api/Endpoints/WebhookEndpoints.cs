using Grigori.Integrations.GitHub.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Grigori.Integrations.GitHub.Api.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapGitHubWebhookEndpoints(this IEndpointRouteBuilder builder)
    {
        var hooks = builder.MapGroup("/hooks");

        hooks.MapPost("/github", async Task<Results<Ok, UnauthorizedHttpResult, BadRequest<string>>> (
            HttpRequest request,
            IGitHubWebhookHandler handler,
            CancellationToken cancellationToken) =>
        {
            using var body = new MemoryStream();
            await request.Body.CopyToAsync(body, cancellationToken);

            var delivery = new WebhookDeliveryDto(
                request.Headers["X-GitHub-Event"].ToString(),
                request.Headers["X-GitHub-Delivery"].ToString(),
                request.Headers["X-Hub-Signature-256"].ToString(),
                body.ToArray());

            var result = await handler.Handle(delivery, cancellationToken);

            // Ignored events answer 200 on purpose: GitHub disables a webhook whose endpoint
            // keeps returning errors, and "I don't handle push yet" is not an error.
            return result.Match<Results<Ok, UnauthorizedHttpResult, BadRequest<string>>>(
                success => TypedResults.Ok(),
                rejected => TypedResults.Unauthorized(),
                ignored => TypedResults.Ok(),
                malformed => TypedResults.BadRequest(malformed.Reason));
        });

        return builder;
    }
}
