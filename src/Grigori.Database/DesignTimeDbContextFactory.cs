using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Grigori.Database;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GrigoriDbContext>
{
    public GrigoriDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GrigoriDbContext>();

        // Use a default connection string for migrations
        // This will be overridden at runtime
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=grigori;Username=grigori;Password=grigori",
            o => o.UseVector());

        return new GrigoriDbContext(optionsBuilder.Options);
    }
}
