using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Dashboard.Components;
using Grigori.Dashboard.Services;
using Grigori.Database;
using Grigori.Database.Extensions;
using Grigori.DataAccess.Extensions;
using Grigori.Infrastructure.Extensions;
using Grigori.Infrastructure.Services.Benchmark;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

// Determine the application directory
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

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add configuration
var projectDir = AppContext.BaseDirectory;
builder.Configuration.AddJsonFile(Path.Combine(projectDir, "appsettings.json"), optional: true);
builder.Configuration.AddEnvironmentVariables();

// Configure options
builder.Services.Configure<GrigoriOptions>(builder.Configuration.GetSection(GrigoriOptions.SectionName));

// Add layers following dependency graph
builder.Services.AddGrigoriDatabase();         // Database layer
builder.Services.AddGrigoriDataAccess();       // DataAccess layer
builder.Services.AddGrigoriInfrastructure();   // Infrastructure layer (includes SearchService, IndexService)

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

var app = builder.Build();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<GrigoriDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

// Eagerly initialize embedding provider to start background connection
_ = app.Services.GetRequiredService<IEmbeddingProvider>();

app.UseCors();

// ============================================================================
// REST API Endpoints
// ============================================================================

// Map Aspire default endpoints (health, alive)
app.MapDefaultEndpoints();

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
    var result = await searchService.SearchAsync(new Grigori.Contracts.Dtos.Search.SearchRequestDto(
        request.Query,
        request.Limit ?? 10,
        request.OutputMode ?? "full",
        request.FileTypes), ct);

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

    var result = await indexService.IndexDirectoryAsync(new IndexRequestDto(request.Path), ct);

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
        var result = await indexService.IndexDirectoryAsync(new IndexRequestDto(tempDir, request.ProjectName), ct);

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

// Benchmark API
app.MapPost("/api/benchmark", async (BenchmarkRequest request, BenchmarkService benchmarkService, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(request.Query) || string.IsNullOrEmpty(request.Directory))
        return Results.BadRequest(new { error = "Query and Directory are required" });

    var result = await benchmarkService.RunBenchmarkAsync(request.Query, request.Directory, ct);
    return Results.Ok(result);
}).WithTags("Benchmark");

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

await app.StartAsync();
var urls = app.Urls.ToList();
var baseUrl = urls.FirstOrDefault() ?? "http://localhost:5000";

Console.WriteLine($"Grigori API Server started");
Console.WriteLine($"  Dashboard: {baseUrl}");
Console.WriteLine($"  API:       {baseUrl}/api");
Console.WriteLine($"  Health:    {baseUrl}/api/health");

await app.WaitForShutdownAsync();

// ============================================================================
// Request DTOs
// ============================================================================

record SearchRequest(string Query, int? Limit, string? FileTypes, string? OutputMode);
record IndexRequest(string Path);
record IndexFilesRequest(string? ProjectName, List<FileContent> Files);
record FileContent(string RelativePath, string Content);
record BenchmarkRequest(string Query, string Directory);
