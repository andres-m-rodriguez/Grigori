namespace Grigori.Integrations.GitHub.Errors;

/// <summary>
/// A signed, well-formed delivery Grigori has no handler for yet. Not a failure — the
/// endpoint still answers 2xx, because GitHub disables a webhook that keeps erroring.
/// </summary>
public sealed record EventIgnored(string Event);
