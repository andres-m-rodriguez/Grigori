using System.Globalization;
using System.Text.Json;
using Grigori.Integrations.GitHub.Dtos;
using Grigori.Integrations.GitHub.Errors;
using Grigori.Reviews.Application;
using Grigori.Reviews.Contracts;
using Grigori.Reviews.Contracts.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace Grigori.Integrations.GitHub;

internal sealed class GitHubWebhookHandler(
    IOptions<GitHubWebhookOptions> options,
    IReviewIngestion ingestion,
    ILogger<GitHubWebhookHandler> logger) : IGitHubWebhookHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<OneOf<Success, SignatureRejected, EventIgnored, MalformedPayload>> Handle(
        WebhookDeliveryDto delivery,
        CancellationToken cancellationToken)
    {
        if (!GitHubWebhookSignature.IsValid(delivery.Body.Span, delivery.Signature, options.Value.WebhookSecret))
        {
            logger.LogWarning("Rejected delivery {DeliveryId}: signature did not verify", delivery.DeliveryId);
            return new SignatureRejected();
        }

        // GitHub sends `ping` once when the webhook is first saved; answering it is what turns
        // the green check on in the App's settings page.
        if (delivery.Event is "ping")
        {
            logger.LogInformation("GitHub webhook ping accepted, delivery {DeliveryId}", delivery.DeliveryId);
            return new Success();
        }

        // Logged rather than dropped in silence: during bring-up "my event never arrived" and
        // "my event arrived and Grigori skipped it" look identical from the GitHub side.
        if (delivery.Event is not "pull_request")
        {
            logger.LogDebug("Ignored delivery {DeliveryId}: no handler for {Event}", delivery.DeliveryId, delivery.Event);
            return new EventIgnored(delivery.Event);
        }

        GitHubPullRequestEvent? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GitHubPullRequestEvent>(delivery.Body.Span, SerializerOptions);
        }
        catch (JsonException exception)
        {
            // The signature already proved this came from GitHub, so a bind failure means the
            // payload shape moved. Surface it instead of 500-ing, or GitHub retries it forever.
            logger.LogError(exception, "Delivery {DeliveryId} did not bind to pull_request", delivery.DeliveryId);
            return new MalformedPayload(exception.Message);
        }

        if (payload is not GitHubPullRequestEvent pullRequestEvent)
            return new MalformedPayload("pull_request body deserialized to null");

        // Everything except "opened" belongs to a later phase: reopened, synchronize, edited,
        // and closed are events against an existing Review, and there is no Review store yet.
        // `edited` is the one that will matter most — it carries description changes.
        if (pullRequestEvent.Action is not "opened")
        {
            logger.LogDebug(
                "Ignored delivery {DeliveryId}: pull_request.{Action} has no Review to attach to yet",
                delivery.DeliveryId,
                pullRequestEvent.Action);

            return new EventIgnored($"pull_request.{pullRequestEvent.Action}");
        }

        var pullRequest = pullRequestEvent.PullRequest;

        await ingestion.Ingest(
            new ReviewOpenedDto(
                new Origin(
                    GitHubIntegration.IntegrationName,
                    pullRequestEvent.Repository.FullName,
                    pullRequest.Number.ToString(CultureInfo.InvariantCulture)),
                pullRequest.Title,
                pullRequest.Body,
                pullRequest.User.Login,
                pullRequest.Head.Ref,
                pullRequest.Base.Ref,
                pullRequest.Head.Sha,
                pullRequest.Draft,
                pullRequest.HtmlUrl,
                pullRequest.CreatedAt),
            cancellationToken);

        return new Success();
    }
}
