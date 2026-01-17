using Grigori.Api.Endpoints;
using Grigori.Core.Embeddings;
using Grigori.Core.Indexing;
using Grigori.Core.Search;
using Grigori.Core.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add configuration
builder.Configuration.AddJsonFile("appsettings.json", optional: true);

// Configure options
builder.Services.Configure<GrigoriOptions>(builder.Configuration.GetSection("Grigori"));

// Register core services
builder.Services.AddSingleton<VectorStore>();
builder.Services.AddSingleton<IEmbeddingProvider, VoyageEmbeddings>();
builder.Services.AddSingleton<RoslynCodeChunker>();
builder.Services.AddSingleton<CodeChunker>();
builder.Services.AddSingleton<FileWatcher>();
builder.Services.AddSingleton<SemanticSearch>();

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure OpenAPI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map endpoints
app.MapIndexEndpoints();
app.MapSearchEndpoints();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
