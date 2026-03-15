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
    /// <summary>
/// Checks whether a Kubernetes namespace with the specified name exists.
/// </summary>
/// <param name="namespaceName">The name of the namespace to check.</param>
/// <returns>`true` if the namespace exists, `false` otherwise.</returns>
Task<bool> NamespaceExistsAsync(string namespaceName);
    /// <summary>
/// Retrieves the names of all secrets that exist in the specified namespace.
/// </summary>
/// <param name="namespaceName">The Kubernetes namespace to query for secrets.</param>
/// <returns>A list of secret names present in the namespace. Returns an empty list if no secrets are found.</returns>
Task<List<string>> GetExistingSecretNamesAsync(string namespaceName);
    /// <summary>
/// Retrieves the names of secrets in the specified namespace that are managed by the sync service.
/// </summary>
/// <param name="namespaceName">The Kubernetes namespace to search for managed secrets.</param>
/// <returns>A list of secret names that are managed by the service; an empty list if none are found.</returns>
Task<List<string>> GetManagedSecretNamesAsync(string namespaceName);
    /// <summary>
/// Creates a Kubernetes secret in the specified namespace using the provided data.
/// </summary>
/// <param name="namespaceName">The namespace in which to create the secret.</param>
/// <param name="secretName">The name to assign to the created secret.</param>
/// <param name="data">A mapping of secret keys to their string values.</param>
/// <param name="annotations">Optional annotations to attach to the secret.</param>
/// <param name="customLabels">Optional custom labels to attach to the secret.</param>
/// <param name="secretType">Optional Kubernetes secret type (for example, "Opaque").</param>
/// <returns>An <see cref="OperationResult"/> describing whether the creation succeeded and containing related details.</returns>
Task<OperationResult> CreateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data, Dictionary<string, string>? annotations = null, Dictionary<string, string>? customLabels = null, string? secretType = null);
    /// <summary>
/// Update an existing secret in the given namespace with the provided data and optionally set annotations and custom labels.
/// </summary>
/// <param name="namespaceName">The namespace that contains the secret.</param>
/// <param name="secretName">The name of the secret to update.</param>
/// <param name="data">Key/value pairs to set on the secret; existing keys with the same name will be replaced.</param>
/// <param name="annotations">Optional annotations to apply to the secret.</param>
/// <param name="customLabels">Optional custom labels to apply to the secret.</param>
/// <returns>An OperationResult describing whether the update succeeded and any related details.</returns>
Task<OperationResult> UpdateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data, Dictionary<string, string>? annotations = null, Dictionary<string, string>? customLabels = null);
    /// <summary>
/// Deletes the specified secret from the given namespace.
/// </summary>
/// <param name="namespaceName">The namespace that contains the secret.</param>
/// <param name="secretName">The name of the secret to delete.</param>
/// <returns>`true` if the secret was deleted, `false` otherwise.</returns>
Task<bool> DeleteSecretAsync(string namespaceName, string secretName);
    /// <summary>
/// Determines whether a secret with the given name exists in the specified namespace.
/// </summary>
/// <param name="namespaceName">The Kubernetes namespace to check.</param>
/// <param name="secretName">The name of the secret to look for.</param>
/// <returns>`true` if the secret exists in the namespace, `false` otherwise.</returns>
Task<bool> SecretExistsAsync(string namespaceName, string secretName);
    Task<Dictionary<string, string>?> GetSecretDataAsync(string namespaceName, string secretName);
    Task<Dictionary<string, string>?> GetSecretAnnotationsAsync(string namespaceName, string secretName);

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
    /// Gets the full V1Secret object from Kubernetes
    /// </summary>
    /// <param name="namespaceName">The namespace containing the secret</param>
    /// <param name="secretName">The name of the secret</param>
    /// <returns>The V1Secret object, or null if not found</returns>
    Task<V1Secret?> GetSecretAsync(string namespaceName, string secretName);
    
    /// <summary>
    /// Exports a secret as properly formatted YAML with literal block style for multiline values
    /// </summary>
    /// <param name="namespaceName">The namespace containing the secret</param>
    /// <param name="secretName">The name of the secret to export</param>
    /// <returns>The secret formatted as YAML string, or null if not found</returns>
    Task<string?> ExportSecretAsYamlAsync(string namespaceName, string secretName);
} 