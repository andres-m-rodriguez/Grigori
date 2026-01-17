using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Database;
using Grigori.Database.Extensions;
using Grigori.DataAccess.Extensions;
using Grigori.Infrastructure.Extensions;
using Grigori.Infrastructure.Indexing;
using Grigori.Mcp.Dashboard.Components;
using Grigori.Mcp.Dashboard.Services;
using Grigori.Mcp.Features.Benchmark.Endpoints;
using Grigori.Mcp.Features.Benchmark.Services;
using Grigori.Mcp.Features.Index.Endpoints;
using Grigori.Mcp.Features.Index.Services;
using Grigori.Mcp.Features.Metrics.Endpoints;
using Grigori.Mcp.Features.Search.Endpoints;
using Grigori.Mcp.Features.Search.Services;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Check if running in MCP mode (default) or dashboard-only mode
var dashboardOnly = args.Contains("--dashboard");
var dashboardPort = builder.Configuration.GetValue("Grigori:Dashboard:Port", 5151);

// Add configuration
var projectDir = AppContext.BaseDirectory;
builder.Configuration.AddJsonFile(Path.Combine(projectDir, "appsettings.json"), optional: true);

// Configure options
builder.Services.Configure<GrigoriOptions>(builder.Configuration.GetSection(GrigoriOptions.SectionName));

// Add layers following dependency graph
builder.Services.AddGrigoriDatabase();         // Database layer
builder.Services.AddGrigoriDataAccess();       // DataAccess layer
builder.Services.AddGrigoriInfrastructure();   // Infrastructure layer

// Add feature services
builder.Services.AddSingleton<ISearchService, SearchService>();
builder.Services.AddSingleton<IIndexService, IndexService>();
builder.Services.AddSingleton<BenchmarkService>();

// Dashboard services
builder.Services.AddScoped<DashboardService>();

// Add Blazor services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

if (!dashboardOnly)
{
    // Register MCP tool endpoints
    builder.Services.AddScoped<SearchEndpoints>();
    builder.Services.AddScoped<IndexEndpoints>();
    builder.Services.AddScoped<MetricsEndpoints>();
    builder.Services.AddScoped<BenchmarkEndpoints>();

    // Register MCP server with stdio transport
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
}

// Configure Kestrel for dashboard
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(dashboardPort);
});

var app = builder.Build();

// Redirect root to dashboard (before path base)
app.MapGet("/", () => Results.Redirect("/dashboard"));

// Configure Blazor dashboard at /dashboard path
app.UsePathBase("/dashboard");
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (dashboardOnly)
{
    Console.WriteLine($"Grigori Dashboard running at http://localhost:{dashboardPort}/dashboard");
    await app.RunAsync();
}
else
{
    // Run both MCP and dashboard
    Console.Error.WriteLine($"Grigori MCP Server started. Dashboard at http://localhost:{dashboardPort}/dashboard");

    // Start the web server in background
    _ = Task.Run(() => app.RunAsync());

    // The MCP server runs via the hosted service registered by AddMcpServer
    await Task.Delay(Timeout.Infinite);
}
