var builder = DistributedApplication.CreateBuilder(args);

// GitHub signs every delivery with this secret and Grigori verifies the HMAC before reading
// a byte of the body.
//
// Deliberately NOT a generated parameter. The same value has to be typed into the webhook's
// "Secret" field on GitHub by hand, so anything that can rewrite it under us produces a 401
// that looks exactly like a misconfigured webhook. A GenerateParameterDefault here will
// silently replace a stored value that doesn't meet its constraints. Set it with:
//   dotnet user-secrets set Parameters:github-webhook-secret <value> --project src/Grigori.AppHost
var githubWebhookSecret = builder.AddParameter("github-webhook-secret", secret: true);

builder.AddProject<Projects.Grigori_Server>("api")
    .WithEnvironment("GitHub__WebhookSecret", githubWebhookSecret);

builder.Build().Run();
