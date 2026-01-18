using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Infrastructure.Chunking;
using Grigori.Infrastructure.Dependencies;
using Grigori.Infrastructure.Embeddings;
using Grigori.Infrastructure.FileWatching;
using Grigori.Infrastructure.Indexing;
using Grigori.Infrastructure.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grigori.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriInfrastructure(this IServiceCollection services)
    {
        // Dependency tracking (singleton, must be registered first)
        services.AddSingleton<IDependencyTracker, DependencyTracker>();

        // Metrics (singleton for thread-safe metrics tracking)
        services.AddSingleton<IMetricsService, MetricsService>();

        // Chunking
        services.AddSingleton<ILanguageChunker, CSharpChunker>();
        services.AddSingleton<ChunkingService>();

        // File watching
        services.AddSingleton<FileWatcher>();

        // HNSW Index (singleton for in-memory vector index)
        services.AddSingleton<HnswIndex>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GrigoriOptions>>();
            var logger = sp.GetRequiredService<ILogger<HnswIndex>>();
            var hnsw = options.Value.Hnsw;

            return new HnswIndex(
                logger,
                hnsw.M,
                hnsw.EfConstruction,
                hnsw.EfSearch);
        });

        // Embedding provider (based on configuration)
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GrigoriOptions>>();
            var provider = options.Value.EmbeddingProvider.ToLowerInvariant();

            return provider switch
            {
                "voyage" => ActivatorUtilities.CreateInstance<VoyageEmbeddingProvider>(sp),
                "onnx" => ActivatorUtilities.CreateInstance<OnnxEmbeddingProvider>(sp),
                _ => ActivatorUtilities.CreateInstance<OnnxEmbeddingProvider>(sp)
            };
        });

        return services;
    }
}
