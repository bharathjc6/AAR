// =============================================================================
// AAR.Infrastructure - Persistence/AarDbContext.cs
// Entity Framework Core DbContext for the application
// =============================================================================

using AAR.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AAR.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core DbContext for AAR
/// Supports SQLite for development and Azure SQL for production
/// </summary>
public class AarDbContext : DbContext
{
    public AarDbContext(DbContextOptions<AarDbContext> options) : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<FileRecord> FileRecords => Set<FileRecord>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReviewFinding> ReviewFindings => Set<ReviewFinding>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<JobCheckpoint> JobCheckpoints => Set<JobCheckpoint>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<OrganizationQuota> OrganizationQuotas => Set<OrganizationQuota>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Project configuration
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.GitRepoUrl).HasMaxLength(500);
            entity.Property(e => e.OriginalFileName).HasMaxLength(500);
            entity.Property(e => e.StoragePath).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.ApiKeyId);

            entity.HasOne(e => e.Report)
                .WithOne(e => e.Project)
                .HasForeignKey<Report>(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Files)
                .WithOne(e => e.Project)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // FileRecord configuration
        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RelativePath).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Extension).HasMaxLength(50);
            entity.Property(e => e.ContentHash).HasMaxLength(100);
            
            // Store FileMetrics as JSON
            entity.OwnsOne(e => e.Metrics, m =>
            {
                m.Property(p => p.LinesOfCode).HasColumnName("Metrics_LinesOfCode");
                m.Property(p => p.TotalLines).HasColumnName("Metrics_TotalLines");
                m.Property(p => p.CyclomaticComplexity).HasColumnName("Metrics_CyclomaticComplexity");
                m.Property(p => p.TypeCount).HasColumnName("Metrics_TypeCount");
                m.Property(p => p.MethodCount).HasColumnName("Metrics_MethodCount");
                m.Property(p => p.NamespaceCount).HasColumnName("Metrics_NamespaceCount");
            });

            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.Extension);
        });

        // Report configuration
        modelBuilder.Entity<Report>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Summary).HasMaxLength(10000);
            entity.Property(e => e.PdfReportPath).HasMaxLength(500);
            entity.Property(e => e.JsonReportPath).HasMaxLength(500);
            entity.Property(e => e.PatchFilesPath).HasMaxLength(500);
            entity.Property(e => e.ReportVersion).HasMaxLength(20);
            
            // Store Recommendations as JSON array
            entity.Property(e => e.Recommendations)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>());

            entity.HasIndex(e => e.ProjectId).IsUnique();
        });

        // ReviewFinding configuration
        modelBuilder.Entity<ReviewFinding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FilePath).HasMaxLength(1000);
            entity.Property(e => e.Description).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.Explanation).HasMaxLength(5000).IsRequired();
            entity.Property(e => e.SuggestedFix).HasMaxLength(5000);
            entity.Property(e => e.FixedCodeSnippet).HasMaxLength(10000);
            entity.Property(e => e.OriginalCodeSnippet).HasMaxLength(10000);

            // Store LineRange as owned type
            entity.OwnsOne(e => e.LineRange, lr =>
            {
                lr.Property(p => p.Start).HasColumnName("LineRange_Start");
                lr.Property(p => p.End).HasColumnName("LineRange_End");
            });

            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.ReportId);
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.Category);

            entity.HasOne(e => e.FileRecord)
                .WithMany(e => e.Findings)
                .HasForeignKey(e => e.FileRecordId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Report)
                .WithMany(e => e.Findings)
                .HasForeignKey(e => e.ReportId)
                .OnDelete(DeleteBehavior.Restrict);  // Changed from Cascade to avoid multiple cascade paths in SQL Server
        });

        // ApiKey configuration
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.KeyHash).HasMaxLength(100).IsRequired();
            entity.Property(e => e.KeyPrefix).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Scopes).HasMaxLength(500);

            entity.HasIndex(e => e.KeyPrefix);
            entity.HasIndex(e => e.IsActive);
        });

        // Chunk configuration for vector storage
        modelBuilder.Entity<Chunk>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ChunkHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.FilePath).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Language).HasMaxLength(50);
            entity.Property(e => e.TextHash).HasMaxLength(64);
            entity.Property(e => e.SemanticType).HasMaxLength(50);
            entity.Property(e => e.SemanticName).HasMaxLength(200);
            entity.Property(e => e.EmbeddingModel).HasMaxLength(100);
            
            // Large content fields
            entity.Property(e => e.Content).HasMaxLength(100000);
            entity.Property(e => e.EmbeddingJson).HasMaxLength(50000);

            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => new { e.ProjectId, e.ChunkHash }).IsUnique(); // Unique per project
            entity.HasIndex(e => new { e.ProjectId, e.FilePath });

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // JobCheckpoint configuration
        modelBuilder.Entity<JobCheckpoint>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.SerializedState).HasMaxLength(50000);
            
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.LastCheckpointAt);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UploadSession configuration
        modelBuilder.Entity<UploadSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ProjectDescription).HasMaxLength(2000);
            entity.Property(e => e.FileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.StoragePath).HasMaxLength(500);
            entity.Property(e => e.ContentHash).HasMaxLength(100);
            entity.Property(e => e.UploadedParts).HasMaxLength(10000);
            
            entity.HasIndex(e => e.ApiKeyId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt);
        });

        // OrganizationQuota configuration
        modelBuilder.Entity<OrganizationQuota>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrganizationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SuspensionReason).HasMaxLength(500);
            
            entity.Property(e => e.TotalCredits).HasPrecision(18, 4);
            entity.Property(e => e.CreditsUsed).HasPrecision(18, 4);
            
            entity.HasIndex(e => e.OrganizationId).IsUnique();
            entity.HasIndex(e => e.PeriodEndDate);
        });
    }
}
