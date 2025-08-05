using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public interface IVaultwardenServiceV2
{
    Task<bool> AuthenticateAsync();
    Task<List<VaultwardenItemV2>> GetItemsAsync();
    Task<VaultwardenItemV2?> GetItemAsync(string id);
    Task<string> GetItemPasswordAsync(string id);
    Task<bool> IsAuthenticatedAsync();
    Task LogoutAsync();
} 