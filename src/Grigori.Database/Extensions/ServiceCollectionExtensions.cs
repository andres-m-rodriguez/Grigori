using Microsoft.Extensions.DependencyInjection;

namespace Grigori.Database.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriDatabase(this IServiceCollection services)
    {
        services.AddSingleton<GrigoriDbContext>();
        return services;
    }
}
