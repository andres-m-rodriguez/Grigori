using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grigori.Database.Models;

public sealed class CodeTemplate
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Language { get; set; } // e.g., "csharp", "typescript"
    public required string Category { get; set; } // e.g., "Endpoint", "Repository", "DTO"
    public required string Template { get; set; } // Code with placeholders like {{EntityName}}
    public int? LayerId { get; set; } // Optional link to architecture layer
    public DateTime CreatedAt { get; set; }

    public ArchitectureLayer? Layer { get; set; }

    public sealed class EntityConfiguration : IEntityTypeConfiguration<CodeTemplate>
    {
        public void Configure(EntityTypeBuilder<CodeTemplate> builder)
        {
            builder.ToTable("code_templates");
            builder.HasKey(static e => e.Id);

            builder.Property(static e => e.Name)
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(static e => e.Description)
                .HasMaxLength(2000)
                .IsRequired();

            builder.Property(static e => e.Language)
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(static e => e.Category)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(static e => e.Template)
                .HasMaxLength(50000)
                .IsRequired();

            builder.HasOne(static e => e.Layer)
                .WithMany(static l => l.Templates)
                .HasForeignKey(static e => e.LayerId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(static e => e.Category)
                .HasDatabaseName("code_templates_category_idx");

            builder.HasIndex(static e => e.Language)
                .HasDatabaseName("code_templates_language_idx");

            builder.HasIndex(static e => new { e.Name, e.Language })
                .IsUnique()
                .HasDatabaseName("code_templates_name_language_idx");
        }
    }
}
