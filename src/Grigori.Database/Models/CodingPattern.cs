using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grigori.Database.Models;

public sealed class CodingPattern
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public string? Example { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public sealed class EntityConfiguration : IEntityTypeConfiguration<CodingPattern>
    {
        public void Configure(EntityTypeBuilder<CodingPattern> builder)
        {
            builder.ToTable("coding_patterns");
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

            builder.Property(static e => e.Example)
                .HasColumnType("TEXT");

            builder.HasIndex(static e => e.Category)
                .HasDatabaseName("coding_patterns_category_idx");
        }
    }
}
