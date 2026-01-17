using Grigori.Api.Features.Index;
using Grigori.Api.Features.Search;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Database.Extensions;
using Grigori.DataAccess.Extensions;
using Grigori.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add configuration
builder.Configuration.AddJsonFile("appsettings.json", optional: true);

// Configure options
builder.Services.Configure<GrigoriOptions>(builder.Configuration.GetSection(GrigoriOptions.SectionName));

// Add layers following dependency graph
builder.Services.AddGrigoriDatabase();         // Database layer
builder.Services.AddGrigoriDataAccess();       // DataAccess layer
builder.Services.AddGrigoriInfrastructure();   // Infrastructure layer

// Add feature services
builder.Services.AddSingleton<ISearchService, SearchService>();
builder.Services.AddSingleton<IIndexService, IndexService>();

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure OpenAPI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map feature endpoints
app.MapSearchEndpoints();
app.MapIndexEndpoints();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithTags("Health")
    .WithOpenApi();

// Metrics endpoint
app.MapGet("/api/metrics", (IMetricsService metricsService, bool reset = false) =>
{
    var snapshot = metricsService.GetSnapshot();
    if (reset)
    {
        metricsService.Reset();
    }
    return Results.Ok(snapshot);
})
.WithTags("Metrics")
.WithOpenApi()
.WithName("GetMetrics")
.WithDescription("Get accumulated metrics");

app.Run();
