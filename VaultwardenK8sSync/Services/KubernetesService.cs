using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VaultwardenK8sSync.Services;

public class KubernetesService : IKubernetesService
{
    private readonly ILogger<KubernetesService> _logger;
    private readonly KubernetesSettings _config;
    private IKubernetes? _client;

    public KubernetesService(ILogger<KubernetesService> logger, KubernetesSettings config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<bool> InitializeAsync()
    {
        // Skip if already initialized
        if (_client != null)
        {
            return true;
        }

        try
        {
            _logger.LogDebug("Initializing Kubernetes client...");

            KubernetesClientConfiguration config;
            if (_config.InCluster)
            {
                config = KubernetesClientConfiguration.InClusterConfig();
                _logger.LogDebug("Using in-cluster configuration");
            }
            else
            {
                config = !string.IsNullOrEmpty(_config.KubeConfigPath)
                    ? KubernetesClientConfiguration.BuildConfigFromConfigFile(_config.KubeConfigPath, _config.Context)
                    : KubernetesClientConfiguration.BuildDefaultConfig();
                
                _logger.LogDebug("Using kubeconfig configuration: {KubeConfigPath}, Context: {Context}, Host: {Host}", 
                    _config.KubeConfigPath ?? "default", 
                    _config.Context ?? "current", 
                    config.Host);
            }

            _client = new Kubernetes(config);

            // Test the connection
            var version = await _client.Version.GetCodeAsync();
            _logger.LogInformation("Connected to Kubernetes API version: {Version} at {Host}", version.GitVersion, config.Host);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Kubernetes client");
            return false;
        }
    }

    public async Task<List<string>> GetAllNamespacesAsync()
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var namespaces = await _client.CoreV1.ListNamespaceAsync();
            return namespaces.Items.Select(ns => ns.Metadata.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all namespaces");
            return new List<string>();
        }
    }

