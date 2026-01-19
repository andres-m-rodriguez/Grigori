using Grigori.Contracts.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Grigori.Database.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriDatabase(this IServiceCollection services)
    {
        services.AddDbContext<GrigoriDbContext>((serviceProvider, options) =>
        {
            var grigoriOptions = serviceProvider.GetRequiredService<IOptions<GrigoriOptions>>().Value;
            options.UseSqlite($"Data Source={grigoriOptions.IndexPath}");
        });

        return services;
    }
}
