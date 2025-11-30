using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database;

public class SyncDbContext : DbContext
{
    public SyncDbContext(DbContextOptions<SyncDbContext> options) : base(options)
    {
        // Enable WAL mode for better concurrency when connection opens
        if (Database.GetDbConnection() is SqliteConnection sqliteConnection)
        {
            sqliteConnection.StateChange += (sender, args) =>
            {
                if (args.CurrentState == System.Data.ConnectionState.Open)
                {
                    using var command = sqliteConnection.CreateCommand();
                    command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=30000;";
                    command.ExecuteNonQuery();
                }
            };
        }
    }

    public DbSet<SyncLog> SyncLogs { get; set; }
    public DbSet<SyncItem> SyncItems { get; set; }
    public DbSet<SecretState> SecretStates { get; set; }
    public DbSet<SystemMetric> SystemMetrics { get; set; }
    public DbSet<VaultwardenItem> VaultwardenItems { get; set; }

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

        modelBuilder.Entity<VaultwardenItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ItemId).IsUnique();
            entity.HasIndex(e => e.LastFetched);
            entity.HasIndex(e => e.HasNamespacesField);
            entity.Property(e => e.ItemId).IsRequired();
            entity.Property(e => e.Name).IsRequired();
        });
    }
}