    public async Task<bool> NamespaceExistsAsync(string namespaceName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            await _client.CoreV1.ReadNamespaceAsync(namespaceName);
            return true;
        }
        catch (k8s.Autorest.HttpOperationException httpEx) when (httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Namespace {Namespace} does not exist", namespaceName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if namespace {Namespace} exists, assuming it doesn't", namespaceName);
            return false;
        }
    }

    public async Task<List<string>> GetExistingSecretNamesAsync(string namespaceName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(namespaceName);
            return secrets.Items.Select(s => s.Metadata.Name).ToList();
        }
        catch (k8s.Autorest.HttpOperationException httpEx) when (httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var errorMessage = ParseKubernetesErrorMessage(httpEx.Response?.Content);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                _logger.LogWarning("Cannot list secrets: {ErrorMessage}", errorMessage);
            }
            else
            {
                _logger.LogWarning("Cannot list secrets - namespace {Namespace} does not exist", namespaceName);
            }
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get existing secrets in namespace {Namespace}", namespaceName);
            return new List<string>();
        }
    }

    public async Task<List<string>> GetManagedSecretNamesAsync(string namespaceName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(namespaceName);
            var managedSecrets = new List<string>();
            
            foreach (var secret in secrets.Items)
            {
                // Check if the secret was created by the sync service (not just managed by the app)
                if (secret.Metadata?.Labels != null)
                {
                    if (secret.Metadata.Labels.ContainsKey(Constants.Kubernetes.CreatedByLabel) &&
                        secret.Metadata.Labels[Constants.Kubernetes.CreatedByLabel] == Constants.Kubernetes.SyncServiceValue)
                    {
                        managedSecrets.Add(secret.Metadata.Name);
                    }
                }
            }
            
            return managedSecrets;
        }
        catch (k8s.Autorest.HttpOperationException httpEx) when (httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var errorMessage = ParseKubernetesErrorMessage(httpEx.Response?.Content);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                _logger.LogWarning("Cannot list managed secrets: {ErrorMessage}", errorMessage);
            }
            else
            {
                _logger.LogWarning("Cannot list managed secrets - namespace {Namespace} does not exist", namespaceName);
            }
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get managed secrets in namespace {Namespace}", namespaceName);
            return new List<string>();
        }
    }

    public async Task<OperationResult> CreateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data, Dictionary<string, string>? annotations = null, Dictionary<string, string>? customLabels = null)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            // Start with required management labels
            var labels = new Dictionary<string, string>
            {
                { Constants.Kubernetes.ManagedByLabel, Constants.Kubernetes.ManagedByValue },
                { Constants.Kubernetes.CreatedByLabel, Constants.Kubernetes.SyncServiceValue }
            };

            // Merge custom labels if provided (custom labels cannot override management labels)
            if (customLabels != null)
            {
                foreach (var kvp in customLabels)
                {
                    if (!labels.ContainsKey(kvp.Key))
                    {
                        labels[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Build annotations - include managed-keys annotation
            var secretAnnotations = new Dictionary<string, string>();
            if (annotations != null)
            {
                foreach (var kvp in annotations)
                {
                    secretAnnotations[kvp.Key] = kvp.Value;
                }
            }
            // Track which keys are managed by Vaultwarden sync
            secretAnnotations[Constants.Kubernetes.ManagedKeysAnnotationKey] = SerializeManagedKeysAnnotation(data.Keys);

            var metadata = new V1ObjectMeta
            {
                Name = secretName,
                NamespaceProperty = namespaceName,
                Labels = labels,
                Annotations = secretAnnotations
            };

            var secret = new V1Secret
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = metadata,
                Type = Constants.Kubernetes.SecretType,
                Data = data.ToDictionary(kvp => kvp.Key, kvp => System.Text.Encoding.UTF8.GetBytes(kvp.Value))
            };

            await _client.CoreV1.CreateNamespacedSecretAsync(secret, namespaceName);
            _logger.LogInformation("Created secret {SecretName} in namespace {Namespace} with {KeyCount} managed keys", secretName, namespaceName, data.Count);
            return OperationResult.Successful();
        }
        catch (k8s.Autorest.HttpOperationException httpEx)
        {
            var status = httpEx.Response?.StatusCode;
            if (status == System.Net.HttpStatusCode.NotFound)
            {
                // Try to parse the Kubernetes error message from the response
                var errorMessage = ParseKubernetesErrorMessage(httpEx.Response?.Content);
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    // _logger.LogWarning("Cannot create secret {SecretName}: {ErrorMessage}", secretName, errorMessage);
                    return OperationResult.Failed(errorMessage);
                }
                else
                {
                    var message = $"Namespace '{namespaceName}' does not exist";
                    // _logger.LogWarning("Cannot create secret {SecretName} - namespace {Namespace} does not exist", secretName, namespaceName);
                    return OperationResult.Failed(message);
                }
            }
            else
            {
                var errorMessage = ParseKubernetesErrorMessage(httpEx.Response?.Content);
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    _logger.LogError("Failed to create secret {SecretName}: {ErrorMessage}", secretName, errorMessage);
                    return OperationResult.Failed(errorMessage);
                }
                else
                {
                    _logger.LogError(httpEx, "Failed to create secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
                    return OperationResult.Failed($"HTTP {status}: {httpEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return OperationResult.Failed(ex.Message);
        }
    }

    public async Task<OperationResult> UpdateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data, Dictionary<string, string>? annotations = null, Dictionary<string, string>? customLabels = null)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            // Fetch the existing secret to merge keys
            var existingSecret = await _client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);

            // Get previously managed keys from annotation
            string? previousManagedKeysJson = null;
            existingSecret.Metadata?.Annotations?.TryGetValue(Constants.Kubernetes.ManagedKeysAnnotationKey, out previousManagedKeysJson);
            var previousManagedKeys = ParseManagedKeysAnnotation(previousManagedKeysJson, _logger);

            // Start with existing secret data
            var mergedData = new Dictionary<string, byte[]>();
            if (existingSecret.Data != null)
            {
                foreach (var kvp in existingSecret.Data)
                {
                    // Only keep keys that were NOT previously managed by us
                    // (we'll add the new managed keys next)
                    if (!previousManagedKeys.Contains(kvp.Key))
                    {
                        mergedData[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Add the new Vaultwarden-synced keys
            foreach (var kvp in data)
            {
                mergedData[kvp.Key] = System.Text.Encoding.UTF8.GetBytes(kvp.Value);
            }

            // Track the new managed keys
            var newManagedKeys = data.Keys.ToList();
            var managedKeysAnnotation = SerializeManagedKeysAnnotation(newManagedKeys);

            // Start with required management labels
            var labels = new Dictionary<string, string>
            {
                { Constants.Kubernetes.ManagedByLabel, Constants.Kubernetes.ManagedByValue },
                { Constants.Kubernetes.CreatedByLabel, Constants.Kubernetes.SyncServiceValue }
            };

            // Merge custom labels if provided (custom labels cannot override management labels)
            if (customLabels != null)
            {
                foreach (var kvp in customLabels)
                {
                    if (!labels.ContainsKey(kvp.Key))
                    {
                        labels[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Preserve existing labels that aren't management labels
            if (existingSecret.Metadata?.Labels != null)
            {
                foreach (var kvp in existingSecret.Metadata.Labels)
                {
                    if (!labels.ContainsKey(kvp.Key))
                    {
                        labels[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Build annotations - start with existing, then merge provided, then add managed-keys
            var mergedAnnotations = new Dictionary<string, string>();
            if (existingSecret.Metadata?.Annotations != null)
            {
                foreach (var kvp in existingSecret.Metadata.Annotations)
                {
                    mergedAnnotations[kvp.Key] = kvp.Value;
                }
            }
            if (annotations != null)
            {
                foreach (var kvp in annotations)
                {
                    mergedAnnotations[kvp.Key] = kvp.Value;
                }
            }
            mergedAnnotations[Constants.Kubernetes.ManagedKeysAnnotationKey] = managedKeysAnnotation;

            var metadata = new V1ObjectMeta
            {
                Name = secretName,
                NamespaceProperty = namespaceName,
                Labels = labels,
                Annotations = mergedAnnotations
            };

            var secret = new V1Secret
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = metadata,
                Type = existingSecret.Type ?? Constants.Kubernetes.SecretType, // Preserve existing type (immutable field)
                Data = mergedData
            };

            await _client.CoreV1.ReplaceNamespacedSecretAsync(secret, secretName, namespaceName);
            _logger.LogInformation("Updated secret {SecretName} in namespace {Namespace} (merged {NewKeyCount} keys, preserved {ExternalKeyCount} external keys)",
                secretName, namespaceName, newManagedKeys.Count, mergedData.Count - newManagedKeys.Count);
            return OperationResult.Successful();
        }
        catch (k8s.Autorest.HttpOperationException httpEx)
        {
            var status = httpEx.Response?.StatusCode;
            var errorMessage = ParseKubernetesErrorMessage(httpEx.Response?.Content);

            if (status == System.Net.HttpStatusCode.NotFound)
            {
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    _logger.LogWarning("Cannot update secret {SecretName}: {ErrorMessage}", secretName, errorMessage);
                    return OperationResult.Failed(errorMessage);
                }
                else
                {
                    var message = $"Namespace '{namespaceName}' does not exist";
                    _logger.LogWarning("Cannot update secret {SecretName} - namespace {Namespace} does not exist", secretName, namespaceName);
                    return OperationResult.Failed(message);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    _logger.LogError("Failed to update secret {SecretName}: {ErrorMessage}", secretName, errorMessage);
                    return OperationResult.Failed(errorMessage);
                }
                else
                {
                    _logger.LogError(httpEx, "Failed to update secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
                    return OperationResult.Failed($"HTTP {status}: {httpEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return OperationResult.Failed(ex.Message);
        }
    }

    public async Task<bool> DeleteSecretAsync(string namespaceName, string secretName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            await _client.CoreV1.DeleteNamespacedSecretAsync(secretName, namespaceName);
            _logger.LogInformation("Deleted secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return true;
        }
        catch (k8s.Autorest.HttpOperationException httpEx)
        {
            var status = httpEx.Response?.StatusCode;
            var errorMessage = ParseKubernetesErrorMessage(httpEx.Response?.Content);
            
            if (status == System.Net.HttpStatusCode.NotFound)
            {
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    _logger.LogWarning("Cannot delete secret {SecretName}: {ErrorMessage}", secretName, errorMessage);
                }
                else
                {
                    _logger.LogWarning("Cannot delete secret {SecretName} - namespace {Namespace} or secret does not exist", secretName, namespaceName);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    _logger.LogError("Failed to delete secret {SecretName}: {ErrorMessage}", secretName, errorMessage);
                }
                else
                {
                    _logger.LogError(httpEx, "Failed to delete secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string namespaceName, string secretName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secret = await _client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);
            return secret != null;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (System.Net.Http.HttpRequestException httpEx)
        {
            // Provide more helpful error message for connection issues
            var message = httpEx.InnerException is System.Net.Sockets.SocketException socketEx
                ? $"Kubernetes API connection failed: {socketEx.Message}. " +
                  $"Please check your kubeconfig configuration. " +
                  $"If using port-forward, ensure it's active. " +
                  $"If running in-cluster, ensure KUBERNETES__INCLUSTER=true."
                : $"Kubernetes API connection failed: {httpEx.Message}";
            
            _logger.LogError(httpEx, "Failed to check if secret {SecretName} exists in namespace {Namespace}. {Message}", 
                secretName, namespaceName, message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if secret {SecretName} exists in namespace {Namespace}", secretName, namespaceName);
            return false;
        }
    }

    public async Task<Dictionary<string, string>?> GetSecretDataAsync(string namespaceName, string secretName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secret = await _client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);
            if (secret?.Data == null)
                return null;

            var data = new Dictionary<string, string>();
            foreach (var kvp in secret.Data)
            {
                data[kvp.Key] = System.Text.Encoding.UTF8.GetString(kvp.Value);
            }
            
            return data;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (k8s.Autorest.HttpOperationException httpEx)
        {
            var errorMessage = ParseKubernetesErrorMessage(httpEx.Response?.Content);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                _logger.LogError("Failed to get secret data for {SecretName}: {ErrorMessage}", secretName, errorMessage);
            }
            else
            {
                _logger.LogError(httpEx, "Failed to get secret data for {SecretName} in namespace {Namespace}", secretName, namespaceName);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret data for {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return null;
        }
    }

    public async Task<Dictionary<string, string>?> GetSecretAnnotationsAsync(string namespaceName, string secretName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secret = await _client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);
            var annotations = secret.Metadata?.Annotations;
            return annotations != null ? new Dictionary<string, string>(annotations) : new Dictionary<string, string>();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get annotations for secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return null;
        }
    }

    public async Task<V1Secret?> GetSecretAsync(string namespaceName, string secretName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            return await _client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return null;
        }
    }

    public async Task<string?> ExportSecretAsYamlAsync(string namespaceName, string secretName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secret = await _client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);
            if (secret?.Data == null)
                return null;

            var yamlBuilder = new System.Text.StringBuilder();
            yamlBuilder.AppendLine("apiVersion: v1");
            yamlBuilder.AppendLine("kind: Secret");
            yamlBuilder.AppendLine("metadata:");
            yamlBuilder.AppendLine($"  name: {secretName}");
            yamlBuilder.AppendLine($"  namespace: {namespaceName}");
            
            if (secret.Metadata?.Labels != null && secret.Metadata.Labels.Any())
            {
                yamlBuilder.AppendLine("  labels:");
                foreach (var label in secret.Metadata.Labels.OrderBy(l => l.Key))
                {
                    yamlBuilder.AppendLine($"    {label.Key}: {label.Value}");
                }
            }
            
            yamlBuilder.AppendLine($"type: {secret.Type ?? "Opaque"}");
            yamlBuilder.AppendLine("data:");

            foreach (var kvp in secret.Data.OrderBy(k => k.Key))
            {
                var value = System.Text.Encoding.UTF8.GetString(kvp.Value);
                var normalizedValue = value.Replace("\r\n", "\n").Replace("\r", "\n");
                
                if (normalizedValue.Contains('\n'))
                {
                    // Use literal block style for multiline values
                    yamlBuilder.AppendLine($"  {kvp.Key}: |");
                    var lines = normalizedValue.Split('\n');
                    foreach (var line in lines)
                    {
                        yamlBuilder.AppendLine($"    {line}");
                    }
                }
                else
                {
                    // Use simple string for single-line values
                    yamlBuilder.AppendLine($"  {kvp.Key}: {value}");
                }
            }

            return yamlBuilder.ToString();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export secret {SecretName} in namespace {Namespace} as YAML", secretName, namespaceName);
            return null;
        }
    }

    private static string? ParseKubernetesErrorMessage(string? responseContent)
    {
        if (string.IsNullOrEmpty(responseContent))
            return null;

        try
        {
            // Try to parse the JSON response to extract the message field
            using var document = System.Text.Json.JsonDocument.Parse(responseContent);
            if (document.RootElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }
        }
        catch
        {
            // If JSON parsing fails, return null to fall back to default error handling
        }

        return null;
    }

    /// <summary>
    /// Parses the managed-keys annotation JSON into a list of key names
    /// </summary>
    internal static List<string> ParseManagedKeysAnnotation(string? annotationValue, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(annotationValue))
            return new List<string>();

        try
        {
            var keys = System.Text.Json.JsonSerializer.Deserialize<List<string>>(annotationValue);
            return keys ?? new List<string>();
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to parse managed-keys annotation: {Value}. Treating as empty list.", annotationValue);
            return new List<string>();
        }
    }

    /// <summary>
    /// Removes only the managed keys from a secret, preserving any external keys
    /// Returns true if the secret was updated, false if no changes were needed
    /// Returns null if the secret doesn't exist or has no managed keys
    /// </summary>
    public async Task<bool?> RemoveManagedKeysAsync(string namespaceName, string secretName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            // Fetch the existing secret
            var existingSecret = await _client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);
            
            // Get managed keys from annotation
            string? managedKeysJson = null;
            existingSecret.Metadata?.Annotations?.TryGetValue(Constants.Kubernetes.ManagedKeysAnnotationKey, out managedKeysJson);
            var managedKeysList = ParseManagedKeysAnnotation(managedKeysJson, _logger);

            if (!managedKeysList.Any())
            {
                _logger.LogDebug("Secret {SecretName} in namespace {Namespace} has no managed keys to remove", secretName, namespaceName);
                return null;
            }

            // Use HashSet for O(1) lookup instead of O(n) List.Contains
            var managedKeys = new HashSet<string>(managedKeysList, StringComparer.OrdinalIgnoreCase);

            // Check if secret has only managed keys (no external keys)
            var hasOnlyManagedKeys = existingSecret.Data?.All(kvp => managedKeys.Contains(kvp.Key)) == true;

            if (hasOnlyManagedKeys)
            {
                _logger.LogDebug("Secret {SecretName} in namespace {Namespace} has only managed keys, will delete entire secret", secretName, namespaceName);
                return null; // Signal to caller that entire secret should be deleted
            }

            // Remove managed keys, preserve external keys
            var updatedData = new Dictionary<string, byte[]>();
            var keysRemoved = 0;

            if (existingSecret.Data != null)
            {
                foreach (var kvp in existingSecret.Data)
                {
                    if (!managedKeys.Contains(kvp.Key))
                    {
                        // Keep external keys
                        updatedData[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        keysRemoved++;
                    }
                }
            }

            if (keysRemoved == 0)
            {
                _logger.LogDebug("No managed keys found in secret {SecretName} data", secretName);
                return false;
            }

            // Remove the managed-keys annotation since we're removing all managed keys
            var updatedAnnotations = new Dictionary<string, string>();
            if (existingSecret.Metadata?.Annotations != null)
            {
                foreach (var kvp in existingSecret.Metadata.Annotations)
                {
                    if (kvp.Key != Constants.Kubernetes.ManagedKeysAnnotationKey)
                    {
                        updatedAnnotations[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Preserve existing labels
            var updatedLabels = new Dictionary<string, string>();
            if (existingSecret.Metadata?.Labels != null)
            {
                foreach (var kvp in existingSecret.Metadata.Labels)
                {
                    // Remove management labels since we're no longer managing any keys
                    if (kvp.Key != Constants.Kubernetes.ManagedByLabel && kvp.Key != Constants.Kubernetes.CreatedByLabel)
                    {
                        updatedLabels[kvp.Key] = kvp.Value;
                    }
                }
            }

            var metadata = new V1ObjectMeta
            {
                Name = secretName,
                NamespaceProperty = namespaceName,
                Labels = updatedLabels,
                Annotations = updatedAnnotations
            };

            var updatedSecret = new V1Secret
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = metadata,
                Type = existingSecret.Type ?? Constants.Kubernetes.SecretType,
                Data = updatedData
            };

            await _client.CoreV1.ReplaceNamespacedSecretAsync(updatedSecret, secretName, namespaceName);
            _logger.LogInformation("Removed {KeysRemoved} managed keys from secret {SecretName} in namespace {Namespace}, preserving {ExternalKeysCount} external keys", 
                secretName, namespaceName, keysRemoved, updatedData.Count);
            return true;
        }
        catch (k8s.Autorest.HttpOperationException httpEx) when (httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Cannot remove managed keys from secret {SecretName} - secret does not exist in namespace {Namespace}", secretName, namespaceName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove managed keys from secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            throw;
        }
    }

    /// <summary>
    /// Checks if a secret has only managed keys (no external keys)
    /// </summary>
    public async Task<bool> HasOnlyManagedKeysAsync(string namespaceName, string secretName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secret = await _client.CoreV1.ReadNamespacedSecretAsync(secretName, namespaceName);
            
            // Get managed keys from annotation
            string? managedKeysJson = null;
            secret.Metadata?.Annotations?.TryGetValue(Constants.Kubernetes.ManagedKeysAnnotationKey, out managedKeysJson);
            var managedKeys = ParseManagedKeysAnnotation(managedKeysJson, _logger);
            
            if (!managedKeys.Any() || secret.Data == null)
            {
                return false;
            }

            // Use HashSet for O(1) lookup instead of O(n) List.Contains
            var managedKeysSet = new HashSet<string>(managedKeys, StringComparer.OrdinalIgnoreCase);

            // Check if all keys in the secret are managed keys
            return secret.Data.All(kvp => managedKeysSet.Contains(kvp.Key));
        }
        catch (k8s.Autorest.HttpOperationException httpEx) when (httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if secret {SecretName} has only managed keys in namespace {Namespace}", secretName, namespaceName);
            return false;
        }
    }

    /// <summary>
    /// Gets all secrets that have managed keys (either created by sync service or have managed-keys annotation)
    /// </summary>
    public async Task<List<string>> GetSecretsWithManagedKeysAsync(string namespaceName)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secrets = await _client.CoreV1.ListNamespacedSecretAsync(namespaceName);
            var secretsWithManagedKeys = new List<string>();
            
            foreach (var secret in secrets.Items)
            {
                var hasManagedKeys = false;
                
                // Check if created by sync service
                if (secret.Metadata?.Labels != null)
                {
                    if (secret.Metadata.Labels.ContainsKey(Constants.Kubernetes.CreatedByLabel) &&
                        secret.Metadata.Labels[Constants.Kubernetes.CreatedByLabel] == Constants.Kubernetes.SyncServiceValue)
                    {
                        hasManagedKeys = true;
                    }
                }
                
                // Check if has managed-keys annotation
                if (!hasManagedKeys && secret.Metadata?.Annotations != null)
                {
                    if (secret.Metadata.Annotations.ContainsKey(Constants.Kubernetes.ManagedKeysAnnotationKey))
                    {
                        var managedKeysJson = secret.Metadata.Annotations[Constants.Kubernetes.ManagedKeysAnnotationKey];
                        var managedKeys = ParseManagedKeysAnnotation(managedKeysJson, _logger);
                        hasManagedKeys = managedKeys.Any();
                    }
                }
                
                if (hasManagedKeys)
                {
                    secretsWithManagedKeys.Add(secret.Metadata.Name);
                }
            }
            
            return secretsWithManagedKeys;
        }
        catch (k8s.Autorest.HttpOperationException httpEx) when (httpEx.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var errorMessage = ParseKubernetesErrorMessage(httpEx.Response?.Content);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                _logger.LogWarning("Cannot list secrets with managed keys: {ErrorMessage}", errorMessage);
            }
            else
            {
                _logger.LogWarning("Cannot list secrets with managed keys - namespace {Namespace} does not exist", namespaceName);
            }
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secrets with managed keys in namespace {Namespace}", namespaceName);
            return new List<string>();
        }
    }

    /// <summary>
    /// Serializes a list of key names to JSON for the managed-keys annotation
    /// </summary>
    internal static string SerializeManagedKeysAnnotation(IEnumerable<string> keyNames)
    {
        return System.Text.Json.JsonSerializer.Serialize(keyNames.OrderBy(k => k).ToList());
    }
} 