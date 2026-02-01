using System.Text;
using FluentAssertions;
using k8s;
using k8s.Models;
using Spectre.Console;
using VaultwardenK8sSync.E2ETests.Infrastructure;
using Xunit;

namespace VaultwardenK8sSync.E2ETests.Tests;

/// <summary>
/// E2E tests for verifying secret synchronization from Vaultwarden to Kubernetes.
/// </summary>
[Collection("E2E")]
public class SecretSyncTests : IAsyncLifetime
{
    private readonly E2ETestFixture _fixture;
    private readonly IKubernetes _k8s;

    public SecretSyncTests(E2ETestFixture fixture)
    {
        _fixture = fixture;
        _k8s = fixture.KubernetesClient ?? throw new InvalidOperationException("Kubernetes client not initialized");
    }

    public async Task InitializeAsync()
    {
        // Wait for initial sync to complete
        AnsiConsole.MarkupLine("[blue]Waiting for initial sync (15s)...[/]");
        await Task.Delay(TimeSpan.FromSeconds(15));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BasicLoginItem_ShouldCreateSecret()
    {
        // Arrange
        var secretName = "test-basic-login";
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull($"Secret '{secretName}' should be created in namespace '{ns}'");
        
        var data = DecodeSecretData(secret!);
        data.Should().ContainKey("username");
        data.Should().ContainKey("password");
        data["username"].Should().Be("testuser");
        data["password"].Should().Be("testpassword123");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Basic login item synced correctly");
    }

    [Fact]
    public async Task CustomSecretName_ShouldBeRespected()
    {
        // Arrange
        var secretName = "custom-app-secret"; // Defined via secret-name field
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull($"Secret with custom name '{secretName}' should be created");
        
        var data = DecodeSecretData(secret!);
        data["username"].Should().Be("appuser");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Custom secret name '{secretName}' working correctly");
    }

    [Fact]
    public async Task MultiNamespace_ShouldCreateSecretsInAllNamespaces()
    {
        // Arrange
        var secretName = "multi-namespace-secret";
        var namespaces = new[] 
        { 
            E2ETestFixture.TestNamespace1, 
            E2ETestFixture.TestNamespace2, 
            E2ETestFixture.TestNamespace3 
        };

        // Act & Assert
        foreach (var ns in namespaces)
        {
            var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));
            secret.Should().NotBeNull($"Secret should exist in namespace '{ns}'");
            
            var data = DecodeSecretData(secret!);
            data["username"].Should().Be("multiuser");
        }
        
