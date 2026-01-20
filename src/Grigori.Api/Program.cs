using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Dashboard.Components;
using Grigori.Dashboard.Services;
using Grigori.Database;
using Grigori.DataAccess.Extensions;
using Grigori.Infrastructure.Extensions;
using Grigori.Infrastructure.Services.Benchmark;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Grigori.Api;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add Aspire PostgreSQL EF Core integration (gets connection string from Aspire)
builder.AddNpgsqlDbContext<GrigoriDbContext>("grigori", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsqlOptions => npgsqlOptions.UseVector());
});

// Configure options
builder.Services.Configure<GrigoriOptions>(builder.Configuration.GetSection(GrigoriOptions.SectionName));

// Add layers following dependency graph (skip database - already added via Aspire)
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
    await dbContext.Database.MigrateAsync();
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
// Projects API - GitHub Project Indexing
// ============================================================================

// Get all projects
app.MapGet("/api/projects", async (IProjectRepository projectRepository, CancellationToken ct) =>
{
    var result = await projectRepository.GetAllAsync(ct);
    return result.Match(
        success => Results.Ok(success),
        error => Results.BadRequest(new { error = error.Message })
    );
}).WithTags("Projects");

// Get project by ID
app.MapGet("/api/projects/{id:long}", async (long id, IProjectRepository projectRepository, CancellationToken ct) =>
{
    var result = await projectRepository.GetByIdAsync(id, ct);
    return result.Match(
        success => Results.Ok(success),
        error => error.Code == Grigori.Contracts.Results.GrigoriErrorCode.NotFound
            ? Results.NotFound(new { error = error.Message })
            : Results.BadRequest(new { error = error.Message })
    );
}).WithTags("Projects");

// Register a project (from GitHub repository)
app.MapPost("/api/projects", async (RegisterProjectRequest request, IGitHubIndexingService indexingService, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(request.RepoFullName))
        return Results.BadRequest(new { error = "RepoFullName is required" });

    var result = await indexingService.RegisterProjectAsync(request.RepoFullName, ct);
    return result.Match(
        success => Results.Created($"/api/projects/{success.Id}", success),
        error => error.Code == Grigori.Contracts.Results.GrigoriErrorCode.AlreadyExists
            ? Results.Conflict(new { error = error.Message })
            : Results.BadRequest(new { error = error.Message })
    );
}).WithTags("Projects");

// Index a project
app.MapPost("/api/projects/{id:long}/index", async (long id, IGitHubIndexingService indexingService, CancellationToken ct) =>
{
    var result = await indexingService.IndexRepositoryAsync(id, ct);
    return result.Match(
        success => Results.Ok(success),
        error => error.Code == Grigori.Contracts.Results.GrigoriErrorCode.NotFound
            ? Results.NotFound(new { error = error.Message })
            : Results.BadRequest(new { error = error.Message })
    );
}).WithTags("Projects");

// Delete a project
app.MapDelete("/api/projects/{id:long}", async (long id, IProjectRepository projectRepository, CancellationToken ct) =>
{
    var result = await projectRepository.DeleteAsync(id, ct);
    return result.Match(
        success => Results.NoContent(),
        error => error.Code == Grigori.Contracts.Results.GrigoriErrorCode.NotFound
            ? Results.NotFound(new { error = error.Message })
            : Results.BadRequest(new { error = error.Message })
    );
}).WithTags("Projects");


app.UseRouting();
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Routes).Assembly);

await app.RunAsync();




record SearchRequest(string Query, int? Limit, string? FileTypes, string? OutputMode);
record IndexRequest(string Path);
record IndexFilesRequest(string? ProjectName, List<FileContent> Files);
record FileContent(string RelativePath, string Content);
record BenchmarkRequest(string Query, string Directory);
record RegisterProjectRequest(string RepoFullName);
