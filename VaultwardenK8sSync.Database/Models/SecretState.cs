namespace VaultwardenK8sSync.Database.Models;

/// <summary>
/// Tracks the current state of synced secrets
/// </summary>
public class SecretState
{
    public long Id { get; set; }
    public string Namespace { get; set; } = string.Empty;
    public string SecretName { get; set; } = string.Empty;
    public string VaultwardenItemId { get; set; } = string.Empty;
    public string VaultwardenItemName { get; set; } = string.Empty;
    public DateTime LastSynced { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty; // Active, Deleted, Failed
    public int DataKeysCount { get; set; }
    public string? LastError { get; set; }
}
