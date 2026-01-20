using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Options;
using Grigori.Infrastructure.Chunking;
using Grigori.Infrastructure.Dependencies;
using Grigori.Infrastructure.Embeddings;
using Grigori.Infrastructure.FileWatching;
using Grigori.Infrastructure.GitHub;
using Grigori.Infrastructure.Metrics;
using Grigori.Infrastructure.Services.Benchmark;
using Grigori.Infrastructure.Services.Index;
using Grigori.Infrastructure.Services.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriInfrastructure(this IServiceCollection services)
    {
        // Dependency tracking (singleton, must be registered first)
        services.AddSingleton<IDependencyTracker, DependencyTracker>();

        // Metrics (scoped to match repository lifetime)
        services.AddScoped<IMetricsService, MetricsService>();

        // Chunking
        services.AddSingleton<ILanguageChunker, CSharpChunker>();
        services.AddSingleton<ChunkingService>();

        // File watching
        services.AddSingleton<FileWatcher>();

        // GitHub integration (GitHubService singleton for token state, IndexingService scoped for repository access)
        services.AddHttpClient("GitHub");
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddScoped<IGitHubIndexingService, GitHubIndexingService>();

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

        // Feature services (scoped to match repository lifetime)
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IIndexService, IndexService>();
        services.AddScoped<BenchmarkService>();

        return services;
    }
}
