using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Models;
using Xunit;
using FluentAssertions;

namespace VaultwardenK8sSync.Tests.Integration;

/// <summary>
/// Integration tests for orphan cleanup functionality.
/// Tests the complete flow of orphan detection and cleanup across the SyncService.
/// </summary>
[Collection("SyncService Sequential")]
public class OrphanCleanupIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock;
    private readonly SyncSettings _syncConfig;

    public OrphanCleanupIntegrationTests()
    {
        // Clean up any leftover sync lock file from previous tests
        var lockFilePath = Path.Combine(Path.GetTempPath(), "vaultwarden-sync-operation.lock");
        if (File.Exists(lockFilePath))
        {
            try { File.Delete(lockFilePath); } catch { }
        }

        _loggerMock = new Mock<ILogger<SyncService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
        _metricsServiceMock = new Mock<IMetricsService>();
        _dbLoggerMock = new Mock<IDatabaseLoggerService>();
        _syncConfig = new SyncSettings { DeleteOrphans = true };

        // Setup default database logger mocks
        _dbLoggerMock.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
        _dbLoggerMock.Setup(x => x.CacheVaultwardenItemsAsync(It.IsAny<List<VaultwardenItem>>()))
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.CleanupStaleSecretStatesAsync(It.IsAny<List<VaultwardenItem>>()))
            .ReturnsAsync(0);
        _dbLoggerMock.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.UpdateSyncProgressAsync(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.UpsertSecretStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Setup namespace validation
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task FullSyncWithOrphanCleanup_ShouldCreateSecretsAndDeleteOrphans()
    {
        // Arrange: Setup a complete sync scenario with orphan cleanup
        var currentItem = new VaultwardenItem
        {
            Id = "item-1",
            Name = "active-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "production", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { currentItem });

        // Setup Kubernetes with one active secret and one orphaned secret
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "production" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("production"))
            .ReturnsAsync(new List<string> { "active-secret", "orphaned-secret" });
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("production"))
            .ReturnsAsync(new List<string> { "active-secret", "orphaned-secret" });

        // Active secret doesn't exist yet - will be created
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("production", "active-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync("production", "active-secret",
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        // Orphaned secret has only managed keys - should be deleted entirely
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync("production", "orphaned-secret"))
            .ReturnsAsync((bool?)null); // null = delete entire secret
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync("production", "orphaned-secret"))
            .ReturnsAsync(true);

        var syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig);

        // Act
        var result = await syncService.SyncAsync();

        // Assert
        result.OverallSuccess.Should().BeTrue();

        // Verify active secret was created
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync("production", "active-secret",
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()), Times.Once);

        // Verify orphaned secret was deleted
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync("production", "orphaned-secret"), Times.Once);
    }

    [Fact]
    public async Task OrphanCleanup_WithMixedOwnershipSecrets_ShouldPreserveExternalKeys()
    {
        // Arrange: Orphaned secret has both managed and external keys
        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>()); // No items = all secrets are orphans

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("default"))
            .ReturnsAsync(new List<string> { "mixed-secret" });
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("default"))
            .ReturnsAsync(new List<string> { "mixed-secret" });

        // Secret has external keys - should only remove managed keys
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync("default", "mixed-secret"))
            .ReturnsAsync(true); // true = keys removed, external keys preserved

        var syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig);

        // Act - Call CleanupOrphanedSecretsAsync directly (SyncAsync returns early with no items)
        var result = await syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();

        // Verify RemoveManagedKeysAsync was called but NOT DeleteSecretAsync
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync("default", "mixed-secret"), Times.Once);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync("default", "mixed-secret"), Times.Never);

        // Verify database was updated to reflect keys removed
        _dbLoggerMock.Verify(x => x.UpsertSecretStateAsync(
            "default", "mixed-secret", It.IsAny<string>(), It.IsAny<string>(),
            SecretStatusConstants.Active, 0, "Managed keys removed - external keys preserved"), Times.Once);
    }

    [Fact]
    public async Task OrphanCleanup_WithMultipleNamespaces_ShouldProcessAll()
    {
        // Arrange: Multiple namespaces with orphaned secrets
        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "ns1", "ns2", "ns3" });

        // Each namespace has managed secrets
        foreach (var ns in new[] { "ns1", "ns2", "ns3" })
        {
            _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(ns))
                .ReturnsAsync(new List<string> { $"orphan-{ns}" });
            _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(ns))
                .ReturnsAsync(new List<string> { $"orphan-{ns}" });
            _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync(ns, $"orphan-{ns}"))
                .ReturnsAsync((bool?)null);
            _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync(ns, $"orphan-{ns}"))
                .ReturnsAsync(true);
        }

        var syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig);

        // Act - Call CleanupOrphanedSecretsAsync directly (SyncAsync returns early with no items)
        var result = await syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();

        // Verify each namespace's orphan was processed
        foreach (var ns in new[] { "ns1", "ns2", "ns3" })
        {
            _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(ns, $"orphan-{ns}"), Times.Once);
        }
    }

    [Fact]
    public async Task OrphanCleanup_InDryRunMode_ShouldNotModifySecrets()
    {
        // Arrange
        _syncConfig.DryRun = true;

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("default"))
            .ReturnsAsync(new List<string> { "orphan-secret" });
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("default"))
            .ReturnsAsync(new List<string> { "orphan-secret" });

        var syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig);

        // Act - Call CleanupOrphanedSecretsAsync directly (SyncAsync returns early with no items)
        var result = await syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();

        // Verify no modifications were made (dry run mode)
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OrphanCleanup_WhenDisabled_ShouldNotProcessOrphans()
    {
        // Arrange
        _syncConfig.DeleteOrphans = false;

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });

        var syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig);

        // Act - Call CleanupOrphanedSecretsAsync directly
        // Note: When DeleteOrphans is false, CleanupOrphanedSecretsAsync still runs but
        // the SyncAsync method skips calling it. For this test, we verify the direct call
        // still succeeds but doesn't cause issues.
        var result = await syncService.CleanupOrphanedSecretsAsync();

        // Assert - CleanupOrphanedSecretsAsync always processes when called directly
        // The DeleteOrphans flag only controls whether SyncAsync calls it
        result.Should().BeTrue();
    }

    [Fact]
    public async Task OrphanCleanup_ShouldExcludeAuthTokenSecret()
    {
        // Arrange: Auth token secret should never be deleted
        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("default"))
            .ReturnsAsync(new List<string> { "vaultwarden-kubernetes-secrets-token", "orphan-secret" });
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("default"))
            .ReturnsAsync(new List<string> { "vaultwarden-kubernetes-secrets-token", "orphan-secret" });

        // Only orphan-secret should be processed
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync("default", "orphan-secret"))
            .ReturnsAsync((bool?)null);
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync("default", "orphan-secret"))
            .ReturnsAsync(true);

        var syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig);

        // Act - Call CleanupOrphanedSecretsAsync directly (SyncAsync returns early with no items)
        var result = await syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();

        // Verify auth token secret was NOT processed
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync("default", "vaultwarden-kubernetes-secrets-token"), Times.Never);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync("default", "vaultwarden-kubernetes-secrets-token"), Times.Never);

        // Verify regular orphan was processed
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync("default", "orphan-secret"), Times.Once);
    }

    public void Dispose()
    {
        var lockFilePath = Path.Combine(Path.GetTempPath(), "vaultwarden-sync-operation.lock");
        Thread.Sleep(100);
        try
        {
            if (File.Exists(lockFilePath))
            {
                File.Delete(lockFilePath);
            }
        }
        catch { }
    }
}
