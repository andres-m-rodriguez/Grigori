using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grigori.Database.Models;

public sealed class ArchitecturePattern
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string? Diagram { get; set; } // ASCII art or text-based diagram
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<ArchitectureLayer> Layers { get; set; } = [];

    public sealed class EntityConfiguration : IEntityTypeConfiguration<ArchitecturePattern>
    {
        public void Configure(EntityTypeBuilder<ArchitecturePattern> builder)
        {
            builder.ToTable("architecture_patterns");
            builder.HasKey(static e => e.Id);

            builder.Property(static e => e.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(static e => e.Description)
                .HasMaxLength(2000)
                .IsRequired();

            builder.Property(static e => e.Diagram)
                .HasMaxLength(10000);

            builder.HasIndex(static e => e.Name)
                .IsUnique()
                .HasDatabaseName("architecture_patterns_name_idx");
        }
    }
}
