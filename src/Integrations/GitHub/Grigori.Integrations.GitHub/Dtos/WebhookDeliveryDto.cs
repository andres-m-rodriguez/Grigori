namespace Grigori.Integrations.GitHub.Dtos;

/// <summary>
/// One inbound webhook delivery, still in GitHub's own shape. The body stays as raw bytes
/// because the signature is computed over the exact octets that arrived — round-tripping
/// through a string and back changes them.
/// </summary>
public sealed record WebhookDeliveryDto(
    string Event,
    string DeliveryId,
    string? Signature,
    ReadOnlyMemory<byte> Body);
