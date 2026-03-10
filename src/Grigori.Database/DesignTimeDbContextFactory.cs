using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Grigori.Database;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GrigoriDbContext>
{
    public GrigoriDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GrigoriDbContext>();
        optionsBuilder.UseSqlite("Data Source=grigori.db");

        return new GrigoriDbContext(optionsBuilder.Options);
    }
}
