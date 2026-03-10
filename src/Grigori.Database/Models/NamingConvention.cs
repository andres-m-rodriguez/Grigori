using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grigori.Database.Models;

public sealed class NamingConvention
{
    public int Id { get; set; }
    public required string Context { get; set; } // e.g., "DTO", "Repository", "Endpoint", "Entity"
    public required string Pattern { get; set; } // e.g., "{{Entity}}DtoForList", "I{{Entity}}Repository"
    public required string Example { get; set; } // e.g., "UserDtoForList", "IUserRepository"
    public string? Description { get; set; }
    public int? LayerId { get; set; } // Optional link to architecture layer
    public DateTime CreatedAt { get; set; }

    public ArchitectureLayer? Layer { get; set; }

    public sealed class EntityConfiguration : IEntityTypeConfiguration<NamingConvention>
    {
        public void Configure(EntityTypeBuilder<NamingConvention> builder)
        {
            builder.ToTable("naming_conventions");
            builder.HasKey(static e => e.Id);

            builder.Property(static e => e.Context)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(static e => e.Pattern)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(static e => e.Example)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(static e => e.Description)
                .HasMaxLength(1000);

            builder.HasOne(static e => e.Layer)
                .WithMany(static l => l.NamingConventions)
                .HasForeignKey(static e => e.LayerId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(static e => e.Context)
                .HasDatabaseName("naming_conventions_context_idx");

            builder.HasIndex(static e => new { e.Context, e.Pattern })
                .IsUnique()
                .HasDatabaseName("naming_conventions_context_pattern_idx");
        }
    }
}
