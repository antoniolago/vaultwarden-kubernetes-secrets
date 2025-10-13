namespace VaultwardenK8sSync.Database.Models;

/// <summary>
/// Represents an individual item that was synced
/// </summary>
public class SyncItem
{
    public long Id { get; set; }
    public long SyncLogId { get; set; }
    public SyncLog? SyncLog { get; set; }
    
    public string ItemKey { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string SecretName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string Outcome { get; set; } = string.Empty; // Created, Updated, Skipped, Failed
    public DateTime Timestamp { get; set; }
}
