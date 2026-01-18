using Grigori.Contracts.Dtos.Index;
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

// Check run mode
var mcpMode = args.Contains("--mcp");           // stdio MCP mode (for local use)
var serverMode = args.Contains("--server");      // HTTP server mode (for Docker/shared)
var dashboardOnly = args.Contains("--dashboard"); // Dashboard only

var dashboardPort = builder.Configuration.GetValue("Grigori:Dashboard:Port", 5151);
var apiPort = builder.Configuration.GetValue("Grigori:Api:Port", 5152);

// Default to server mode if no flags provided
if (!mcpMode && !dashboardOnly)
    serverMode = true;

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

// Add CORS for API access
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

if (mcpMode)
{
    // Register MCP tool endpoints for stdio mode
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

// Configure Kestrel
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(dashboardPort);  // Dashboard + API
});

var app = builder.Build();

app.UseCors();

// ============================================================================
// REST API Endpoints (for Docker/shared mode)
// ============================================================================

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithTags("Health");

app.MapGet("/api/status", (IMetricsService metrics) =>
{
    var snapshot = metrics.GetSnapshot();
    return Results.Ok(snapshot);
}).WithTags("Status");

// Search API
app.MapPost("/api/search", async (SearchRequest request, ISearchService searchService, CancellationToken ct) =>
{
    var result = await searchService.SearchAsync(new Grigori.Contracts.Dtos.Search.SearchRequestDto
    {
        Query = request.Query,
        Limit = request.Limit ?? 10,
        FileTypes = request.FileTypes,
        OutputMode = request.OutputMode ?? "full"
    }, ct);

    return result.Match(
        success => Results.Ok(success),
        error => Results.BadRequest(new { error = error.Message })
    );
}).WithTags("Search");

// Index API - for indexing mounted directories
app.MapPost("/api/index", async (IndexRequest request, IIndexService indexService, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(request.Path))
        return Results.BadRequest(new { error = "Path is required" });

    var result = await indexService.IndexDirectoryAsync(new IndexRequestDto { Path = request.Path }, ct);

    return result.Match(
        success => Results.Ok(success),
        error => Results.BadRequest(new { error = error.Message })
    );
}).WithTags("Index");

// Index API - for indexing files sent over the network
app.MapPost("/api/index/files", async Task<IResult> (IndexFilesRequest request, IIndexService indexService, CancellationToken ct) =>
{
    if (request.Files is null || request.Files.Count == 0)
        return Results.BadRequest(new { error = "Files are required" });

    // Create temp directory for files
    var tempDir = Path.Combine(Path.GetTempPath(), "grigori-index", Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);

    try
    {
        // Write files to temp directory preserving structure
        foreach (var file in request.Files)
        {
            var filePath = Path.Combine(tempDir, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var fileDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(fileDir))
                Directory.CreateDirectory(fileDir);
            await File.WriteAllTextAsync(filePath, file.Content, ct);
        }

        // Index the temp directory
        var result = await indexService.IndexDirectoryAsync(new IndexRequestDto
        {
            Path = tempDir
        }, ct);

        return result.Match(
            success => Results.Ok(success),
            error => Results.BadRequest(new { error = error.Message })
        );
    }
    finally
    {
        // Cleanup temp directory
        try { Directory.Delete(tempDir, true); } catch { }
    }
}).WithTags("Index");

// ============================================================================
// Dashboard (Blazor)
// ============================================================================

// Redirect root to dashboard
app.MapGet("/", () => Results.Redirect("/dashboard"));

// Configure Blazor dashboard at /dashboard path
app.UsePathBase("/dashboard");
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ============================================================================
// Startup
// ============================================================================

if (mcpMode)
{
    // MCP stdio mode - runs MCP server + dashboard
    Console.Error.WriteLine($"Grigori MCP Server started. Dashboard at http://localhost:{dashboardPort}/dashboard");
    _ = Task.Run(() => app.RunAsync());
    await Task.Delay(Timeout.Infinite);
}
else
{
    // Server mode - HTTP API + Dashboard
    Console.WriteLine($"Grigori Server started");
    Console.WriteLine($"  Dashboard: http://localhost:{dashboardPort}/dashboard");
    Console.WriteLine($"  API:       http://localhost:{dashboardPort}/api");
    Console.WriteLine($"  Health:    http://localhost:{dashboardPort}/api/health");
    await app.RunAsync();
}

// ============================================================================
// Request DTOs
// ============================================================================

record SearchRequest(string Query, int? Limit, string? FileTypes, string? OutputMode);
record IndexRequest(string Path);
record IndexFilesRequest(List<FileContent> Files);
record FileContent(string RelativePath, string Content);
