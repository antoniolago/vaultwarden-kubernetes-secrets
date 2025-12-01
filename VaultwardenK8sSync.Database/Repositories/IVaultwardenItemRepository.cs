using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database.Repositories;

public interface IVaultwardenItemRepository
{
    Task<List<VaultwardenItem>> GetAllAsync();
    Task<VaultwardenItem?> GetByItemIdAsync(string itemId);
    Task<DateTime?> GetLastFetchTimeAsync();
}
