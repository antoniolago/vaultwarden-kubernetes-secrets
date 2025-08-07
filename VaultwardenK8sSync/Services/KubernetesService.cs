using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;

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
        try
        {
            _logger.LogInformation("Initializing Kubernetes client...");

            if (_config.InCluster)
            {
                _client = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
                _logger.LogInformation("Using in-cluster configuration");
            }
            else
            {
                var config = !string.IsNullOrEmpty(_config.KubeConfigPath)
                    ? KubernetesClientConfiguration.BuildConfigFromConfigFile(_config.KubeConfigPath, _config.Context)
                    : KubernetesClientConfiguration.BuildDefaultConfig();

                _client = new Kubernetes(config);
                _logger.LogInformation("Using kubeconfig configuration");
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
                // Check if the secret has our management labels
                if (secret.Metadata?.Labels != null)
                {
                    if (secret.Metadata.Labels.ContainsKey("app.kubernetes.io/managed-by") &&
                        secret.Metadata.Labels["app.kubernetes.io/managed-by"] == "vaultwarden-k8s-sync")
                    {
                        managedSecrets.Add(secret.Metadata.Name);
                    }
                }
            }
            
            return managedSecrets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get managed secrets in namespace {Namespace}", namespaceName);
            return new List<string>();
        }
    }

    public async Task<bool> CreateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secret = new V1Secret
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = new V1ObjectMeta
                {
                    Name = secretName,
                    NamespaceProperty = namespaceName,
                    Labels = new Dictionary<string, string>
                    {
                        { "app.kubernetes.io/managed-by", "vaultwarden-k8s-sync" },
                        { "app.kubernetes.io/created-by", "vaultwarden-k8s-sync" }
                    }
                },
                Type = "Opaque",
                Data = data.ToDictionary(kvp => kvp.Key, kvp => System.Text.Encoding.UTF8.GetBytes(kvp.Value))
            };

            await _client.CoreV1.CreateNamespacedSecretAsync(secret, namespaceName);
            _logger.LogInformation("Created secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return false;
        }
    }

    public async Task<bool> UpdateSecretAsync(string namespaceName, string secretName, Dictionary<string, string> data)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Kubernetes client not initialized. Call InitializeAsync first.");
        }

        try
        {
            var secret = new V1Secret
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Metadata = new V1ObjectMeta
                {
                    Name = secretName,
                    NamespaceProperty = namespaceName,
                    Labels = new Dictionary<string, string>
                    {
                        { "app.kubernetes.io/managed-by", "vaultwarden-k8s-sync" },
                        { "app.kubernetes.io/created-by", "vaultwarden-k8s-sync" }
                    }
                },
                Type = "Opaque",
                Data = data.ToDictionary(kvp => kvp.Key, kvp => System.Text.Encoding.UTF8.GetBytes(kvp.Value))
            };

            await _client.CoreV1.ReplaceNamespacedSecretAsync(secret, secretName, namespaceName);
            _logger.LogInformation("Updated secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return false;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret data for {SecretName} in namespace {Namespace}", secretName, namespaceName);
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
} 