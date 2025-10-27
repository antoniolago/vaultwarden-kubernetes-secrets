using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Configuration;

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

            if (_config.InCluster)
            {
                _client = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
                _logger.LogDebug("Using in-cluster configuration");
            }
            else
            {
                var config = !string.IsNullOrEmpty(_config.KubeConfigPath)
                    ? KubernetesClientConfiguration.BuildConfigFromConfigFile(_config.KubeConfigPath, _config.Context)
                    : KubernetesClientConfiguration.BuildDefaultConfig();

                _client = new Kubernetes(config);
                _logger.LogDebug("Using kubeconfig configuration");
            }

            // Test the connection
            var version = await _client.Version.GetCodeAsync();
            _logger.LogInformation("Connected to Kubernetes API version: {Version}", version.GitVersion);

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

    public async Task<OperationResult> CreateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data, Dictionary<string, string>? annotations = null)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var metadata = new V1ObjectMeta
            {
                Name = secretName,
                NamespaceProperty = namespaceName,
                Labels = new Dictionary<string, string>
                {
                    { Constants.Kubernetes.ManagedByLabel, Constants.Kubernetes.ManagedByValue },
                    { Constants.Kubernetes.CreatedByLabel, Constants.Kubernetes.SyncServiceValue }
                }
            };

            // Add annotations if provided
            if (annotations != null && annotations.Any())
            {
                metadata.Annotations = new Dictionary<string, string>(annotations);
            }

            var secret = new V1Secret
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = metadata,
                Type = Constants.Kubernetes.SecretType,
                Data = data.ToDictionary(kvp => kvp.Key, kvp => System.Text.Encoding.UTF8.GetBytes(kvp.Value))
            };

            await _client.CoreV1.CreateNamespacedSecretAsync(secret, namespaceName);
            _logger.LogInformation("Created secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
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

    public async Task<OperationResult> UpdateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data, Dictionary<string, string>? annotations = null)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var metadata = new V1ObjectMeta
            {
                Name = secretName,
                NamespaceProperty = namespaceName,
                Labels = new Dictionary<string, string>
                {
                    { Constants.Kubernetes.ManagedByLabel, Constants.Kubernetes.ManagedByValue },
                    { Constants.Kubernetes.CreatedByLabel, Constants.Kubernetes.SyncServiceValue }
                }
            };

            // Add annotations if provided
            if (annotations != null && annotations.Any())
            {
                metadata.Annotations = new Dictionary<string, string>(annotations);
            }

            var secret = new V1Secret
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = metadata,
                Type = Constants.Kubernetes.SecretType,
                Data = data.ToDictionary(kvp => kvp.Key, kvp => System.Text.Encoding.UTF8.GetBytes(kvp.Value))
            };

            await _client.CoreV1.ReplaceNamespacedSecretAsync(secret, secretName, namespaceName);
            _logger.LogInformation("Updated secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
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
} 