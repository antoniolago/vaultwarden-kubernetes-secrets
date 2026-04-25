using k8s.Models;
using VaultwardenK8sSync.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VaultwardenK8sSync.Services;

public interface IKubernetesService
{
    Task<bool> InitializeAsync();
    Task<List<string>> GetAllNamespacesAsync();
    Task<bool> NamespaceExistsAsync(string namespaceName);
    Task<List<string>> GetExistingSecretNamesAsync(string namespaceName);
    Task<List<string>> GetManagedSecretNamesAsync(string namespaceName);
    Task<OperationResult> CreateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data, Dictionary<string, string>? annotations = null, Dictionary<string, string>? customLabels = null, string? secretType = null);
    Task<OperationResult> UpdateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data, Dictionary<string, string>? annotations = null, Dictionary<string, string>? customLabels = null);
    Task<bool> DeleteSecretAsync(string namespaceName, string secretName);
    Task<bool> SecretExistsAsync(string namespaceName, string secretName);
    Task<Dictionary<string, string>?> GetSecretDataAsync(string namespaceName, string secretName);
    Task<Dictionary<string, string>?> GetSecretAnnotationsAsync(string namespaceName, string secretName);
    Task<string?> GetSecretTypeAsync(string namespaceName, string secretName);

    /// <summary>
    /// Removes only the managed keys from a secret, preserving any external keys
    /// Returns true if the secret was updated, false if no changes were needed
    /// Returns null if the secret doesn't exist or has no managed keys
    /// </summary>
    Task<bool?> RemoveManagedKeysAsync(string namespaceName, string secretName);

    /// <summary>
    /// Checks if a secret has only managed keys (no external keys)
    /// </summary>
    Task<bool> HasOnlyManagedKeysAsync(string namespaceName, string secretName);

    /// <summary>
    /// Gets all secrets that have managed keys (either created by sync service or have managed-keys annotation)
    /// </summary>
    Task<List<string>> GetSecretsWithManagedKeysAsync(string namespaceName);

    /// <summary>
    /// Gets the detected Kubernetes context name
    /// </summary>
    string? GetContextName();

    /// <summary>
    /// Exports a secret as YAML string
    /// </summary>
    Task<string?> ExportSecretAsYamlAsync(string namespaceName, string secretName);

    /// <summary>
    /// Applies a Kubernetes YAML manifest to the cluster using kubectl apply.
    /// Supports multi-document YAML (multiple objects separated by ---).
    /// </summary>
    Task<OperationResult> ApplyYamlAsync(string yaml);
}