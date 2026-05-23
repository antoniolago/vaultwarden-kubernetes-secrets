using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database.Repositories;

public interface ISecretStateRepository
{
    Task<SecretState> UpsertAsync(SecretState secretState);
    Task<SecretState?> GetByNamespaceAndNameAsync(string namespaceName, string secretName);
    Task<List<SecretState>> GetAllAsync();
    Task<List<SecretState>> GetByNamespaceAsync(string namespaceName);
    Task<List<SecretState>> GetActiveSecretsAsync();
    Task<(int ActiveSecretsCount, int TotalNamespaces)> GetOverviewStatsAsync();
    Task DeleteAsync(long id);
    Task<string?> GetSecretHashAsync(string namespaceName, string secretName);
    Task UpdateSecretHashAsync(string namespaceName, string secretName, string contentHash);
}