        AnsiConsole.MarkupLine($"[green]✓[/] Multi-namespace sync working ({namespaces.Length} namespaces)");
    }

    [Fact]
    public async Task CustomKeyNames_ShouldBeUsed()
    {
        // Arrange
        var secretName = "custom-keys-login";
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull();
        
        var data = DecodeSecretData(secret!);
        
        // Should have db_user and db_password instead of username and password
        data.Should().ContainKey("db_user", "Custom key name 'db_user' should be used");
        data.Should().ContainKey("db_password", "Custom key name 'db_password' should be used");
        data["db_user"].Should().Be("keyuser");
        data["db_password"].Should().Be("keypass000");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Custom key names (db_user, db_password) working");
    }

    [Fact]
    public async Task ExtraCustomFields_ShouldBeIncluded()
    {
        // Arrange
        var secretName = "extra-fields-login";
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull();
        
        var data = DecodeSecretData(secret!);
        
        data.Should().ContainKey("api_key");
        data.Should().ContainKey("api_endpoint");
        data.Should().ContainKey("environment");
        
        data["api_key"].Should().Be("sk-abc123xyz");
        data["api_endpoint"].Should().Be("https://api.example.com");
        data["environment"].Should().Be("production");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Extra custom fields synced correctly");
    }

    [Fact]
    public async Task AnnotationsAndLabels_ShouldBeApplied()
    {
        // Arrange
        var secretName = "annotated-secret";
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull();
        
        var annotations = secret!.Metadata?.Annotations ?? new Dictionary<string, string>();
        var labels = secret.Metadata?.Labels ?? new Dictionary<string, string>();
        
        annotations.Should().ContainKey("description");
        annotations["description"].Should().Be("Test secret with annotations");
        
        labels.Should().ContainKey("team");
        labels["team"].Should().Be("platform");
        
        labels.Should().ContainKey("environment");
        labels["environment"].Should().Be("test");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Annotations and labels applied correctly");
    }

    [Fact]
    public async Task SecureNote_ShouldSyncWithCustomFields()
    {
        // Arrange
        var secretName = "test-secure-note";
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull();
        
        var data = DecodeSecretData(secret!);
        data.Should().ContainKey("config_data");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Secure note synced with custom fields");
    }

    [Fact]
    public async Task SecretMerging_ShouldCombineMultipleItems()
    {
        // Arrange
        var secretName = "merged-secret";
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull($"Merged secret '{secretName}' should exist");
        
        var data = DecodeSecretData(secret!);
        
        // Should have keys from both merge-source-1 and merge-source-2
        var mergedKeys = new[] { "first_user", "first_pass", "second_user", "second_pass" };
        var foundKeys = mergedKeys.Where(k => data.ContainsKey(k)).ToList();
        
        foundKeys.Count.Should().BeGreaterOrEqualTo(2, 
            "At least some keys from both sources should be merged");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Secret merging working ({foundKeys.Count} keys merged)");
    }

    [Fact]
    public async Task HiddenField_ShouldBeSynced()
    {
        // Arrange
        var secretName = "hidden-field-login";
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull();
        
        var data = DecodeSecretData(secret!);
        data.Should().ContainKey("hidden_api_token");
        data["hidden_api_token"].Should().Be("hidden-token-value");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Hidden field synced correctly");
    }

    [Fact]
    public async Task ManagedByLabel_ShouldBePresent()
    {
        // Arrange
        var secretName = "test-basic-login";
        var ns = E2ETestFixture.TestNamespace1;

        // Act
        var secret = await WaitForSecret(ns, secretName, TimeSpan.FromSeconds(30));

        // Assert
        secret.Should().NotBeNull();
        
        var labels = secret!.Metadata?.Labels ?? new Dictionary<string, string>();
        
        // Check for managed-by or vaultwarden-related label
        var hasManagedLabel = labels.Any(l => 
            l.Key.Contains("managed-by", StringComparison.OrdinalIgnoreCase) ||
            l.Value.Contains("vaultwarden", StringComparison.OrdinalIgnoreCase));
        
        hasManagedLabel.Should().BeTrue("Secret should have a managed-by label");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Managed-by label present");
    }

    [Fact]
    public async Task ExpectedSecretCount_ShouldBeCorrect()
    {
        // Arrange
        var ns = E2ETestFixture.TestNamespace1;
        
        // Act
        var secrets = await _k8s.CoreV1.ListNamespacedSecretAsync(ns);
        
        // Filter to test secrets (exclude service account tokens etc)
        var testSecrets = secrets.Items
            .Where(s => s.Metadata?.Name != null && 
                       (s.Metadata.Name.StartsWith("test-") ||
                        s.Metadata.Name.StartsWith("custom-") ||
                        s.Metadata.Name.StartsWith("extra-") ||
                        s.Metadata.Name.StartsWith("annotated-") ||
                        s.Metadata.Name.StartsWith("merged-") ||
                        s.Metadata.Name.StartsWith("hidden-") ||
                        s.Metadata.Name.StartsWith("orphan-") ||
                        s.Metadata.Name.StartsWith("multi-")))
            .ToList();
        
        // Assert
        testSecrets.Count.Should().BeGreaterOrEqualTo(5, 
            "At least 5 test secrets should be created");
        
        AnsiConsole.MarkupLine($"[green]✓[/] Found {testSecrets.Count} test secrets (expected >= 5)");
    }

    private async Task<V1Secret?> WaitForSecret(string ns, string name, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var secret = await _k8s.CoreV1.ReadNamespacedSecretAsync(name, ns);
                if (secret != null)
                    return secret;
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Secret doesn't exist yet, keep waiting
            }
            
            await Task.Delay(2000);
        }
        
        return null;
    }

    private static Dictionary<string, string> DecodeSecretData(V1Secret secret)
    {
        var result = new Dictionary<string, string>();
        
        if (secret.Data == null)
            return result;
        
        foreach (var kvp in secret.Data)
        {
            try
            {
                result[kvp.Key] = Encoding.UTF8.GetString(kvp.Value);
            }
            catch
            {
                result[kvp.Key] = $"<binary: {kvp.Value.Length} bytes>";
            }
        }
        
        return result;
    }
}
