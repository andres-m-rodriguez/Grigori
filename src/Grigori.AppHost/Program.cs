var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector extension
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg16")
    .WithDataVolume("grigori-postgres-data")
    .WithPgAdmin();

var grigoriDb = postgres.AddDatabase("grigori");

// Embedder service (gRPC server for generating embeddings)
var embedder = builder.AddProject<Projects.Grigori_Embedder>("embedder");

// Grigori API (includes Dashboard)
var api = builder.AddProject<Projects.Grigori_Api>("api")
    .WithReference(grigoriDb)
    .WaitFor(grigoriDb)
    .WithReference(embedder)
    .WaitFor(embedder)
    .WithExternalHttpEndpoints();

// Grigori MCP Server
builder.AddProject<Projects.Grigori_Mcp>("mcp")
    .WithReference(grigoriDb)
    .WaitFor(grigoriDb)
    .WithReference(embedder)
    .WaitFor(embedder);

builder.Build().Run();
