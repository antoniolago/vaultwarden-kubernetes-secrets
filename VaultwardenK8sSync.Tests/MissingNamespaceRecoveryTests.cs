using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Regression tests for the "new secret never appears until the pod is restarted" bug.
///
/// Sequence that triggered it: a new item targets a namespace that does not exist yet, so the
/// namespace is cached as known-missing. The namespace is created afterwards, but the cache entry
/// made every later full reconciliation skip it; the quick-change-detection path kept detecting
/// "secret missing" drift, forcing full syncs that skipped the namespace again. Only a restart
/// (empty cache) created the secret.
/// </summary>
[Collection("SyncService Sequential")]
public class MissingNamespaceRecoveryTests
{
    private readonly Mock<ILogger<SyncService>> _loggerMock = new();
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock = new();
    private readonly Mock<IKubernetesService> _kubernetesServiceMock = new();
    private readonly Mock<IMetricsService> _metricsServiceMock = new();
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock = new();
    private readonly SyncSettings _syncConfig = new() { LockFileName = $"vks-missing-ns-{Guid.NewGuid():N}.lock" };

    public MissingNamespaceRecoveryTests()
    {
        _kubernetesServiceMock.Setup(x => x.IsInitialized).Returns(true);

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
    }

    private SyncService CreateSyncService() => new(
        _loggerMock.Object,
        _vaultwardenServiceMock.Object,
        _kubernetesServiceMock.Object,
        _metricsServiceMock.Object,
        _dbLoggerMock.Object,
        _syncConfig,
        new DockerConfigJsonSettings());

    [Fact]
    public async Task SyncAsync_WhenNamespaceIsCreatedAfterFirstFailure_CreatesSecretOnNextSync()
    {
        var vaultItems = new List<VaultwardenItem>
        {
            new()
            {
                Id = "item-1",
                Name = "new-secret",
                Password = "secret-value",
                Fields = new List<FieldInfo>
                {
                    new() { Name = "namespaces", Value = "new-ns", Type = 0 }
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync()).ReturnsAsync(vaultItems);

        var namespaceExists = false;
        var secretExists = new Dictionary<string, bool>();
        var secretAnnotations = new Dictionary<string, Dictionary<string, string>>();
        var createCalls = 0;

        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(() => namespaceExists);
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(() => namespaceExists
                ? new List<string> { "default", "new-ns" }
                : new List<string> { "default" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("new-ns", "new-secret"))
            .ReturnsAsync(() => secretExists.GetValueOrDefault("new-ns/new-secret"));
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("new-ns", "new-secret"))
            .ReturnsAsync(() => secretAnnotations.GetValueOrDefault("new-ns/new-secret"));
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("new-ns", "new-secret"))
            .ReturnsAsync(() => secretExists.GetValueOrDefault("new-ns/new-secret")
                ? new Dictionary<string, string> { { "password", "secret-value" } }
                : null);
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(It.IsAny<string>()))
            .ReturnsAsync(() => secretExists.Where(kv => kv.Value).Select(kv => kv.Key.Split('/')[1]).ToList());
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "new-ns", "new-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<string?>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data,
                Dictionary<string, string>? annotations, Dictionary<string, string>? labels, string? type) =>
            {
                createCalls++;
                secretExists["new-ns/new-secret"] = true;
                if (annotations != null)
                    secretAnnotations["new-ns/new-secret"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        var syncService = CreateSyncService();

        // 1st sync: namespace does not exist yet -> item is skipped and the namespace is cached
        await syncService.SyncAsync();
        Assert.False(secretExists.GetValueOrDefault("new-ns/new-secret"));
        Assert.Equal(0, createCalls);

        // namespace is created out-of-band (new service rollout)
        namespaceExists = true;

        // 2nd sync: must re-check the namespace and create the secret without a pod restart
        await syncService.SyncAsync();

        Assert.Equal(1, createCalls);
        Assert.True(secretExists.GetValueOrDefault("new-ns/new-secret"),
            "secret must be created once the namespace exists, without restarting the process");
    }
}
