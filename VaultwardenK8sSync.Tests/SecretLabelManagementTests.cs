using Xunit;
using k8s.Models;
using VaultwardenK8sSync.Configuration;

namespace VaultwardenK8sSync.Tests;

public class SecretLabelManagementTests
{
    [Fact]
    public void SecretLabelLogic_FiltersCorrectly()
    {
        // Arrange - Simulate the logic from GetManagedSecretNamesAsync
        var secrets = new List<V1Secret>
        {
            // Secret created by sync service - should be included
            new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "sync-created-secret",
                    Labels = new Dictionary<string, string>
                    {
                        { Constants.Kubernetes.ManagedByLabel, Constants.Kubernetes.ManagedByValue },
                        { Constants.Kubernetes.CreatedByLabel, Constants.Kubernetes.SyncServiceValue }
                    }
                }
            },
            // Secret created by API - should NOT be included
            new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "api-created-secret",
                    Labels = new Dictionary<string, string>
                    {
                        { Constants.Kubernetes.ManagedByLabel, Constants.Kubernetes.ManagedByValue },
                        { Constants.Kubernetes.CreatedByLabel, "vaultwarden-k8s-api" }
                    }
                }
            },
            // Secret without created-by label - should NOT be included
            new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "legacy-secret",
                    Labels = new Dictionary<string, string>
                    {
                        { Constants.Kubernetes.ManagedByLabel, Constants.Kubernetes.ManagedByValue }
                    }
                }
            },
            // Unrelated secret - should NOT be included
            new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "other-secret",
                    Labels = new Dictionary<string, string>
                    {
                        { "app", "my-app" }
                    }
                }
            }
        };

        // Act - Apply the same logic as GetManagedSecretNamesAsync
        var managedSecrets = new List<string>();
        foreach (var secret in secrets)
        {
            if (secret.Metadata?.Labels != null)
            {
                if (secret.Metadata.Labels.ContainsKey(Constants.Kubernetes.CreatedByLabel) &&
                    secret.Metadata.Labels[Constants.Kubernetes.CreatedByLabel] == Constants.Kubernetes.SyncServiceValue)
                {
                    managedSecrets.Add(secret.Metadata.Name);
                }
            }
        }

        // Assert
        Assert.Single(managedSecrets);
        Assert.Contains("sync-created-secret", managedSecrets);
        Assert.DoesNotContain("api-created-secret", managedSecrets);
        Assert.DoesNotContain("legacy-secret", managedSecrets);
        Assert.DoesNotContain("other-secret", managedSecrets);
    }

    [Fact]
    public void Constants_HasDistinctCreatorValues()
    {
        // Ensure we have distinct values for different creators
        Assert.NotEqual(Constants.Kubernetes.ManagedByValue, Constants.Kubernetes.SyncServiceValue);
        Assert.Equal("vaultwarden-kubernetes-secrets", Constants.Kubernetes.ManagedByValue);
        Assert.Equal("vaultwarden-k8s-sync", Constants.Kubernetes.SyncServiceValue);
    }

    [Fact]
    public void Constants_HasCorrectLabelKeys()
    {
        // Verify label keys follow Kubernetes conventions
        Assert.Equal("app.kubernetes.io/managed-by", Constants.Kubernetes.ManagedByLabel);
        Assert.Equal("app.kubernetes.io/created-by", Constants.Kubernetes.CreatedByLabel);
    }

    [Theory]
    [InlineData("sync-created-secret", true)]
    [InlineData("api-created-secret", false)]
    [InlineData("other-secret", false)]
    public void SecretLabels_DetermineManagement(string secretName, bool shouldBeManaged)
    {
        // This test verifies the logic for determining if a secret should be managed
        var syncCreatedSecret = new Dictionary<string, string>
        {
            { Constants.Kubernetes.CreatedByLabel, Constants.Kubernetes.SyncServiceValue }
        };

        var apiCreatedSecret = new Dictionary<string, string>
        {
            { Constants.Kubernetes.CreatedByLabel, "vaultwarden-k8s-api" }
        };

        var otherSecret = new Dictionary<string, string>
        {
            { "app", "my-app" }
        };

        var labels = secretName switch
        {
            "sync-created-secret" => syncCreatedSecret,
            "api-created-secret" => apiCreatedSecret,
            _ => otherSecret
        };

        var isManagedBySync = labels.ContainsKey(Constants.Kubernetes.CreatedByLabel) &&
                              labels[Constants.Kubernetes.CreatedByLabel] == Constants.Kubernetes.SyncServiceValue;

        Assert.Equal(shouldBeManaged, isManagedBySync);
    }
}
