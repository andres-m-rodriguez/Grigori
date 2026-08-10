namespace Grigori.Integrations.GitHub;

public sealed class GitHubWebhookOptions
{
    public const string SectionName = "GitHub";

    /// <summary>
    /// The same string entered in the GitHub App's (or repository webhook's) "Secret" field.
    /// Supplied as <c>GitHub__WebhookSecret</c>; the AppHost feeds it from an Aspire parameter.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
