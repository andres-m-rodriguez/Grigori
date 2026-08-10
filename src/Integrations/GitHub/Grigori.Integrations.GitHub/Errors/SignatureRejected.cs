namespace Grigori.Integrations.GitHub.Errors;

/// <summary>The delivery's HMAC did not match, or carried no signature at all.</summary>
public readonly record struct SignatureRejected;
