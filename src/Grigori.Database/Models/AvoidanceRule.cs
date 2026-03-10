using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grigori.Database.Models;

public enum AvoidanceSeverity
{
    Avoid = 0,
    StronglyAvoid = 1,
    Never = 2
}

public sealed class AvoidanceRule
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public AvoidanceSeverity Severity { get; set; }
    public DateTime CreatedAt { get; set; }

    // Optional: "Instead of this, do this" - links to a preferred alternative
    public int? PreferredAlternativeId { get; set; }
    public DesignPreference? PreferredAlternative { get; set; }

    public sealed class EntityConfiguration : IEntityTypeConfiguration<AvoidanceRule>
    {
        public void Configure(EntityTypeBuilder<AvoidanceRule> builder)
        {
            builder.ToTable("avoidance_rules");
            builder.HasKey(static e => e.Id);

            builder.Property(static e => e.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(static e => e.Description)
                .HasMaxLength(2000)
                .IsRequired();

            builder.Property(static e => e.Category)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(static e => e.Severity)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.HasIndex(static e => e.Category)
                .HasDatabaseName("avoidance_rules_category_idx");

            builder.HasIndex(static e => e.Severity)
                .HasDatabaseName("avoidance_rules_severity_idx");

            builder.HasOne(static e => e.PreferredAlternative)
                .WithMany()
                .HasForeignKey(static e => e.PreferredAlternativeId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
