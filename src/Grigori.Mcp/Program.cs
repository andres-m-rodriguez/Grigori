using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Database;
using Grigori.DataAccess.Extensions;
using Grigori.Infrastructure.Extensions;
using Grigori.Mcp.Features.Benchmark.Endpoints;
using Grigori.Mcp.Features.Index.Endpoints;
using Grigori.Mcp.Features.Metrics.Endpoints;
using Grigori.Mcp.Features.Search.Endpoints;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Check run mode
var mcpMode = args.Contains("--mcp");           // stdio MCP mode (for local use)
var mcpHttpMode = args.Contains("--mcp-http");  // HTTP MCP mode (for remote AI clients)

// Default to HTTP mode if no flags provided
if (!mcpMode)
    mcpHttpMode = true;

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
    await dbContext.Database.MigrateAsync();
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

// Map Aspire default endpoints (health, alive)
app.MapDefaultEndpoints();

// Legacy health endpoint for container orchestration
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
