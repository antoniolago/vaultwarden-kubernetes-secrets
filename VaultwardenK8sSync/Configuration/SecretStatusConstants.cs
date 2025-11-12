namespace VaultwardenK8sSync.Configuration;

/// <summary>
/// Centralized constants for secret state status values.
/// Ensures consistency across the application.
/// </summary>
public static class SecretStatusConstants
{
    /// <summary>
    /// Secret is active and up-to-date in Kubernetes
    /// </summary>
    public const string Active = "Active";
    
    /// <summary>
    /// Secret creation or update failed
    /// </summary>
    public const string Failed = "Failed";
    
    /// <summary>
    /// Secret has been deleted/removed
    /// </summary>
    public const string Deleted = "Deleted";
}
