using Grigori.Core.Embeddings;
using Grigori.Core.Indexing;
using Grigori.Core.Metrics;
using Grigori.Core.Search;
using Grigori.Core.Storage;
using Grigori.Mcp;
using Grigori.Mcp.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Add configuration - use full path since working directory may vary
var projectDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? AppContext.BaseDirectory;
builder.Configuration.AddJsonFile(Path.Combine(projectDir, "appsettings.json"), optional: true);

// Configure options
builder.Services.Configure<GrigoriOptions>(builder.Configuration.GetSection("Grigori"));

// Register core services
builder.Services.AddSingleton<VectorStore>();
builder.Services.AddSingleton(SearchMetrics.Instance);

// Register embedding provider based on configuration
var embeddingProvider = builder.Configuration.GetSection("Grigori")["EmbeddingProvider"] ?? "onnx";
if (embeddingProvider.Equals("voyage", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IEmbeddingProvider, VoyageEmbeddings>();
else
    builder.Services.AddSingleton<IEmbeddingProvider, OnnxEmbeddings>();
builder.Services.AddSingleton<RoslynCodeChunker>();
builder.Services.AddSingleton<CodeChunker>();
builder.Services.AddSingleton<FileWatcher>();
builder.Services.AddSingleton<SemanticSearch>();

// Register tool classes for DI
builder.Services.AddScoped<IndexTool>();
builder.Services.AddScoped<SearchTool>();
builder.Services.AddScoped<BenchmarkTool>();
builder.Services.AddScoped<MetricsTool>();

// Register MCP server with tools
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();
await host.RunAsync();
