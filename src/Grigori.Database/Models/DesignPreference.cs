using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grigori.Database.Models;

public sealed class DesignPreference
{
    public int Id { get; set; }
    public required string Category { get; set; }
    public required string Preference { get; set; }
    public string? Rationale { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }

    public sealed class EntityConfiguration : IEntityTypeConfiguration<DesignPreference>
    {
        public void Configure(EntityTypeBuilder<DesignPreference> builder)
        {
            builder.ToTable("design_preferences");
            builder.HasKey(static e => e.Id);

            builder.Property(static e => e.Category)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(static e => e.Preference)
                .HasMaxLength(1000)
                .IsRequired();

            builder.Property(static e => e.Rationale)
                .HasMaxLength(2000);

            builder.HasIndex(static e => e.Category)
                .HasDatabaseName("design_preferences_category_idx");

            builder.HasIndex(static e => e.Priority)
                .HasDatabaseName("design_preferences_priority_idx");
        }
    }
}
