using Grigori.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Grigori.Database;

public class GrigoriDbContext : DbContext
{
    public GrigoriDbContext(DbContextOptions<GrigoriDbContext> options) : base(options)
    {
    }

    public DbSet<CodeChunk> Chunks => Set<CodeChunk>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<SearchHistory> SearchHistories => Set<SearchHistory>();
    public DbSet<MentalNote> MentalNotes => Set<MentalNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // CodeChunk configuration
        modelBuilder.Entity<CodeChunk>(entity =>
        {
            entity.ToTable("chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FilePath).HasColumnName("file_path").IsRequired();
            entity.Property(e => e.StartLine).HasColumnName("start_line");
            entity.Property(e => e.EndLine).HasColumnName("end_line");
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.ContentHash).HasColumnName("content_hash").IsRequired();
            entity.Property(e => e.Embedding).HasColumnName("embedding").IsRequired();
            entity.Property(e => e.Features).HasColumnName("features");
            entity.Property(e => e.IndexedAt).HasColumnName("indexed_at");

            entity.HasIndex(e => e.FilePath);
            entity.HasIndex(e => e.ContentHash);
        });

        // ActivityLog configuration
        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.ToTable("activity_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ActivityType).HasColumnName("activity_type").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").IsRequired();
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");
            entity.Property(e => e.Details).HasColumnName("details");

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ActivityType);
        });

        // SearchHistory configuration
        modelBuilder.Entity<SearchHistory>(entity =>
        {
            entity.ToTable("search_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Query).HasColumnName("query").IsRequired();
            entity.Property(e => e.ResultCount).HasColumnName("result_count");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.CacheHit).HasColumnName("cache_hit");
            entity.Property(e => e.UsedHnsw).HasColumnName("used_hnsw");
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");

            entity.HasIndex(e => e.Timestamp);
        });

        // MentalNote configuration
        modelBuilder.Entity<MentalNote>(entity =>
        {
            entity.ToTable("mental_notes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectName).HasColumnName("project_name").IsRequired();
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.Tags).HasColumnName("tags");
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.ProjectName);
            entity.HasIndex(e => new { e.ProjectName, e.Category });
            entity.HasIndex(e => new { e.ProjectName, e.Priority });
        });
    }
}
