using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Models;
using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VaultwardenK8sSync.Tests;

public class OrphanCleanupUnitTests
{
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerServiceMock;
    private readonly SyncSettings _syncConfig;
    private readonly SyncService _syncService;

    public OrphanCleanupUnitTests()
    {
        _loggerMock = new Mock<ILogger<SyncService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
        _metricsServiceMock = new Mock<IMetricsService>();
        _dbLoggerServiceMock = new Mock<IDatabaseLoggerService>();
        _syncConfig = new SyncSettings { DeleteOrphans = true };
        
        // Default mocks for the orphan cleanup flow
        // 1. GetItemsAsync - returns empty list (no current items, so all secrets are orphans)
        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());
        
        // 2. GetAllNamespacesAsync - returns test namespace
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "test-namespace" });
        
        // 3. GetManagedSecretNamesAsync - returns secrets so namespace is included in cleanup
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("test-namespace"))
            .ReturnsAsync(new List<string> { "some-secret" });
        
        // 4. Default GetSecretsWithManagedKeysAsync - empty list (tests will override)
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("test-namespace"))
            .ReturnsAsync(new List<string>());
        
        _syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerServiceMock.Object,
            _syncConfig);
    }

    [Fact]
    public async Task CleanupOrphanedSecretsAsync_WithMixedOwnershipSecret_ShouldCallRemoveManagedKeys()
    {
        // Arrange
        var namespaceName = "test-namespace";
        
        // Override default mock to return our test secret (orphaned)
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(namespaceName))
            .ReturnsAsync(new List<string> { "mixed-secret" });
        
        // Mock RemoveManagedKeysAsync to return true (keys removed successfully)
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync(namespaceName, "mixed-secret"))
            .ReturnsAsync(true);
        
        _dbLoggerServiceMock.Setup(x => x.UpsertSecretStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        // Act
        var result = await _syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();
        
        // Verify the correct method was called
        _kubernetesServiceMock.Verify(x => x.GetSecretsWithManagedKeysAsync(namespaceName), Times.Once);
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(namespaceName, "mixed-secret"), Times.Once);
        
        // Verify database was updated with correct status
        _dbLoggerServiceMock.Verify(x => x.UpsertSecretStateAsync(
            namespaceName, "mixed-secret", It.IsAny<string>(), It.IsAny<string>(), 
            SecretStatusConstants.Active, 0, "Managed keys removed - external keys preserved"), Times.Once);
        
        // Verify DeleteSecretAsync was NOT called
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedSecretsAsync_WithFullyManagedSecret_ShouldDeleteSecret()
    {
        // Arrange
        var namespaceName = "test-namespace";
        
        // Override default mock to return our test secret (orphaned)
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(namespaceName))
            .ReturnsAsync(new List<string> { "managed-only-secret" });
        
        // Mock RemoveManagedKeysAsync to return null (signal to delete entire secret)
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync(namespaceName, "managed-only-secret"))
            .ReturnsAsync((bool?)null);
        
        // Mock DeleteSecretAsync to return true (deletion successful)
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync(namespaceName, "managed-only-secret"))
            .ReturnsAsync(true);
        
        _dbLoggerServiceMock.Setup(x => x.UpsertSecretStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        // Act
        var result = await _syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();
        
        // Verify the correct methods were called in order
        _kubernetesServiceMock.Verify(x => x.GetSecretsWithManagedKeysAsync(namespaceName), Times.Once);
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(namespaceName, "managed-only-secret"), Times.Once);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(namespaceName, "managed-only-secret"), Times.Once);
        
        // Verify database was updated with deleted status
        _dbLoggerServiceMock.Verify(x => x.UpsertSecretStateAsync(
            namespaceName, "managed-only-secret", It.IsAny<string>(), It.IsAny<string>(), 
            SecretStatusConstants.Deleted, 0, "Secret removed - no longer configured in Vaultwarden"), Times.Once);
    }

    [Fact]
    public async Task CleanupOrphanedSecretsAsync_WithDryRun_ShouldNotModifySecrets()
    {
        // Arrange
        var syncConfig = new SyncSettings { DeleteOrphans = true, DryRun = true };
        var syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerServiceMock.Object,
            syncConfig);
        
        var namespaceName = "test-namespace";
        
        // Override to return a secret that would be orphaned
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });

        // Act
        var result = await syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();
        
        // Verify no actual modifications were made (dry run mode)
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _dbLoggerServiceMock.Verify(x => x.UpsertSecretStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedSecretsAsync_WithDisabledOrphanCleanup_ShouldSkip()
    {
        // Arrange
        var syncConfig = new SyncSettings { DeleteOrphans = false };
        var syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerServiceMock.Object,
            syncConfig);

        // Act
        var result = await syncService.CleanupOrphanedSecretsAsync();

        // Assert - when DeleteOrphans=false, the cleanup should still return true (success) but not do any work
        // Note: The current implementation still fetches items but doesn't process orphans when disabled
        result.Should().BeTrue();
        
        // Verify no orphan cleanup operations were performed
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedSecretsAsync_WithMultipleSecrets_ShouldProcessAll()
    {
        // Arrange
        var namespaceName = "test-namespace";
        var secrets = new[] { "secret-1", "secret-2", "secret-3" };
        
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(namespaceName))
            .ReturnsAsync(secrets.ToList());
        
        // Setup different behaviors for each secret
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync(namespaceName, "secret-1"))
            .ReturnsAsync(true); // Keys removed
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync(namespaceName, "secret-2"))
            .ReturnsAsync((bool?)null); // Should delete entire secret
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync(namespaceName, "secret-3"))
            .ReturnsAsync(false); // No managed keys
        
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync(namespaceName, "secret-2"))
            .ReturnsAsync(true);
        
        _dbLoggerServiceMock.Setup(x => x.UpsertSecretStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        // Act
        var result = await _syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();
        
        // Verify all secrets were processed
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(namespaceName, "secret-1"), Times.Once);
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(namespaceName, "secret-2"), Times.Once);
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(namespaceName, "secret-3"), Times.Once);
        
        // Verify only secret-2 was deleted
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(namespaceName, "secret-2"), Times.Once);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(namespaceName, "secret-1"), Times.Never);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(namespaceName, "secret-3"), Times.Never);
        
        // Verify database updates for processed secrets
        _dbLoggerServiceMock.Verify(x => x.UpsertSecretStateAsync(
            namespaceName, "secret-1", It.IsAny<string>(), It.IsAny<string>(), 
            SecretStatusConstants.Active, 0, It.IsAny<string>()), Times.Once);
        
        _dbLoggerServiceMock.Verify(x => x.UpsertSecretStateAsync(
            namespaceName, "secret-2", It.IsAny<string>(), It.IsAny<string>(), 
            SecretStatusConstants.Deleted, 0, It.IsAny<string>()), Times.Once);
        
        // secret-3 should not have database update (no changes)
        _dbLoggerServiceMock.Verify(x => x.UpsertSecretStateAsync(
            namespaceName, "secret-3", It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedSecretsAsync_WithNoManagedSecrets_ShouldReturnEarly()
    {
        // Arrange
        var namespaceName = "test-namespace";
        
        // Override default mock to return empty list (no managed secrets)
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(namespaceName))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _syncService.CleanupOrphanedSecretsAsync();

        // Assert
        result.Should().BeTrue();
        
        // Verify no further processing was attempted
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _dbLoggerServiceMock.Verify(x => x.UpsertSecretStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }
}
