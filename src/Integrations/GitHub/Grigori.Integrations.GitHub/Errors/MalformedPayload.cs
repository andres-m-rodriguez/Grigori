namespace Grigori.Integrations.GitHub.Errors;

/// <summary>
/// The signature checked out but the body did not bind. In practice this means GitHub changed
/// a schema Grigori depends on, so the reason is worth surfacing rather than swallowing.
/// </summary>
public sealed record MalformedPayload(string Reason);
