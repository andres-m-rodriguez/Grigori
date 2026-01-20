var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector extension
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg16")
    .WithDataVolume("grigori-postgres-data")
    .WithPgAdmin();

var grigoriDb = postgres.AddDatabase("grigori");

// Embedder service (gRPC server for generating embeddings)
var embedder = builder.AddProject("embedder", "../Grigori.Embedder/Grigori.Embedder.csproj")
    .WithEndpoint("grpc", e =>
    {
        e.Port = 50051;
        e.TargetPort = 50051;
        e.IsProxied = false;
    });

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
