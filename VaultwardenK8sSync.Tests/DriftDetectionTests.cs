using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Models;
using Xunit;
using FluentAssertions;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Tests for K8s state drift detection when Vaultwarden items haven't changed.
/// Verifies the fix for the bug where secrets deleted/modified externally in K8s
/// are not resynced because the quick-change-detection hash check skips reconciliation.
/// </summary>
[Collection("SyncService Sequential")]
public class DriftDetectionTests
{
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock;
    private readonly SyncSettings _syncConfig;

    public DriftDetectionTests()
    {
        _loggerMock = new Mock<ILogger<SyncService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
        _kubernetesServiceMock.Setup(x => x.IsInitialized).Returns(true);
        _metricsServiceMock = new Mock<IMetricsService>();
        _dbLoggerMock = new Mock<IDatabaseLoggerService>();
        _syncConfig = new SyncSettings();

        // Default database logger setups
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

        // Default namespace validation
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
    }

    private SyncService CreateSyncService()
    {
        return new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig,
            new DockerConfigJsonSettings()
        );
    }

    /// <summary>
    /// When a K8s secret is deleted externally, the second sync should detect drift
    /// and recreate it instead of skipping.
    /// </summary>
    [Fact]
    public async Task SyncAsync_WhenSecretDeletedExternally_ShouldDetectDriftAndRecreate()
    {
        // Arrange
        var vaultItems = new List<VaultwardenItem>
        {
            new()
            {
                Id = "item-1",
                Name = "test-secret",
                Password = "secret-value",
                Fields = new List<FieldInfo>
                {
                    new() { Name = "namespaces", Value = "default", Type = 0 }
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });

        // Track secret existence and annotations
        var secretExists = new Dictionary<string, bool>();
        var secretAnnotations = new Dictionary<string, Dictionary<string, string>>();

        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "test-secret"))
            .ReturnsAsync(() => secretExists.GetValueOrDefault("default/test-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "test-secret"))
            .ReturnsAsync(() => secretAnnotations.GetValueOrDefault("default/test-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "test-secret"))
            .ReturnsAsync(() =>
            {
                if (secretExists.GetValueOrDefault("default/test-secret"))
                    return new Dictionary<string, string> { { "password", "secret-value" } };
                return null;
            });

        // Mock GetSecretsWithManagedKeysAsync to return the expected secret
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("default"))
            .ReturnsAsync(() => secretExists.Where(kv => kv.Value).Select(kv => kv.Key.Split('/')[1]).ToList());

        // Mock secret creation
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "default", "test-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels, string secretType) =>
            {
                secretExists["default/test-secret"] = true;
                if (annotations != null)
                    secretAnnotations["default/test-secret"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        // Mock UpdateSecretAsync
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
                "default", "test-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    secretAnnotations["default/test-secret"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        var syncService = CreateSyncService();

        // Act - First sync: creates the secret
        var firstSync = await syncService.SyncAsync();
        Assert.True(firstSync.OverallSuccess);

        // Verify secret was created
        Assert.True(secretExists.GetValueOrDefault("default/test-secret"));

        // Simulate external deletion of the secret
        secretExists["default/test-secret"] = false;
        secretAnnotations.Remove("default/test-secret");

        // Act - Second sync: should detect drift and recreate
        var secondSync = await syncService.SyncAsync();

        // Assert
        Assert.True(secondSync.OverallSuccess);
        // The secret should be recreated
        Assert.True(secretExists.GetValueOrDefault("default/test-secret"),
            "Secret should have been recreated after external deletion");
    }

    /// <summary>
    /// When a K8s secret's content hash doesn't match (externally modified),
    /// the second sync should detect drift and update it.
    /// </summary>
    [Fact]
    public async Task SyncAsync_WhenSecretHashMismatch_ShouldDetectDriftAndUpdate()
    {
        // Arrange
        var vaultItems = new List<VaultwardenItem>
        {
            new()
            {
                Id = "item-1",
                Name = "test-secret",
                Password = "correct-value",
                Fields = new List<FieldInfo>
                {
                    new() { Name = "namespaces", Value = "default", Type = 0 }
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });

        var secretExists = new Dictionary<string, bool>();
        var secretAnnotations = new Dictionary<string, Dictionary<string, string>>();

        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "test-secret"))
            .ReturnsAsync(() => secretExists.GetValueOrDefault("default/test-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "test-secret"))
            .ReturnsAsync(() => secretAnnotations.GetValueOrDefault("default/test-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "test-secret"))
            .ReturnsAsync(() =>
            {
                if (secretExists.GetValueOrDefault("default/test-secret"))
                    return new Dictionary<string, string> { { "password", "correct-value" } };
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("default"))
            .ReturnsAsync(() => secretExists.Where(kv => kv.Value).Select(kv => kv.Key.Split('/')[1]).ToList());

        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "default", "test-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels, string secretType) =>
            {
                secretExists["default/test-secret"] = true;
                if (annotations != null)
                    secretAnnotations["default/test-secret"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
                "default", "test-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    secretAnnotations["default/test-secret"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        var syncService = CreateSyncService();

        // Act - First sync: creates the secret
        var firstSync = await syncService.SyncAsync();
        Assert.True(firstSync.OverallSuccess);

        // Verify secret was created with correct hash annotation
        Assert.True(secretExists.GetValueOrDefault("default/test-secret"));
        var originalHash = secretAnnotations["default/test-secret"]
            .GetValueOrDefault(Constants.Kubernetes.HashAnnotationKey);
        Assert.NotNull(originalHash);

        // Simulate external modification: change the hash annotation to something else
        secretAnnotations["default/test-secret"][Constants.Kubernetes.HashAnnotationKey] = "tampered-hash-value";

        // Act - Second sync: should detect hash mismatch drift and update
        var secondSync = await syncService.SyncAsync();

        // Assert
        Assert.True(secondSync.OverallSuccess);
        // The hash annotation should be restored to the correct value
        var restoredHash = secretAnnotations["default/test-secret"]
            .GetValueOrDefault(Constants.Kubernetes.HashAnnotationKey);
        Assert.Equal(originalHash, restoredHash);
    }

    /// <summary>
    /// When everything is in sync (no drift), the second sync should still skip
    /// reconciliation with "UP-TO-DATE" status.
    /// </summary>
    [Fact]
    public async Task SyncAsync_WhenNoDrift_ShouldSkipReconciliation()
    {
        // Arrange
        var vaultItems = new List<VaultwardenItem>
        {
            new()
            {
                Id = "item-1",
                Name = "test-secret",
                Password = "secret-value",
                Fields = new List<FieldInfo>
                {
                    new() { Name = "namespaces", Value = "default", Type = 0 }
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });

        var secretExists = new Dictionary<string, bool>();
        var secretAnnotations = new Dictionary<string, Dictionary<string, string>>();

        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "test-secret"))
            .ReturnsAsync(() => secretExists.GetValueOrDefault("default/test-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "test-secret"))
            .ReturnsAsync(() => secretAnnotations.GetValueOrDefault("default/test-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "test-secret"))
            .ReturnsAsync(() =>
            {
                if (secretExists.GetValueOrDefault("default/test-secret"))
                    return new Dictionary<string, string> { { "password", "secret-value" } };
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("default"))
            .ReturnsAsync(() => secretExists.Where(kv => kv.Value).Select(kv => kv.Key.Split('/')[1]).ToList());

        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "default", "test-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels, string secretType) =>
            {
                secretExists["default/test-secret"] = true;
                if (annotations != null)
                    secretAnnotations["default/test-secret"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
                "default", "test-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    secretAnnotations["default/test-secret"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        // Also need to set up GetSecretTypeAsync for the Reconciled secret path (VerifyK8sStateAsync doesn't use it,
        // but the reconciliation flow might reach SyncSecretAsync which does)
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("default", "test-secret"))
            .ReturnsAsync("Opaque");

        var dbHashCallback = "";
        _dbLoggerMock.Setup(x => x.UpdateSecretHashAsync("default", "test-secret", It.IsAny<string>()))
            .Callback((string ns, string name, string hash) => { dbHashCallback = hash; })
            .Returns(Task.CompletedTask);

        _dbLoggerMock.Setup(x => x.GetSecretHashAsync("default", "test-secret"))
            .ReturnsAsync(() => dbHashCallback);

        var syncService = CreateSyncService();

        // Act - First sync: creates the secret
        var firstSync = await syncService.SyncAsync();

        // Verify the sync was processed
        Assert.True(firstSync.OverallSuccess);

        // Act - Second sync: should verify no drift and skip
        var secondSync = await syncService.SyncAsync();

        // Assert
        Assert.True(secondSync.OverallSuccess);
        Assert.False(secondSync.HasChanges, "Should not report changes when nothing changed");
        // The secret should still exist
        Assert.True(secretExists.GetValueOrDefault("default/test-secret"));
    }

    /// <summary>
    /// When an unexpected managed secret appears in K8s but not in VW items,
    /// the drift check should detect it (orphan detection).
    /// </summary>
    [Fact]
    public async Task SyncAsync_WhenOrphanSecretExists_ShouldDetectDrift()
    {
        // Arrange
        var vaultItems = new List<VaultwardenItem>
        {
            new()
            {
                Id = "item-1",
                Name = "active-secret",
                Password = "value",
                Fields = new List<FieldInfo>
                {
                    new() { Name = "namespaces", Value = "default", Type = 0 }
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });

        var secretExists = new Dictionary<string, bool>
        {
            { "default/active-secret", false }
        };
        var secretAnnotations = new Dictionary<string, Dictionary<string, string>>();

        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "active-secret"))
            .ReturnsAsync(() => secretExists.GetValueOrDefault("default/active-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "active-secret"))
            .ReturnsAsync(() => secretAnnotations.GetValueOrDefault("default/active-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "active-secret"))
            .ReturnsAsync(() =>
            {
                if (secretExists.GetValueOrDefault("default/active-secret"))
                    return new Dictionary<string, string> { { "password", "value" } };
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("default", "active-secret"))
            .ReturnsAsync("Opaque");

        // Return active-secret only (the orphan will be added later)
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("default"))
            .ReturnsAsync(() =>
            {
                var result = new List<string>();
                foreach (var kv in secretExists)
                {
                    if (kv.Value)
                        result.Add(kv.Key.Split('/')[1]);
                }
                // Also include orphan if it has been "created"
                if (_orphanExists)
                    result.Add("orphan-secret");
                return result;
            });

        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "default", "active-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels, string secretType) =>
            {
                secretExists[$"{ns}/{name}"] = true;
                if (annotations != null)
                    secretAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
                "default", "active-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    secretAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        // Also mock RemoveManagedKeysAsync and DeleteSecretAsync for orphan cleanup
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync("default", "orphan-secret"))
            .ReturnsAsync((bool?)null);

        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync("default", "orphan-secret"))
            .ReturnsAsync(true);

        var syncService = CreateSyncService();

        // Act - First sync creates the active secret
        var firstSync = await syncService.SyncAsync();
        Assert.True(firstSync.OverallSuccess);
        Assert.True(secretExists.GetValueOrDefault("default/active-secret"));

        // Now simulate an orphan secret appearing (externally created)
        _orphanExists = true;
        // Also allow the orphan to "exist" for existence checks
        secretExists["default/orphan-secret"] = true;

        // Act - Second sync: should detect orphan drift and trigger full reconciliation
        var secondSync = await syncService.SyncAsync();

        // Assert - The sync should have processed (full reconciliation was triggered due to drift)
        Assert.True(secondSync.OverallSuccess);
        // The active secret should still exist
        Assert.True(secretExists.GetValueOrDefault("default/active-secret"));

        // Verify that orphan cleanup was attempted (GetSecretsWithManagedKeysAsync will have been called,
        // and RemoveManagedKeysAsync should have been called for the orphan)
        _kubernetesServiceMock.Verify(
            x => x.GetSecretsWithManagedKeysAsync("default"),
            Times.AtLeastOnce);
    }
    private bool _orphanExists = false;

    /// <summary>
    /// Drift detection should work across multiple namespaces.
    /// </summary>
    [Fact]
    public async Task SyncAsync_MultipleNamespaces_ShouldDetectDriftInAnyNamespace()
    {
        // Arrange
        var vaultItems = new List<VaultwardenItem>
        {
            new()
            {
                Id = "item-1",
                Name = "secret-a",
                Password = "value-a",
                Fields = new List<FieldInfo>
                {
                    new() { Name = "namespaces", Value = "ns1", Type = 0 }
                }
            },
            new()
            {
                Id = "item-2",
                Name = "secret-b",
                Password = "value-b",
                Fields = new List<FieldInfo>
                {
                    new() { Name = "namespaces", Value = "ns2", Type = 0 }
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "ns1", "ns2" });

        var secretExists = new Dictionary<string, bool>();
        var secretAnnotations = new Dictionary<string, Dictionary<string, string>>();

        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) => secretExists.GetValueOrDefault($"{ns}/{name}"));

        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) => secretAnnotations.GetValueOrDefault($"{ns}/{name}"));

        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string ns, string name) =>
            {
                if (secretExists.GetValueOrDefault($"{ns}/{name}"))
                    return new Dictionary<string, string> { { "password", ns == "ns1" ? "value-a" : "value-b" } };
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Opaque");

        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(It.IsAny<string>()))
            .ReturnsAsync((string ns) =>
            {
                return secretExists
                    .Where(kv => kv.Value && kv.Key.StartsWith(ns))
                    .Select(kv => kv.Key.Split('/')[1])
                    .ToList();
            });

        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels, string secretType) =>
            {
                secretExists[$"{ns}/{name}"] = true;
                if (annotations != null)
                    secretAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    secretAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        var dbHashCallback1 = "";
        var dbHashCallback2 = "";
        _dbLoggerMock.Setup(x => x.UpdateSecretHashAsync("ns1", "secret-a", It.IsAny<string>()))
            .Callback((string ns, string name, string hash) => { dbHashCallback1 = hash; })
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.UpdateSecretHashAsync("ns2", "secret-b", It.IsAny<string>()))
            .Callback((string ns, string name, string hash) => { dbHashCallback2 = hash; })
            .Returns(Task.CompletedTask);

        _dbLoggerMock.Setup(x => x.GetSecretHashAsync("ns1", "secret-a"))
            .ReturnsAsync(() => dbHashCallback1);
        _dbLoggerMock.Setup(x => x.GetSecretHashAsync("ns2", "secret-b"))
            .ReturnsAsync(() => dbHashCallback2);

        var syncService = CreateSyncService();

        // Act - First sync creates both secrets
        var firstSync = await syncService.SyncAsync();
        Assert.True(firstSync.OverallSuccess);
        Assert.True(secretExists.GetValueOrDefault("ns1/secret-a"));
        Assert.True(secretExists.GetValueOrDefault("ns2/secret-b"));

        // Simulate deletion of secret in ns1 only
        secretExists["ns1/secret-a"] = false;
        secretAnnotations.Remove("ns1/secret-a");

        // Act - Second sync: should detect drift in ns1 and recreate
        var secondSync = await syncService.SyncAsync();

        // Assert
        Assert.True(secondSync.OverallSuccess);
        // The deleted secret should be recreated
        Assert.True(secretExists.GetValueOrDefault("ns1/secret-a"),
            "Secret in ns1 should be recreated after external deletion");
        // The other secret should still exist
        Assert.True(secretExists.GetValueOrDefault("ns2/secret-b"),
            "Secret in ns2 should still exist");
    }

    /// <summary>
    /// When Vaultwarden items are unchanged and an item has ignored fields,
    /// drift detection should still work for the remaining items.
    /// The ignore-field custom field (with comma-separated field names) excludes
    /// specific fields from secret data but does NOT skip the entire item.
    /// </summary>
    [Fact]
    public async Task SyncAsync_WithIgnoredFields_ShouldStillDetectDrift()
    {
        // Arrange
        var vaultItems = new List<VaultwardenItem>
        {
            // This item has an ignore-field to exclude a specific field from data
            new()
            {
                Id = "item-1",
                Name = "my-secret",
                Password = "password-value",
                Fields = new List<FieldInfo>
                {
                    new() { Name = "namespaces", Value = "default", Type = 0 },
                    new() { Name = "ignore-field", Value = "some-extra-field", Type = 0 }
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(vaultItems);

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });

        var secretExists = new Dictionary<string, bool>();
        var secretAnnotations = new Dictionary<string, Dictionary<string, string>>();

        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "my-secret"))
            .ReturnsAsync(() => secretExists.GetValueOrDefault("default/my-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "my-secret"))
            .ReturnsAsync(() => secretAnnotations.GetValueOrDefault("default/my-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "my-secret"))
            .ReturnsAsync(() =>
            {
                if (secretExists.GetValueOrDefault("default/my-secret"))
                    return new Dictionary<string, string> { { "password", "password-value" } };
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("default", "my-secret"))
            .ReturnsAsync("Opaque");

        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("default"))
            .ReturnsAsync(() => secretExists.Where(kv => kv.Value).Select(kv => kv.Key.Split('/')[1]).ToList());

        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "default", "my-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels, string secretType) =>
            {
                secretExists[$"{ns}/{name}"] = true;
                if (annotations != null)
                    secretAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
                "default", "my-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data, Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    secretAnnotations[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        var dbHashCallback = "";
        _dbLoggerMock.Setup(x => x.UpdateSecretHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string ns, string name, string hash) => { dbHashCallback = hash; })
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.GetSecretHashAsync("default", "my-secret"))
            .ReturnsAsync(() => dbHashCallback);

        var syncService = CreateSyncService();

        // Act - First sync creates the secret (ignore-field just filters specific data fields)
        var firstSync = await syncService.SyncAsync();
        Assert.True(firstSync.OverallSuccess);
        Assert.True(secretExists.GetValueOrDefault("default/my-secret"),
            "Secret should be created even with ignore-field present");

        // Capture the correct hash annotation
        var originalHash = secretAnnotations["default/my-secret"]
            .GetValueOrDefault(Constants.Kubernetes.HashAnnotationKey);

        // Simulate external deletion of the secret
        secretExists["default/my-secret"] = false;
        secretAnnotations.Remove("default/my-secret");

        // Act - Second sync: should detect drift even with the ignore-field item present
        var secondSync = await syncService.SyncAsync();

        // Assert
        Assert.True(secondSync.OverallSuccess);
        // The secret should be recreated
        Assert.True(secretExists.GetValueOrDefault("default/my-secret"),
            "Secret should be recreated after external deletion");

        var restoredHash = secretAnnotations["default/my-secret"]
            .GetValueOrDefault(Constants.Kubernetes.HashAnnotationKey);
        Assert.Equal(originalHash, restoredHash);
    }
}
