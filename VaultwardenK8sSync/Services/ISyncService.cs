using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public interface ISyncService
{
    Task<SyncSummary> SyncAsync();
    Task<SyncSummary> SyncAsync(ISyncProgressReporter? progressReporter);
    Task<bool> SyncNamespaceAsync(string namespaceName);
    Task<bool> CleanupOrphanedSecretsAsync();
    void ResetItemsHash();
} 