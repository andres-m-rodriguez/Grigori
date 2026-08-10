using Grigori.Integrations.GitHub.Dtos;
using Grigori.Integrations.GitHub.Errors;
using OneOf;
using OneOf.Types;

namespace Grigori.Integrations.GitHub;

public interface IGitHubWebhookHandler
{
    Task<OneOf<Success, SignatureRejected, EventIgnored, MalformedPayload>> Handle(
        WebhookDeliveryDto delivery,
        CancellationToken cancellationToken);
}
