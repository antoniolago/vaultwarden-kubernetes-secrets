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
                Fields = new List<Field>
                {
                    new Field { Name = "namespaces", Value = "default", Type = 0 }
                }
            },
            new VaultwardenItem
            {
                Id = "item-2",
                Name = "secret-2",
                Fields = new List<Field>
                {
                    new Field { Name = "namespaces", Value = "default", Type = 0 }
                }
            },
            new VaultwardenItem
            {
                Id = "item-3",
                Name = "secret-3",
                Fields = new List<Field>
                {
                    new Field { Name = "namespaces", Value = "production", Type = 0 }
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
        
        // Assert - Second sync should show items as skipped
        Assert.False(secondSync.HasChanges);  // No changes detected
        Assert.Equal(3, secondSync.TotalItemsFromVaultwarden);  // 3 items fetched
        Assert.Equal(2, secondSync.TotalNamespaces);  // 2 namespaces (default, production)
        Assert.Equal(3, secondSync.TotalSecretsSkipped);  // ✅ THIS IS THE FIX - should be 3, not 0
        Assert.Equal(0, secondSync.TotalSecretsCreated);
        Assert.Equal(0, secondSync.TotalSecretsUpdated);
        Assert.Equal(0, secondSync.TotalSecretsFailed);
        
        // Verify namespace summaries are populated
        Assert.Equal(2, secondSync.Namespaces.Count);
        
        var defaultNs = secondSync.Namespaces.Find(ns => ns.Name == "default");
        Assert.NotNull(defaultNs);
        Assert.Equal(2, defaultNs.Skipped);  // 2 items in default namespace
        Assert.Equal(2, defaultNs.SourceItems);
        
        var prodNs = secondSync.Namespaces.Find(ns => ns.Name == "production");
        Assert.NotNull(prodNs);
        Assert.Equal(1, prodNs.Skipped);  // 1 item in production namespace
        Assert.Equal(1, prodNs.SourceItems);
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
                Fields = new List<Field>
                {
                    new Field { Name = "namespaces", Value = "default", Type = 0 }
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
        
        // Assert - Status should be "UP-TO-DATE" not "FAILED"
        Assert.Equal("UP-TO-DATE", secondSync.GetStatusText());
        Assert.Equal("⭕", secondSync.GetStatusIcon());
        Assert.True(secondSync.OverallSuccess);
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
                Fields = new List<Field> { new Field { Name = "namespaces", Value = "default", Type = 0 } }
            },
            new VaultwardenItem
            {
                Id = "item-2",
                Name = "secret-2",
                Fields = new List<Field> { new Field { Name = "namespaces", Value = "default", Type = 0 } }
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
        await syncService.SyncAsync();
        
        // Assert - Database logger should receive correct skipped count
        mockDbLogger.Verify(
            x => x.UpdateSyncProgressAsync(
                It.IsAny<long>(),
                2,  // processedItems should be 2
                0,  // created
                0,  // updated
                2,  // skipped should be 2, not 0
                0,  // failed
                0   // deleted
            ),
            Times.Once,
            "Database should be updated with correct skipped count (2), not 0"
        );
    }
}
