using Grigori.Integrations.GitHub;
using Grigori.Integrations.GitHub.Api.Endpoints;
using Grigori.Reviews.Internal;

var builder = WebApplication.CreateBuilder(args);

// Reviews has no .Api project yet — nothing reads state back out until the projections and
// /await land. It becomes AddReviewsFeature() when it does.
builder.Services.AddReviewsInternal();
builder.Services.AddGitHubIntegration();

var app = builder.Build();

app.MapGitHubWebhookEndpoints();

app.MapGet("/", () =>
{
    return TypedResults.Ok("<h1>Hello world</h1>");
});

app.Run();
