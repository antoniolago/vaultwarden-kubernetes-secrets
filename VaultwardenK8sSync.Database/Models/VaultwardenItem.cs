namespace VaultwardenK8sSync.Database.Models;

/// <summary>
/// Represents a Vaultwarden item cached from the last sync
/// </summary>
public class VaultwardenItem
{
    public long Id { get; set; }
    
    /// <summary>
    /// Vaultwarden item UUID
    /// </summary>
    public string ItemId { get; set; } = string.Empty;
    
    /// <summary>
    /// Item name
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Folder ID (optional)
    /// </summary>
    public string? FolderId { get; set; }
    
    /// <summary>
    /// Organization ID (optional)
    /// </summary>
    public string? OrganizationId { get; set; }
    
    /// <summary>
    /// Organization name (optional)
    /// </summary>
    public string? OrganizationName { get; set; }
    
    /// <summary>
    /// Owner name: either organization name or user email
    /// </summary>
    public string? Owner { get; set; }
    
    /// <summary>
    /// Number of custom fields
    /// </summary>
    public int FieldCount { get; set; }
    
    /// <summary>
    /// Custom field names as JSON array
    /// </summary>
    public string? FieldNamesJson { get; set; }
    
    /// <summary>
    /// Item notes (if any)
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// When this item was last fetched from Vaultwarden
    /// </summary>
    public DateTime LastFetched { get; set; }
    
    /// <summary>
    /// Whether this item has "namespaces" field (is configured for sync)
    /// </summary>
    public bool HasNamespacesField { get; set; }
    
    /// <summary>
    /// Cached namespaces value (JSON array)
    /// </summary>
    public string? NamespacesJson { get; set; }
}
