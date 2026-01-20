using Grigori.Contracts.Interfaces;
using Grigori.DataAccess.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.DataAccess.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriDataAccess(this IServiceCollection services)
    {
        // Repositories must be Scoped to match DbContext lifetime
        services.AddScoped<IChunkRepository, ChunkRepository>();
        services.AddScoped<IMetricsRepository, MetricsRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        return services;
    }
}
