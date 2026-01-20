using System.Security.Cryptography;
using System.Text;
using Grigori.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Grigori.Database;

public class GrigoriDbContext : DbContext
{
    public GrigoriDbContext(DbContextOptions<GrigoriDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Computes a SHA256 hash of the given content for deduplication.
    /// </summary>
    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public DbSet<ChunkEntity> Chunks { get; set; } = null!;
    public DbSet<ActivityLogEntity> ActivityLogs { get; set; } = null!;
    public DbSet<SearchHistoryEntity> SearchHistory { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Configure ChunkEntity
        modelBuilder.Entity<ChunkEntity>(entity =>
        {
            entity.HasIndex(e => e.FilePath);
            entity.HasIndex(e => e.ContentHash);

            // Configure pgvector column with HNSW index for fast similarity search
            entity.Property(e => e.Embedding)
                .HasColumnType("vector(384)");

            // Create HNSW index for cosine distance similarity search
            entity.HasIndex(e => e.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        // Configure ActivityLogEntity
        modelBuilder.Entity<ActivityLogEntity>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ActivityType);
        });

        // Configure SearchHistoryEntity
        modelBuilder.Entity<SearchHistoryEntity>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
