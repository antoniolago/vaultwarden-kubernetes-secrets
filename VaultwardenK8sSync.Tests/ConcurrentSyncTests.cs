using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace VaultwardenK8sSync.Tests;

public class ConcurrentSyncTests
{
    [Fact]
    public async Task ShouldNotCreateMultipleInProgressSyncLogs_WhenConcurrentSyncsAttempt()
    {
        // Arrange
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        var mockMetrics = new Mock<IMetricsService>();
        var mockLogger = new Mock<ILogger<SyncService>>();
        
        var syncConfig = new SyncSettings
        {
            SyncIntervalSeconds = 30,
            ContinuousSync = true,
            DryRun = false,
            DeleteOrphans = true
        };
        
        var kubernetesConfig = new KubernetesSettings
        {
            DefaultNamespace = "default"
        };
        
        // Track how many sync logs are created and in what state
        var syncLogsCreated = new List<(long id, string status)>();
        long nextSyncLogId = 1;
        var syncLogLock = new object();
        
        // Mock StartSyncLogAsync to track when InProgress logs are created
        mockDbLogger
            .Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(() =>
            {
                long id;
                lock (syncLogLock)
                {
                    id = nextSyncLogId++;
                    syncLogsCreated.Add((id, "InProgress"));
                }
                return id;
            });
        
        // Mock UpdateSyncProgressAsync to track progress updates
        mockDbLogger
            .Setup(x => x.UpdateSyncProgressAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        
        // Mock CompleteSyncLogAsync to track when logs are completed
        mockDbLogger
            .Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((long id, string status, string? error) =>
            {
                lock (syncLogLock)
                {
                    var existingIndex = syncLogsCreated.FindIndex(log => log.id == id);
                    if (existingIndex >= 0)
                    {
                        syncLogsCreated[existingIndex] = (id, status);
                    }
                }
                return Task.CompletedTask;
            });
        
        // Make authentication slow to simulate real-world scenario where syncs overlap
        mockVaultwardenService
            .Setup(x => x.GetItemsAsync())
            .Returns(async () =>
            {
                await Task.Delay(2000); // 2 second delay
                return new List<VaultwardenItem>();
            });
        
        var syncService = new SyncService(
            mockLogger.Object,
            mockVaultwardenService.Object,
            mockKubernetesService.Object,
            mockMetrics.Object,
            mockDbLogger.Object,
            syncConfig
        );
        
        // Act - Start 3 syncs concurrently
        var syncTasks = new List<Task<SyncSummary>>
        {
            Task.Run(() => syncService.SyncAsync()),
            Task.Run(() => syncService.SyncAsync()),
            Task.Run(() => syncService.SyncAsync())
        };
        
        // Give them time to start
        await Task.Delay(500);
        
        // Assert - Only ONE should have actually started (created InProgress log)
        // The others should have been blocked by the semaphore
        var inProgressCount = syncLogsCreated.Count(log => log.status == "InProgress");
        
        Assert.True(inProgressCount <= 1, 
            $"Expected at most 1 InProgress sync log, but found {inProgressCount}. " +
            $"This indicates concurrent syncs are not properly prevented. " +
            $"Logs created: {string.Join(", ", syncLogsCreated.Select(l => $"({l.id}, {l.status})"))}");
        
        // Wait for syncs to complete
        await Task.WhenAll(syncTasks);
        
        // After completion, no logs should remain in InProgress state
        var finalInProgressCount = syncLogsCreated.Count(log => log.status == "InProgress");
        Assert.Equal(0, finalInProgressCount);
    }
    
    [Fact]
    public async Task ShouldPreventConcurrentSyncs_WithSemaphoreLock()
    {
        // Arrange
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        var mockMetrics = new Mock<IMetricsService>();
        var mockLogger = new Mock<ILogger<SyncService>>();
        
        var syncConfig = new SyncSettings
        {
            SyncIntervalSeconds = 30,
            ContinuousSync = true,
            DryRun = false,
            DeleteOrphans = true
        };
        
        var kubernetesConfig = new KubernetesSettings
        {
            DefaultNamespace = "default"
        };
        
        var concurrentExecutions = 0;
        var maxConcurrentExecutions = 0;
        var executionLock = new object();
        
        mockDbLogger
            .Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
        
        mockDbLogger
            .Setup(x => x.UpdateSyncProgressAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        
        mockDbLogger
            .Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        
        // Track concurrent executions
        mockVaultwardenService
            .Setup(x => x.GetItemsAsync())
            .Returns(async () =>
            {
                lock (executionLock)
                {
                    concurrentExecutions++;
                    if (concurrentExecutions > maxConcurrentExecutions)
                    {
                        maxConcurrentExecutions = concurrentExecutions;
                    }
                }
                
                await Task.Delay(1000); // Simulate work
                
                lock (executionLock)
                {
                    concurrentExecutions--;
                }
                
                return new List<VaultwardenItem>();
            });
        
        var syncService = new SyncService(
            mockLogger.Object,
            mockVaultwardenService.Object,
            mockKubernetesService.Object,
            mockMetrics.Object,
            mockDbLogger.Object,
            syncConfig
        );
        
        // Act - Start 5 syncs concurrently
        var syncTasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => syncService.SyncAsync()))
            .ToArray();
        
        await Task.WhenAll(syncTasks);
        
        // Assert - Should never have more than 1 concurrent execution
        Assert.Equal(1, maxConcurrentExecutions);
    }
}
