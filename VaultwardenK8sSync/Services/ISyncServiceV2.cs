namespace VaultwardenK8sSync.Services;

public interface ISyncServiceV2
{
    Task<bool> SyncAsync();
    Task<bool> SyncNamespaceAsync(string namespaceName);
    Task<bool> CleanupOrphanedSecretsAsync();
} 