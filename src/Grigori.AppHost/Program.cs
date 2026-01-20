var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector extension
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg16")
    .WithDataVolume("grigori-postgres-data")
    .WithPgAdmin();

var grigoriDb = postgres.AddDatabase("grigori");

// Python Embedder service (container from pre-built image)
// Build the embedder image separately: docker build -t grigori-embedder src/Grigori.Embedder
var embedder = builder.AddContainer("embedder", "grigori-embedder", "latest")
    .WithEndpoint(port: 50051, targetPort: 50051, name: "grpc", scheme: "http");

// Grigori API (includes Dashboard)
var api = builder.AddProject("api", "../Grigori.Api/Grigori.Api.csproj")
    .WithReference(grigoriDb)
    .WaitFor(grigoriDb)
    .WithEnvironment("Embedder__Host", embedder.GetEndpoint("grpc"))
    .WaitFor(embedder)
    .WithExternalHttpEndpoints();

// Grigori MCP Server
builder.AddProject("mcp", "../Grigori.Mcp/Grigori.Mcp.csproj")
    .WithReference(grigoriDb)
    .WaitFor(grigoriDb)
    .WithEnvironment("Embedder__Host", embedder.GetEndpoint("grpc"))
    .WaitFor(embedder);

builder.Build().Run();
