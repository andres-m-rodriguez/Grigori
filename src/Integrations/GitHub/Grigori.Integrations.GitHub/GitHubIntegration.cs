using Grigori.Reviews.Application;

namespace Grigori.Integrations.GitHub;

/// <summary>
/// Grigori's implementation of <see cref="IReviewIntegration"/> for GitHub. Registering this
/// is what makes an Origin of <c>github:…</c> mean something.
/// </summary>
internal sealed class GitHubIntegration : IReviewIntegration
{
    public const string IntegrationName = "github";

    public string Name => IntegrationName;
}
