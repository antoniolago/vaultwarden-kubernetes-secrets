namespace VaultwardenK8sSync.Services;

public interface IMetricsService
{
    /// <summary>
    /// Records the duration of a sync operation
    /// </summary>
    void RecordSyncDuration(double durationSeconds, bool success);
    
    /// <summary>
    /// Records the number of secrets synced
    /// </summary>
    void RecordSecretsSynced(int count, string operation);
    
    /// <summary>
    /// Records sync errors
    /// </summary>
    void RecordSyncError(string errorType);
    
    /// <summary>
    /// Records the number of items watched from Vaultwarden
    /// </summary>
    void RecordItemsWatched(int count);
    
    /// <summary>
    /// Records API calls to Vaultwarden
    /// </summary>
    void RecordVaultwardenApiCall(string operation, bool success);
    
    /// <summary>
    /// Records Kubernetes API calls
    /// </summary>
    void RecordKubernetesApiCall(string operation, bool success);
    
    /// <summary>
    /// Sets the last successful sync timestamp
    /// </summary>
    void SetLastSuccessfulSync();
    
    /// <summary>
    /// Gets the timestamp of the last successful sync
    /// </summary>
    DateTime? GetLastSuccessfulSync();
}
