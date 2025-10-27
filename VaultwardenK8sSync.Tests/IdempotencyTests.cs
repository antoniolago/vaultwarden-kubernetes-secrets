using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Tests to ensure sync operations are idempotent - running multiple times produces consistent results
/// This verifies the fix for concurrent sync issues.
/// </summary>
public class IdempotencyTests
{
    [Fact]
    public async Task SyncShouldProduceConsistentResults_WhenRun5TimesWithEmptyVault()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        
        var syncConfig = new SyncSettings
        {
            SyncIntervalSeconds = 30,
            ContinuousSync = false
        };
        
        // Setup: Empty vault
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());
            
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync((string phase, int items, int interval, bool continuous) => 1L);
            
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
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
        
        // Act - Run sync 5 times
        var results = new List<SyncSummary>();
        for (int i = 0; i < 5; i++)
        {
            var result = await syncService.SyncAsync();
            results.Add(result);
            await Task.Delay(10); // Small delay to ensure different timestamps
        }
        
        // Assert - All runs should produce IDENTICAL results
        for (int i = 0; i < 5; i++)
        {
            var result = results[i];
            Assert.True(result.OverallSuccess, $"Run {i + 1} should succeed");
            Assert.Equal(0, result.TotalItemsFromVaultwarden);
            Assert.Equal(0, result.TotalSecretsProcessed);
            Assert.Equal(0, result.TotalSecretsCreated);
            Assert.Equal(0, result.TotalSecretsUpdated);
            Assert.Equal(0, result.TotalSecretsSkipped);
            Assert.Equal(0, result.TotalSecretsFailed);
            Assert.Single(result.Warnings); // Should warn about empty vault
        }
    }
    
    [Fact]
    public async Task SyncNumberShouldIncrement_ForEach_Of_5_Runs()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        
        var syncConfig = new SyncSettings();
        
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());
            
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
            
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
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
        
        // Act - Run sync 5 times
        var results = new List<SyncSummary>();
        for (int i = 0; i < 5; i++)
        {
            var result = await syncService.SyncAsync();
            results.Add(result);
        }
        
        // Assert - Sync numbers should increment sequentially
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i + 1, results[i].SyncNumber);
        }
        
        // All sync numbers should be unique
        var syncNumbers = results.Select(r => r.SyncNumber).ToList();
        Assert.Equal(5, syncNumbers.Distinct().Count());
    }
    
    [Fact]
    public async Task TimestampsShouldBeUnique_ForEach_Of_5_Runs()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        
        var syncConfig = new SyncSettings();
        
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());
            
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
            
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
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
        
        // Act - Run sync 5 times with delays
        var results = new List<SyncSummary>();
        for (int i = 0; i < 5; i++)
        {
            var result = await syncService.SyncAsync();
            results.Add(result);
            await Task.Delay(10); // Ensure different timestamps
        }
        
        // Assert - Each run should have unique start/end times
        var startTimes = results.Select(r => r.StartTime).ToList();
        var endTimes = results.Select(r => r.EndTime).ToList();
        
        Assert.Equal(5, startTimes.Distinct().Count());
        Assert.Equal(5, endTimes.Distinct().Count());
        
        // Times should be in ascending order
        for (int i = 1; i < 5; i++)
        {
            Assert.True(startTimes[i] >= startTimes[i - 1]);
            Assert.True(endTimes[i] >= endTimes[i - 1]);
        }
    }
    
    [Fact]
    public async Task OverallSuccessShouldBeConsistent_ForAll5Runs()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        
        var syncConfig = new SyncSettings();
        
        // Setup: vault returns same data every time
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());
            
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
            
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
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
        
        // Act - Run sync 5 times
        var results = new List<SyncSummary>();
        for (int i = 0; i < 5; i++)
        {
            var result = await syncService.SyncAsync();
            results.Add(result);
        }
        
        // Assert - All results should have same success status
        var successStatuses = results.Select(r => r.OverallSuccess).Distinct().ToList();
        Assert.Single(successStatuses);
        
        // All should succeed with empty vault
        Assert.All(results, r => Assert.True(r.OverallSuccess));
    }
    
    [Fact]
    public async Task StatusTextShouldBeConsistent_ForAll5Runs()
    {
        // Arrange  
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        
        var syncConfig = new SyncSettings();
        
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());
            
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
            
        mockDbLogger.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
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
        
        // Act - Run sync 5 times
        var results = new List<SyncSummary>();
        for (int i = 0; i < 5; i++)
        {
            var result = await syncService.SyncAsync();
            results.Add(result);
        }
        
        // Assert - All should have same status text
        var statusTexts = results.Select(r => r.GetStatusText()).Distinct().ToList();
        Assert.Single(statusTexts);
        Assert.Equal("UP-TO-DATE", statusTexts[0]);
    }
}
