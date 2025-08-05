using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public class KubernetesService : IKubernetesService
{
    private readonly ILogger<KubernetesService> _logger;
    private readonly KubernetesConfig _config;
    private IKubernetes? _client;

    public KubernetesService(ILogger<KubernetesService> logger, KubernetesConfig config)
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
} 