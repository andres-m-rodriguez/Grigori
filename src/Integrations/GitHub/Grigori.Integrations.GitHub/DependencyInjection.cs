using Grigori.Reviews.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.Integrations.GitHub;

public static class DependencyInjection
{
    public static IServiceCollection AddGitHubIntegration(this IServiceCollection services)
    {
        // Validated at startup rather than on first delivery: a missing secret means every
        // webhook silently 401s, which looks exactly like a misconfigured App on GitHub's side.
        services.AddOptions<GitHubWebhookOptions>()
            .BindConfiguration(GitHubWebhookOptions.SectionName)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.WebhookSecret),
                "GitHub:WebhookSecret is not set. Run through the AppHost, or set GitHub__WebhookSecret to the same value as the GitHub App's webhook secret.")
            .ValidateOnStart();

        // Registered against the port, not as a concrete type: Reviews resolves integrations by
        // IReviewIntegration.Name, so a second one is another AddXIntegration() call and nothing else.
        services.AddSingleton<IReviewIntegration, GitHubIntegration>();

        services.AddScoped<IGitHubWebhookHandler, GitHubWebhookHandler>();

        return services;
    }
}
