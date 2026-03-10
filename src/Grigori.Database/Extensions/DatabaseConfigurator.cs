using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.Database.Extensions;

public static class DatabaseConfigurator
{
    public static IServiceCollection AddGrigoriDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<GrigoriDbContext>(options =>
            options.UseSqlite(connectionString));

        return services;
    }
}
