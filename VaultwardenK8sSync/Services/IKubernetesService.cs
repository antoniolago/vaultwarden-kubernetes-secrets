using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public interface IKubernetesService
{
    Task<bool> InitializeAsync();
    Task<List<string>> GetAllNamespacesAsync();
    Task<List<string>> GetExistingSecretNamesAsync(string namespaceName);
    Task<List<string>> GetManagedSecretNamesAsync(string namespaceName);
    Task<bool> CreateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data);
    Task<bool> UpdateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data);
    Task<bool> DeleteSecretAsync(string namespaceName, string secretName);
    Task<bool> SecretExistsAsync(string namespaceName, string secretName);
    Task<Dictionary<string, string>?> GetSecretDataAsync(string namespaceName, string secretName);
    
    /// <summary>
    /// Exports a secret as properly formatted YAML with literal block style for multiline values
    /// </summary>
    /// <param name="namespaceName">The namespace containing the secret</param>
    /// <param name="secretName">The name of the secret to export</param>
    /// <returns>The secret formatted as YAML string, or null if not found</returns>
    Task<string?> ExportSecretAsYamlAsync(string namespaceName, string secretName);
} 