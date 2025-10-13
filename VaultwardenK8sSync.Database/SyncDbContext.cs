using Microsoft.EntityFrameworkCore;
using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database;

public class SyncDbContext : DbContext
{
    public SyncDbContext(DbContextOptions<SyncDbContext> options) : base(options)
    {
    }

    public DbSet<SyncLog> SyncLogs { get; set; }
    public DbSet<SyncItem> SyncItems { get; set; }
    public DbSet<SecretState> SecretStates { get; set; }
    public DbSet<SystemMetric> SystemMetrics { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SyncLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.StartTime).IsRequired();
            entity.HasIndex(e => e.StartTime);
        });

        modelBuilder.Entity<SyncItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ItemKey).IsRequired();
            entity.Property(e => e.Namespace).IsRequired();
            entity.Property(e => e.SecretName).IsRequired();
            entity.HasIndex(e => new { e.SyncLogId, e.Timestamp });
            entity.HasIndex(e => new { e.Namespace, e.SecretName });
            
            entity.HasOne(e => e.SyncLog)
                .WithMany()
                .HasForeignKey(e => e.SyncLogId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecretState>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Namespace, e.SecretName }).IsUnique();
            entity.HasIndex(e => e.VaultwardenItemId);
            entity.HasIndex(e => e.LastSynced);
        });

        modelBuilder.Entity<SystemMetric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MetricName, e.Timestamp });
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
