namespace VaultwardenK8sSync.Services;

public interface ISyncService
{
    Task<bool> SyncAsync();
    Task<bool> SyncNamespaceAsync(string namespaceName);
    Task<bool> CleanupOrphanedSecretsAsync();
} 