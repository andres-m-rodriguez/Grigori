using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Database;
using Grigori.Database.Extensions;
using Microsoft.EntityFrameworkCore;
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
using MudBlazor.Services;

// Determine the application directory
// For single-file publish: use the executable's directory
// For framework-dependent: use AppContext.BaseDirectory (the DLL location)
var processPath = Environment.ProcessPath;
var isSingleFile = processPath != null &&
    !processPath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) &&
    !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase);

var appDir = isSingleFile
    ? Path.GetDirectoryName(processPath)!
    : AppContext.BaseDirectory;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = appDir,
    WebRootPath = Path.Combine(appDir, "wwwroot")
});

// Check run mode
var mcpMode = args.Contains("--mcp");             // stdio MCP mode (for local use)
var mcpHttpMode = args.Contains("--mcp-http");    // HTTP MCP mode (for remote AI clients)
var serverMode = args.Contains("--server");        // HTTP server mode (for Docker/shared)
var dashboardOnly = args.Contains("--dashboard");  // Dashboard only

// Default to server mode if no flags provided
if (!mcpMode && !mcpHttpMode && !dashboardOnly)
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

// Add MudBlazor
builder.Services.AddMudServices();

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

// Register MCP tool endpoints (available in all modes except dashboard-only)
if (!dashboardOnly)
{
    builder.Services.AddScoped<SearchEndpoints>();
    builder.Services.AddScoped<IndexEndpoints>();
    builder.Services.AddScoped<MetricsEndpoints>();
    builder.Services.AddScoped<BenchmarkEndpoints>();
}

if (mcpMode)
{
    // Register MCP server with stdio transport (local use)
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
}
else if (serverMode || mcpHttpMode)
{
    // Register MCP server with HTTP transport (server mode includes MCP)
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();
}

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<GrigoriDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

// Eagerly initialize embedding provider to start background model loading
// This triggers registration with the dependency tracker
_ = app.Services.GetRequiredService<IEmbeddingProvider>();

app.UseCors();

// ============================================================================
// MCP HTTP Endpoints (for remote AI clients)
// ============================================================================

if (serverMode || mcpHttpMode)
{
    // Map MCP endpoints for Streamable HTTP transport with /mcp prefix
    // This exposes /mcp, /mcp/sse and /mcp/message endpoints for MCP clients
    // Using a prefix avoids route conflict with Blazor's "/" route
    app.MapMcp("/mcp");
}

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

    // Create temp directory for files using project name if provided
    var projectFolder = !string.IsNullOrEmpty(request.ProjectName)
        ? request.ProjectName
        : Guid.NewGuid().ToString();
    var tempDir = Path.Combine(Path.GetTempPath(), "grigori-index", projectFolder);
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

        // Index the temp directory, passing project name so paths are stored correctly
        var result = await indexService.IndexDirectoryAsync(new IndexRequestDto
        {
            Path = tempDir,
            ProjectName = request.ProjectName
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

app.UseRouting();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ============================================================================
// Startup
// ============================================================================

// Start the app and get actual URLs
await app.StartAsync();
var urls = app.Urls.ToList();
var baseUrl = urls.FirstOrDefault() ?? "http://localhost:5000";

if (mcpMode)
{
    // MCP stdio mode - runs MCP server + dashboard
    Console.Error.WriteLine($"Grigori MCP Server started (stdio transport)");
    Console.Error.WriteLine($"  Dashboard: {baseUrl}");
    await Task.Delay(Timeout.Infinite);
}
else if (mcpHttpMode)
{
    // MCP HTTP mode - runs MCP server over HTTP + REST API + Dashboard
    Console.WriteLine($"Grigori MCP Server started (HTTP transport)");
    Console.WriteLine($"  MCP:       {baseUrl}/mcp");
    Console.WriteLine($"  MCP SSE:   {baseUrl}/mcp/sse");
    Console.WriteLine($"  MCP Msg:   {baseUrl}/mcp/message");
    Console.WriteLine($"  Dashboard: {baseUrl}");
    Console.WriteLine($"  API:       {baseUrl}/api");
    Console.WriteLine($"  Health:    {baseUrl}/api/health");
    await app.WaitForShutdownAsync();
}
else
{
    // Server mode - HTTP API + Dashboard + MCP
    Console.WriteLine($"Grigori Server started");
    Console.WriteLine($"  Dashboard: {baseUrl}");
    Console.WriteLine($"  API:       {baseUrl}/api");
    Console.WriteLine($"  Health:    {baseUrl}/api/health");
    Console.WriteLine($"  MCP:       {baseUrl}/mcp");
    Console.WriteLine($"  MCP SSE:   {baseUrl}/mcp/sse");
    Console.WriteLine($"  MCP Msg:   {baseUrl}/mcp/message");
    await app.WaitForShutdownAsync();
}

// ============================================================================
// Request DTOs
// ============================================================================

record SearchRequest(string Query, int? Limit, string? FileTypes, string? OutputMode);
record IndexRequest(string Path);
record IndexFilesRequest(string? ProjectName, List<FileContent> Files);
record FileContent(string RelativePath, string Content);
