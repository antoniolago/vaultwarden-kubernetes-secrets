using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public interface IVaultwardenService
{
    Task<bool> AuthenticateAsync();
    Task<List<VaultwardenItem>> GetItemsAsync();
    Task<VaultwardenItem?> GetItemAsync(string id);
    Task<string> GetItemPasswordAsync(string id);
    Task<bool> IsAuthenticatedAsync();
    Task LogoutAsync();
    Task<Dictionary<string, string>> GetOrganizationsMapAsync();
    Task<string?> GetCurrentUserEmailAsync();
}

public class OrganizationInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
} 