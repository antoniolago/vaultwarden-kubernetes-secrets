using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Services;

public interface IDatabaseLoggerService
{
    Task<long> StartSyncLogAsync(string phase, int totalItems = 0);
    Task UpdateSyncProgressAsync(long syncLogId, int processedItems, int created, int updated, int skipped, int failed, int deleted = 0);
    Task CompleteSyncLogAsync(long syncLogId, string status, string? errorMessage = null);
    Task LogSyncItemAsync(long syncLogId, string itemKey, string itemName, string namespaceName, string secretName, string status, string outcome, string? details = null);
    Task UpsertSecretStateAsync(string namespaceName, string secretName, string vaultwardenItemId, string vaultwardenItemName, string status, int dataKeysCount, string? lastError = null);
    Task CacheVaultwardenItemsAsync(List<Models.VaultwardenItem> items);
    Task<int> CleanupStaleSecretStatesAsync(List<Models.VaultwardenItem> currentItems);
}
