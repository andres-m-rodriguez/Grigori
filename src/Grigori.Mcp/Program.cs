using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Database;
using Grigori.Database.Extensions;
using Grigori.DataAccess.Extensions;
using Grigori.Infrastructure.Extensions;
using Grigori.Mcp.Features.Benchmark.Endpoints;
using Grigori.Mcp.Features.Index.Endpoints;
using Grigori.Mcp.Features.Metrics.Endpoints;
using Grigori.Mcp.Features.Search.Endpoints;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

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
    ContentRootPath = appDir
});

// Check run mode
var mcpMode = args.Contains("--mcp");           // stdio MCP mode (for local use)
var mcpHttpMode = args.Contains("--mcp-http");  // HTTP MCP mode (for remote AI clients)

// Default to HTTP mode if no flags provided
if (!mcpMode)
    mcpHttpMode = true;

// Add configuration
var projectDir = AppContext.BaseDirectory;
builder.Configuration.AddJsonFile(Path.Combine(projectDir, "appsettings.json"), optional: true);
builder.Configuration.AddEnvironmentVariables();

// Configure options
builder.Services.Configure<GrigoriOptions>(builder.Configuration.GetSection(GrigoriOptions.SectionName));

// Add layers following dependency graph
builder.Services.AddGrigoriDatabase();         // Database layer
builder.Services.AddGrigoriDataAccess();       // DataAccess layer
builder.Services.AddGrigoriInfrastructure();   // Infrastructure layer (includes services)

// Register MCP tool endpoints
builder.Services.AddScoped<SearchEndpoints>();
builder.Services.AddScoped<IndexEndpoints>();
builder.Services.AddScoped<MetricsEndpoints>();
builder.Services.AddScoped<BenchmarkEndpoints>();

if (mcpMode)
{
    // Register MCP server with stdio transport (local use)
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
}
else
{
    // Register MCP server with HTTP transport
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();
}

var app = builder.Build();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<GrigoriDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

// Eagerly initialize embedding provider to start background connection
_ = app.Services.GetRequiredService<IEmbeddingProvider>();

// ============================================================================
// MCP HTTP Endpoints
// ============================================================================

if (mcpHttpMode)
{
    // Map MCP endpoints for Streamable HTTP transport
    app.MapMcp("/mcp");
}

// Health endpoint for container orchestration
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// ============================================================================
// Startup
// ============================================================================

await app.StartAsync();
var urls = app.Urls.ToList();
var baseUrl = urls.FirstOrDefault() ?? "http://localhost:5000";

if (mcpMode)
{
    // MCP stdio mode
    Console.Error.WriteLine($"Grigori MCP Server started (stdio transport)");
    await Task.Delay(Timeout.Infinite);
}
else
{
    // MCP HTTP mode
    Console.WriteLine($"Grigori MCP Server started (HTTP transport)");
    Console.WriteLine($"  MCP:       {baseUrl}/mcp");
    Console.WriteLine($"  MCP SSE:   {baseUrl}/mcp/sse");
    Console.WriteLine($"  MCP Msg:   {baseUrl}/mcp/message");
    Console.WriteLine($"  Health:    {baseUrl}/health");
    await app.WaitForShutdownAsync();
}
