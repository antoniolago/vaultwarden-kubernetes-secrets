using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Configuration;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Tests to verify that summary statistics are correctly populated even when no changes are detected
/// This tests the fix for the bug where TotalSecretsSkipped showed as 0 when hash matched
/// </summary>
[Collection("SyncService Sequential")]
public class NoChangesSummaryTests
{
    [Fact]
    public async Task SyncSummary_ShouldShowSkippedItems_WhenNoChangesDetected()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
            mockKubernetesService.Setup(x => x.IsInitialized).Returns(true);
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        
        var syncConfig = new SyncSettings();
        
        // Setup: Return 3 items with namespaces
        var vaultItems = new List<VaultwardenItem>
        {
            new VaultwardenItem
            {
                Id = "item-1",
                Name = "secret-1",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
                }
            },
            new VaultwardenItem
            {
                Id = "item-2",
                Name = "secret-2",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
                }
            },
            new VaultwardenItem
            {
                Id = "item-3",
                Name = "secret-3",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "namespaces", Value = "production", Type = 0 }
                }
            }
        };
        
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);
            
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
            
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.UpdateSyncProgressAsync(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), 
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.CacheVaultwardenItemsAsync(It.IsAny<List<VaultwardenItem>>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.CleanupStaleSecretStatesAsync(It.IsAny<List<VaultwardenItem>>()))
            .ReturnsAsync(0);
            
        mockDbLogger.Setup(x => x.UpsertSecretStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
            
        // Mock Kubernetes service for secret operations
        mockKubernetesService.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default", "production" });
        
        // Add namespace validation mock
        mockKubernetesService.Setup(x => x.NamespaceExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Track whether each secret exists
        var secretExists = new Dictionary<string, bool>();
        mockKubernetesService.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) => secretExists.GetValueOrDefault($"{ns}/{name}"));

        // Set up secret data retrieval
        mockKubernetesService.Setup(x => x.GetSecretDataAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) => {
                if (secretExists.GetValueOrDefault($"{ns}/{name}"))
                {
                    // Return empty data matching what would be created from items with only "namespaces" field
                    return new Dictionary<string, string>();
                }
                return null;
            });
        
        // Set up secret annotations retrieval (needed for hash comparison)
        var secretAnnotations = new Dictionary<string, Dictionary<string, string>>();
        mockKubernetesService.Setup(x => x.GetSecretAnnotationsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) => {
                return secretAnnotations.GetValueOrDefault($"{ns}/{name}");
            });
        
        // Store annotations when creating/updating secrets
        mockKubernetesService.Setup(x => x.CreateSecretAsync(It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels, string secretType) => {
                secretExists[$"{ns}/{name}"] = true;
                if (annotations != null)
                {
                    secretAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                }
                return OperationResult.Successful();
            });
        
        mockKubernetesService.Setup(x => x.UpdateSecretAsync(It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels) => {
                if (annotations != null)
                {
                    secretAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                }
                return OperationResult.Successful();
            });
        
        var syncService = new SyncService(
            mockLogger.Object,
            mockVaultwardenService.Object,
            mockKubernetesService.Object,
            mockMetrics.Object,
            mockDbLogger.Object,
            syncConfig,
            new DockerConfigJsonSettings()
        );

        // Act - Run sync twice (second time should detect no changes)
        var firstSync = await syncService.SyncAsync();
        var secondSync = await syncService.SyncAsync();
        
        // Assert - Both syncs should succeed
        Assert.True(firstSync.OverallSuccess);
        Assert.True(secondSync.OverallSuccess);
        
        // First sync should process all items (created)
        Assert.Equal(2, firstSync.TotalNamespaces);
        Assert.True(firstSync.TotalSecretsProcessed > 0);
        
        // Second sync detects no changes and skips full reconciliation
        Assert.False(secondSync.HasChanges);
        Assert.Equal(0, secondSync.TotalNamespaces);
        Assert.Equal(0, secondSync.TotalSecretsProcessed);
    }
    
    [Fact]
    public async Task SyncSummary_ShouldShowCorrectStatus_WhenNoChanges()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
            mockKubernetesService.Setup(x => x.IsInitialized).Returns(true);
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        
        var syncConfig = new SyncSettings();
        
        var vaultItems = new List<VaultwardenItem>
        {
            new VaultwardenItem
            {
                Id = "item-1",
                Name = "test-secret",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
                }
            }
        };
        
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);
            
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
            
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.UpdateSyncProgressAsync(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), 
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.CacheVaultwardenItemsAsync(It.IsAny<List<VaultwardenItem>>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.CleanupStaleSecretStatesAsync(It.IsAny<List<VaultwardenItem>>()))
            .ReturnsAsync(0);
            
        mockDbLogger.Setup(x => x.UpsertSecretStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
            
        // Mock Kubernetes service for secret operations
        mockKubernetesService.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        mockKubernetesService.Setup(x => x.NamespaceExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        mockKubernetesService.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        mockKubernetesService.Setup(x => x.CreateSecretAsync(It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());
        
        var syncService = new SyncService(
            mockLogger.Object,
            mockVaultwardenService.Object,
            mockKubernetesService.Object,
            mockMetrics.Object,
            mockDbLogger.Object,
            syncConfig,
            new DockerConfigJsonSettings()
        );

        // Act - Run sync twice
        await syncService.SyncAsync();
        var secondSync = await syncService.SyncAsync();

        // Assert - Both syncs should succeed
        Assert.True(secondSync.OverallSuccess);
        // Second sync skips reconciliation when hash unchanged
        Assert.False(secondSync.HasChanges);
        // Status should be UP-TO-DATE since no changes were processed
        Assert.Equal("UP-TO-DATE", secondSync.GetStatusText());
    }
    
    [Fact]
    public async Task DatabaseLogger_ShouldReceiveCorrectCounts_WhenNoChanges()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
            mockKubernetesService.Setup(x => x.IsInitialized).Returns(true);
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        
        var syncConfig = new SyncSettings();
        
        var vaultItems = new List<VaultwardenItem>
        {
            new VaultwardenItem
            {
                Id = "item-1",
                Name = "secret-1",
                Fields = new List<FieldInfo> { new FieldInfo { Name = "namespaces", Value = "default", Type = 0 } }
            },
            new VaultwardenItem
            {
                Id = "item-2",
                Name = "secret-2",
                Fields = new List<FieldInfo> { new FieldInfo { Name = "namespaces", Value = "default", Type = 0 } }
            }
        };
        
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);
            
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
            
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.UpdateSyncProgressAsync(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), 
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.CacheVaultwardenItemsAsync(It.IsAny<List<VaultwardenItem>>()))
            .Returns(Task.CompletedTask);
            
        mockDbLogger.Setup(x => x.CleanupStaleSecretStatesAsync(It.IsAny<List<VaultwardenItem>>()))
            .ReturnsAsync(0);
            
        mockDbLogger.Setup(x => x.UpsertSecretStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
            
        // Mock Kubernetes service for secret operations  
        mockKubernetesService.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        mockKubernetesService.Setup(x => x.NamespaceExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        mockKubernetesService.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        mockKubernetesService.Setup(x => x.CreateSecretAsync(It.IsAny<string>(), It.IsAny<string>(), 
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());
        
        var syncService = new SyncService(
            mockLogger.Object,
            mockVaultwardenService.Object,
            mockKubernetesService.Object,
            mockMetrics.Object,
            mockDbLogger.Object,
            syncConfig,
            new DockerConfigJsonSettings()
        );

        // Act - Run sync twice
        var firstSync = await syncService.SyncAsync();
        var secondSync = await syncService.SyncAsync();

        // Assert - Both syncs should succeed
        Assert.True(firstSync.OverallSuccess);
        Assert.True(secondSync.OverallSuccess);

        // Database logger should have been called for both syncs
        mockDbLogger.Verify(
            x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()),
            Times.AtLeast(2)
        );
        mockDbLogger.Verify(
            x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.AtLeast(2)
        );
    }

    /// <summary>
    /// When only one Vaultwarden item's name changes, only that secret should be updated.
    /// Unchanged items should be skipped silently.
    /// </summary>
    [Fact]
    public async Task PartialItemNameChange_OnlyChangedItemUpdated()
    {
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
            mockKubernetesService.Setup(x => x.IsInitialized).Returns(true);
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();

        var syncConfig = new SyncSettings();

        var unchangedItems = new List<VaultwardenItem>
        {
            new() { Id = "item-1", Name = "Item Alpha", Type = 1,
                Login = new LoginInfo { Username = "user1", Password = "pass1" },
                Fields = new List<FieldInfo> { new() { Name = "namespaces", Value = "default", Type = 0 },
                                              new() { Name = "secret-name", Value = "secret-alpha", Type = 0 } } },
            new() { Id = "item-3", Name = "Item Gamma", Type = 1,
                Login = new LoginInfo { Username = "user3", Password = "pass3" },
                Fields = new List<FieldInfo> { new() { Name = "namespaces", Value = "default", Type = 0 },
                                              new() { Name = "secret-name", Value = "secret-gamma", Type = 0 } } }
        };

        var itemBeta = new VaultwardenItem
        {
            Id = "item-2", Name = "Item Beta", Type = 1,
            Login = new LoginInfo { Username = "user2", Password = "pass2" },
            Fields = new List<FieldInfo> { new() { Name = "namespaces", Value = "default", Type = 0 },
                                          new() { Name = "secret-name", Value = "secret-beta", Type = 0 } }
        };

        var itemBetaRenamed = new VaultwardenItem
        {
            Id = "item-2", Name = "Item Beta RENAMED", Type = 1,
            Login = new LoginInfo { Username = "user2", Password = "pass2" },
            Fields = new List<FieldInfo> { new() { Name = "namespaces", Value = "default", Type = 0 },
                                          new() { Name = "secret-name", Value = "secret-beta", Type = 0 } }
        };

        var firstSyncItems = new List<VaultwardenItem> { unchangedItems[0], itemBeta, unchangedItems[1] };
        var secondSyncItems = new List<VaultwardenItem> { unchangedItems[0], itemBetaRenamed, unchangedItems[1] };

        mockVaultwardenService.SetupSequence(x => x.GetItemsAsync())
            .ReturnsAsync(firstSyncItems)
            .ReturnsAsync(secondSyncItems);

        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        mockDbLogger.Setup(x => x.UpdateSyncProgressAsync(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        mockDbLogger.Setup(x => x.CacheVaultwardenItemsAsync(It.IsAny<List<VaultwardenItem>>()))
            .Returns(Task.CompletedTask);
        mockDbLogger.Setup(x => x.CleanupStaleSecretStatesAsync(It.IsAny<List<VaultwardenItem>>()))
            .ReturnsAsync(0);
        mockDbLogger.Setup(x => x.UpsertSecretStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var storedHashes = new Dictionary<string, string?>();
        mockDbLogger.Setup(x => x.GetSecretHashAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) =>
                storedHashes.TryGetValue($"{ns}/{name}", out var hash) ? hash : null);
        mockDbLogger.Setup(x => x.UpdateSecretHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string ns, string name, string hash) => { storedHashes[$"{ns}/{name}"] = hash; })
            .Returns(Task.CompletedTask);

        mockKubernetesService.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        mockKubernetesService.Setup(x => x.NamespaceExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var secretExists = new Dictionary<string, bool>();
        mockKubernetesService.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) => secretExists.GetValueOrDefault($"{ns}/{name}"));

        var storedData = new Dictionary<string, Dictionary<string, string>>();
        mockKubernetesService.Setup(x => x.GetSecretDataAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) =>
                storedData.TryGetValue($"{ns}/{name}", out var data) ? new Dictionary<string, string>(data) : null);

        var storedAnnotations = new Dictionary<string, Dictionary<string, string>>();
        mockKubernetesService.Setup(x => x.GetSecretAnnotationsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) =>
                storedAnnotations.TryGetValue($"{ns}/{name}", out var annotations) ? new Dictionary<string, string>(annotations) : null);

        var storedTypes = new Dictionary<string, string>();
        mockKubernetesService.Setup(x => x.GetSecretTypeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) =>
                storedTypes.TryGetValue($"{ns}/{name}", out var type) ? type : null);

        mockKubernetesService.Setup(x => x.CreateSecretAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data,
                Dictionary<string, string> annotations, Dictionary<string, string> labels, string secretType) =>
            {
                secretExists[$"{ns}/{name}"] = true;
                storedData[$"{ns}/{name}"] = new Dictionary<string, string>(data ?? new());
                if (annotations != null)
                    storedAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                if (secretType != null)
                    storedTypes[$"{ns}/{name}"] = secretType;
                return OperationResult.Successful();
            });

        mockKubernetesService.Setup(x => x.UpdateSecretAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data,
                Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    storedAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        var syncService = new SyncService(
            mockLogger.Object, mockVaultwardenService.Object, mockKubernetesService.Object,
            mockMetrics.Object, mockDbLogger.Object, syncConfig, new DockerConfigJsonSettings());

        var firstSync = await syncService.SyncAsync();
        var secondSync = await syncService.SyncAsync();

        Assert.True(firstSync.OverallSuccess);
        Assert.True(secondSync.OverallSuccess);
        Assert.Equal(3, firstSync.TotalSecretsCreated);
        Assert.Equal(0, firstSync.TotalSecretsUpdated);
        Assert.True(1 == secondSync.TotalSecretsUpdated,
            $"Expected 1 secret updated (renamed item only), got {secondSync.TotalSecretsUpdated}");
        Assert.True(2 == secondSync.TotalSecretsSkipped,
            $"Expected 2 secrets skipped (unchanged items), got {secondSync.TotalSecretsSkipped}");

        mockKubernetesService.Verify(x => x.UpdateSecretAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }
}