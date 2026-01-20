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
