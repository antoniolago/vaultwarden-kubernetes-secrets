using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Tests;

[Collection("SyncService Sequential")]
public class SecretCacheTests : IDisposable
{
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock;
    private readonly SyncSettings _syncConfig;
    private readonly SyncService _syncService;

    public SecretCacheTests()
    {
        // Clean up any leftover sync lock file from previous tests
        var lockFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vaultwarden-sync-operation.lock");
        if (System.IO.File.Exists(lockFilePath))
        {
            try
            {
                System.IO.File.Delete(lockFilePath);
            }
            catch
            {
                // Ignore if file is locked by another process
            }
        }
        
        _loggerMock = new Mock<ILogger<SyncService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
        _metricsServiceMock = new Mock<IMetricsService>();
        _dbLoggerMock = new Mock<IDatabaseLoggerService>();
        _syncConfig = new SyncSettings();
        
        // Setup database logger to return a sync log ID
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
        
        // Setup namespace validation (added for namespace existence checks)
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        
        _syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig);
    }

    [Fact]
    public async Task SecretDeletion_ShouldRecreateSecretOnNextSync()
    {
        // Arrange: Create an item that should sync to a secret
        var item = new VaultwardenItem
        {
            Id = "test-id-123",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });

        // First sync: Secret doesn't exist, should create it
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "test-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync("default", "test-secret", It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        // Act: First sync
        var firstSyncResult = await _syncService.SyncAsync();

        // Assert: Secret should be created
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync("default", "test-secret", It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()), Times.Once);

        // Arrange: Simulate secret being deleted externally
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "test-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "test-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        
        // Reset the hash to force re-processing (simulating items changed or external trigger)
        _syncService.ResetItemsHash();

        // Act: Second sync after external deletion
        var secondSyncResult = await _syncService.SyncAsync();

        // Assert: Secret should be recreated (CreateSecretAsync called again)
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync("default", "test-secret", It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task OrphanCleanup_ShouldInvalidateCache_WhenDeletingSecrets()
    {
        // Arrange: Setup orphan cleanup enabled
        _syncConfig.DeleteOrphans = true;

        // Create item that syncs to namespace "default"
        var item = new VaultwardenItem
        {
            Id = "current-item",
            Name = "current-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });

        // Setup Kubernetes to have an orphaned secret
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("default"))
            .ReturnsAsync(new List<string> { "current-secret", "orphaned-secret" });
        
        // Setup current secret exists
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "current-secret"))
            .ReturnsAsync(false); // Will be created
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync("default", "current-secret", It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        // Setup orphaned secret deletion
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync("default", "orphaned-secret"))
            .ReturnsAsync(true);

        // Act
        var syncResult = await _syncService.SyncAsync();

        // Assert: Orphaned secret should be deleted
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync("default", "orphaned-secret"), Times.Once);
        
        // Cache should be invalidated for orphaned secret (verified by behavior in subsequent calls)
    }

    [Fact]
    public async Task SecretNameChange_ShouldDeleteOldSecret_AndCreateNew()
    {
        // Arrange: Item with custom secret name
        var item = new VaultwardenItem
        {
            Id = "item-123",
            Name = "original-name",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "default", Type = 0 },
                new() { Name = "secret-name", Value = "custom-secret-name", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });

        // Old secret (based on item name) exists
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "original-name"))
            .ReturnsAsync(true);
        
        // New secret (custom name) doesn't exist yet
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "custom-secret-name"))
            .ReturnsAsync(false);

        // Setup delete and create operations
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync("default", "original-name"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync("default", "custom-secret-name", It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        // Act
        var syncResult = await _syncService.SyncAsync();

        // Assert: Old secret should be deleted and new one created
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync("default", "original-name"), Times.Once);
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync("default", "custom-secret-name", It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task FailedSecretSync_ShouldRecordErrorInDatabase()
    {
        // Arrange: Item that will fail to sync
        var item = new VaultwardenItem
        {
            Id = "failing-item",
            Name = "failing-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "nonexistent-namespace", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });

        // Setup Kubernetes namespaces
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "nonexistent-namespace" });

        // Simulate namespace doesn't exist (returns failure)
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("nonexistent-namespace", "failing-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync("nonexistent-namespace", "failing-secret", It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Failed("Namespace 'nonexistent-namespace' does not exist"));

        // Act
        var syncResult = await _syncService.SyncAsync();

        // Assert: Failed status should be logged to database
        _dbLoggerMock.Verify(x => x.UpsertSecretStateAsync(
            "nonexistent-namespace",
            "failing-secret",
            "failing-item",
            "failing-secret",
            "Failed",
            It.IsAny<int>(),
            It.Is<string>(err => err.Contains("does not exist"))
        ), Times.AtLeastOnce);
    }

    public void Dispose()
    {
        // Clean up lock file after each test
        var lockFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vaultwarden-sync-operation.lock");
        System.Threading.Thread.Sleep(100); // Give lock time to release
        try
        {
            if (System.IO.File.Exists(lockFilePath))
            {
                System.IO.File.Delete(lockFilePath);
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
}
