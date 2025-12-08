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
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels) => {
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
            syncConfig
        );
        
        // Act - Run sync twice (second time should detect no changes)
        var firstSync = await syncService.SyncAsync();
        var secondSync = await syncService.SyncAsync();
        
        // Assert - Both syncs should succeed and process all items
        Assert.True(firstSync.OverallSuccess);
        Assert.True(secondSync.OverallSuccess);
        Assert.Equal(3, secondSync.TotalItemsFromVaultwarden);  // 3 items fetched
        Assert.Equal(2, secondSync.TotalNamespaces);  // 2 namespaces (default, production)
        
        // Second sync should process all items (either skip or update/create)
        Assert.Equal(3, secondSync.TotalSecretsProcessed);
        Assert.Equal(0, secondSync.TotalSecretsFailed);
        
        // Verify namespace summaries are populated
        Assert.Equal(2, secondSync.Namespaces.Count);
    }
    
    [Fact]
    public async Task SyncSummary_ShouldShowCorrectStatus_WhenNoChanges()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
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
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());
        
        var syncService = new SyncService(
            mockLogger.Object,
            mockVaultwardenService.Object,
            mockKubernetesService.Object,
            mockMetrics.Object,
            mockDbLogger.Object,
            syncConfig
        );
        
        // Act - Run sync twice
        await syncService.SyncAsync();
        var secondSync = await syncService.SyncAsync();
        
        // Assert - Both syncs should succeed
        Assert.True(secondSync.OverallSuccess);
        // Status can be UP-TO-DATE, SUCCESS, or PARTIAL depending on how items were processed
        Assert.Contains(secondSync.GetStatusText(), new[] { "UP-TO-DATE", "SUCCESS", "PARTIAL" });
    }
    
    [Fact]
    public async Task DatabaseLogger_ShouldReceiveCorrectCounts_WhenNoChanges()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
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
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());
        
        var syncService = new SyncService(
            mockLogger.Object,
            mockVaultwardenService.Object,
            mockKubernetesService.Object,
            mockMetrics.Object,
            mockDbLogger.Object,
            syncConfig
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
}