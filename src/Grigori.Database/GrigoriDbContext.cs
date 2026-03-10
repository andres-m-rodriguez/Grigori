using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Grigori.Database;

public sealed class GrigoriDbContext(DbContextOptions<GrigoriDbContext> options)
    : DbContext(options)
{
    public DbSet<CodingPattern> CodingPatterns => Set<CodingPattern>();
    public DbSet<DesignPreference> DesignPreferences => Set<DesignPreference>();
    public DbSet<AvoidanceRule> AvoidanceRules => Set<AvoidanceRule>();

    // Architecture context
    public DbSet<ArchitecturePattern> ArchitecturePatterns => Set<ArchitecturePattern>();
    public DbSet<ArchitectureLayer> ArchitectureLayers => Set<ArchitectureLayer>();
    public DbSet<LayerDependency> LayerDependencies => Set<LayerDependency>();
    public DbSet<CodeTemplate> CodeTemplates => Set<CodeTemplate>();
    public DbSet<NamingConvention> NamingConventions => Set<NamingConvention>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new CodingPattern.EntityConfiguration());
        modelBuilder.ApplyConfiguration(new DesignPreference.EntityConfiguration());
        modelBuilder.ApplyConfiguration(new AvoidanceRule.EntityConfiguration());

        // Architecture context
        modelBuilder.ApplyConfiguration(new ArchitecturePattern.EntityConfiguration());
        modelBuilder.ApplyConfiguration(new ArchitectureLayer.EntityConfiguration());
        modelBuilder.ApplyConfiguration(new LayerDependency.EntityConfiguration());
        modelBuilder.ApplyConfiguration(new CodeTemplate.EntityConfiguration());
        modelBuilder.ApplyConfiguration(new NamingConvention.EntityConfiguration());
    }
}
