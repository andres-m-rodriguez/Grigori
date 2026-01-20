using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Infrastructure.Chunking;
using Grigori.Infrastructure.Dependencies;
using Grigori.Infrastructure.Embeddings;
using Grigori.Infrastructure.FileWatching;
using Grigori.Infrastructure.GitHub;
using Grigori.Infrastructure.Metrics;
using Microsoft.Extensions.DependencyInjection;

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

        // GitHub integration (singleton for token state management)
        services.AddHttpClient("GitHub");
        services.AddSingleton<IGitHubService, GitHubService>();

        // Note: HNSW index removed - using pgvector for vector similarity search

        // Embedding provider (based on configuration)
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GrigoriOptions>>();
            var provider = options.Value.EmbeddingProvider.ToLowerInvariant();

            return provider switch
            {
                "grpc" => ActivatorUtilities.CreateInstance<GrpcEmbeddingProvider>(sp),
                "voyage" => ActivatorUtilities.CreateInstance<VoyageEmbeddingProvider>(sp),
                "onnx" => ActivatorUtilities.CreateInstance<OnnxEmbeddingProvider>(sp),
                _ => ActivatorUtilities.CreateInstance<GrpcEmbeddingProvider>(sp)
            };
        });

        return services;
    }
}
