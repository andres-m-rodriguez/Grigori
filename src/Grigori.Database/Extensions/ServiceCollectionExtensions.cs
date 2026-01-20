using Grigori.Contracts.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Grigori.Database.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriDatabase(this IServiceCollection services)
    {
        // Use DbContextFactory for thread-safe DbContext creation
        // This allows concurrent operations (e.g., Task.WhenAll) without conflicts
        services.AddDbContextFactory<GrigoriDbContext>((serviceProvider, options) =>
        {
            var grigoriOptions = serviceProvider.GetRequiredService<IOptions<GrigoriOptions>>().Value;
            var connectionString = grigoriOptions.Database?.ConnectionString
                ?? "Host=localhost;Database=grigori;Username=grigori;Password=grigori";

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
            });
        });

        // Also register DbContext for cases where scoped context is needed (e.g., migrations)
        services.AddDbContext<GrigoriDbContext>((serviceProvider, options) =>
        {
            var grigoriOptions = serviceProvider.GetRequiredService<IOptions<GrigoriOptions>>().Value;
            var connectionString = grigoriOptions.Database?.ConnectionString
                ?? "Host=localhost;Database=grigori;Username=grigori;Password=grigori";

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
            });
        });

        return services;
    }
}
