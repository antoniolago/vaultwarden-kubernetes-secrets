using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database.Repositories;

public interface ISecretStateRepository
{
    Task<SecretState> UpsertAsync(SecretState secretState);
    Task<SecretState?> GetByNamespaceAndNameAsync(string namespaceName, string secretName);
    Task<List<SecretState>> GetAllAsync();
    Task<List<SecretState>> GetByNamespaceAsync(string namespaceName);
    Task<List<SecretState>> GetActiveSecretsAsync();
    Task DeleteAsync(long id);
}
