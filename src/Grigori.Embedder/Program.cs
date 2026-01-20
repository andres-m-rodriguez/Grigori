using Grigori.Embedder.Services;

var builder = WebApplication.CreateBuilder(args);

// Add gRPC services
builder.Services.AddGrpc();

// Add the embedding generator as a singleton
builder.Services.AddSingleton<OnnxEmbeddingGenerator>();

var app = builder.Build();

// Eagerly initialize the embedding generator
_ = app.Services.GetRequiredService<OnnxEmbeddingGenerator>();

// Map gRPC service
app.MapGrpcService<EmbeddingGrpcService>();

// Health check endpoint for Docker
app.MapGet("/health", (OnnxEmbeddingGenerator generator) =>
{
    return Results.Ok(new
    {
        status = generator.IsReady ? "healthy" : "initializing",
        ready = generator.IsReady
    });
});

Console.WriteLine("Grigori Embedding Service starting...");
Console.WriteLine("  gRPC: http://0.0.0.0:50051 (HTTP/2)");
Console.WriteLine("  Health: http://0.0.0.0:8080/health (HTTP/1.1)");

app.Run();
