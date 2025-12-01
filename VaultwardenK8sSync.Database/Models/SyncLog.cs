namespace VaultwardenK8sSync.Database.Models;

/// <summary>
/// Represents a single sync operation log entry
/// </summary>
public class SyncLog
{
    public long Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = string.Empty; // Success, Failed, InProgress
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int CreatedSecrets { get; set; }
    public int UpdatedSecrets { get; set; }
    public int SkippedSecrets { get; set; }
    public int FailedSecrets { get; set; }
    public int DeletedSecrets { get; set; } = 0;
    public double DurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string Phase { get; set; } = string.Empty;
    public int SyncIntervalSeconds { get; set; } = 0;  // Actual sync interval from sync service
    public bool ContinuousSync { get; set; } = false;  // Whether continuous sync is enabled
}
