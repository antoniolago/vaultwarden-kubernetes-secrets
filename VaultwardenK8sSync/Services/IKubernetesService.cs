using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public interface IKubernetesService
{
    Task<bool> InitializeAsync();
    Task<List<string>> GetExistingSecretNamesAsync(string namespaceName);
    Task<bool> CreateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data);
    Task<bool> UpdateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data);
    Task<bool> DeleteSecretAsync(string namespaceName, string secretName);
    Task<bool> SecretExistsAsync(string namespaceName, string secretName);
} 