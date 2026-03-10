using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grigori.Database.Models;

public sealed class ArchitectureLayer
{
    public int Id { get; set; }
    public int PatternId { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Responsibility { get; set; }
    public string? Contains { get; set; } // What goes in this layer
    public int Order { get; set; } // Stack position (0 = top)

    public ArchitecturePattern Pattern { get; set; } = null!;
    public ICollection<LayerDependency> OutgoingDependencies { get; set; } = [];
    public ICollection<LayerDependency> IncomingDependencies { get; set; } = [];
    public ICollection<CodeTemplate> Templates { get; set; } = [];
    public ICollection<NamingConvention> NamingConventions { get; set; } = [];

    public sealed class EntityConfiguration : IEntityTypeConfiguration<ArchitectureLayer>
    {
        public void Configure(EntityTypeBuilder<ArchitectureLayer> builder)
        {
            builder.ToTable("architecture_layers");
            builder.HasKey(static e => e.Id);

            builder.Property(static e => e.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(static e => e.Description)
                .HasMaxLength(2000)
                .IsRequired();

            builder.Property(static e => e.Responsibility)
                .HasMaxLength(2000)
                .IsRequired();

            builder.Property(static e => e.Contains)
                .HasMaxLength(2000);

            builder.HasOne(static e => e.Pattern)
                .WithMany(static p => p.Layers)
                .HasForeignKey(static e => e.PatternId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(static e => new { e.PatternId, e.Name })
                .IsUnique()
                .HasDatabaseName("architecture_layers_pattern_name_idx");

            builder.HasIndex(static e => new { e.PatternId, e.Order })
                .HasDatabaseName("architecture_layers_pattern_order_idx");
        }
    }
}
