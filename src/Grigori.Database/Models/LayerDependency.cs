using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grigori.Database.Models;

public sealed class LayerDependency
{
    public int Id { get; set; }
    public int SourceLayerId { get; set; }
    public int TargetLayerId { get; set; }
    public bool IsAllowed { get; set; } // true = allowed, false = forbidden
    public string? Rationale { get; set; }

    public ArchitectureLayer SourceLayer { get; set; } = null!;
    public ArchitectureLayer TargetLayer { get; set; } = null!;

    public sealed class EntityConfiguration : IEntityTypeConfiguration<LayerDependency>
    {
        public void Configure(EntityTypeBuilder<LayerDependency> builder)
        {
            builder.ToTable("layer_dependencies");
            builder.HasKey(static e => e.Id);

            builder.Property(static e => e.Rationale)
                .HasMaxLength(1000);

            builder.HasOne(static e => e.SourceLayer)
                .WithMany(static l => l.OutgoingDependencies)
                .HasForeignKey(static e => e.SourceLayerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(static e => e.TargetLayer)
                .WithMany(static l => l.IncomingDependencies)
                .HasForeignKey(static e => e.TargetLayerId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade loops

            builder.HasIndex(static e => new { e.SourceLayerId, e.TargetLayerId })
                .IsUnique()
                .HasDatabaseName("layer_dependencies_source_target_idx");
        }
    }
}
