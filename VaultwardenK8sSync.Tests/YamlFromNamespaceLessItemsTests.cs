using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using FieldInfo = VaultwardenK8sSync.Models.FieldInfo;

namespace VaultwardenK8sSync.Tests;

[Collection("SyncService Sequential")]
[Trait("Category", "YamlFromNotes")]
public class YamlFromNamespaceLessItemsTests
{
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock;
    private readonly SyncSettings _syncConfig;
    private readonly SyncService _syncService;

    public YamlFromNamespaceLessItemsTests()
    {
        _loggerMock = new Mock<ILogger<SyncService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
        _kubernetesServiceMock.Setup(x => x.IsInitialized).Returns(true);
        _metricsServiceMock = new Mock<IMetricsService>();
        _dbLoggerMock = new Mock<IDatabaseLoggerService>();
        _syncConfig = new SyncSettings();

        _dbLoggerMock.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
        _dbLoggerMock.Setup(x => x.CompleteSyncLogAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.CacheVaultwardenItemsAsync(It.IsAny<List<VaultwardenItem>>()))
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.UpdateSyncProgressAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.UpsertSecretStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.CleanupStaleSecretStatesAsync(It.IsAny<List<VaultwardenItem>>()))
            .ReturnsAsync(0);

        _syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig,
            new DockerConfigJsonSettings());
    }

    private const string ValidConfigMapYaml = @"apiVersion: v1
kind: ConfigMap
metadata:
  name: my-config
  namespace: default
data:
  key: value";

    private const string ValidSecretYaml = @"apiVersion: v1
kind: Secret
metadata:
  name: my-secret
  namespace: production
type: Opaque
data:
  password: cGFzc3dvcmQ=";

    [Fact]
    public async Task SyncAsync_WithYamlInNotesAndNoNamespaces_ShouldApplyYaml()
    {
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "configmap-from-notes",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = ValidConfigMapYaml
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(It.Is<string>(y => y.Contains("my-config"))), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WithMultipleYamlItemsAndNoNamespaces_ShouldApplyEachManifest()
    {
        var item1 = new VaultwardenItem
        {
            Id = "item-1",
            Name = "config-from-notes",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = ValidConfigMapYaml
        };

        var item2 = new VaultwardenItem
        {
            Id = "item-2",
            Name = "secret-from-notes",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = ValidSecretYaml
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item1, item2 });
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SyncAsync_WithYamlInNotesAndExistingNamespacesField_ShouldNotApplyYamlSeparately()
    {
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "configmap-from-notes",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = ValidConfigMapYaml,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("default"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync("default"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("default"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "configmap-from-notes"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "configmap-from-notes"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "configmap-from-notes"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("default", "configmap-from-notes"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "default", "configmap-from-notes", It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()))
            .ReturnsAsync(OperationResult.Successful());
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();

        // YAML from notes is applied via the normal yamlManifests path (inside SyncNamespaceAsync),
        // NOT via the YAML-only items path. The normal path also calls ApplyYamlAsync.
        // We verify the normal secret creation happened.
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            "default", "configmap-from-notes", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WithNoNamespacesAndNoYaml_ShouldNotApplyAnyYaml()
    {
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "plain-note",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "This is just a regular note with no YAML content"
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WithDryRunAndYamlOnlyItem_ShouldNotApplyYaml()
    {
        _syncConfig.DryRun = true;

        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "configmap-from-notes",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = ValidConfigMapYaml
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WithYamlOnlyItemAndApplyFailure_ShouldReportError()
    {
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "configmap-from-notes",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = ValidConfigMapYaml
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Failed("Test error"));

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("configmap-from-notes") && e.Contains("Test error"));
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WithYamlOnlyItemHavingInvalidYaml_ShouldNotApply()
    {
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "bad-yaml-note",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "This is not valid K8s YAML at all"
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WithMixedItems_ShouldProcessYamlOnlyItemsAndNamespacedItems()
    {
        var yamlOnlyItem = new VaultwardenItem
        {
            Id = "item-1",
            Name = "config-from-notes",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = ValidConfigMapYaml
        };

        var namespacedItem = new VaultwardenItem
        {
            Id = "item-2",
            Name = "db-credentials",
            Type = 1,
            Login = new LoginInfo { Username = "admin", Password = "secret" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { yamlOnlyItem, namespacedItem });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("default"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync("default"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("default"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "db-credentials"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "db-credentials"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "db-credentials"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("default", "db-credentials"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "default", "db-credentials", It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()))
            .ReturnsAsync(OperationResult.Successful());
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();

        // YAML-only item should trigger ApplyYamlAsync
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(It.Is<string>(y => y.Contains("my-config"))), Times.Once);

        // Namespaced item should be created as a normal secret
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            "default", "db-credentials", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WithMultiDocumentYaml_ShouldApplyOnce()
    {
        var multiDocYaml = @"---
apiVersion: v1
kind: ConfigMap
metadata:
  name: test-cm
  namespace: default
data:
  key: val1
---
apiVersion: v1
kind: Secret
metadata:
  name: test-secret
  namespace: default
type: Opaque
data:
  key: dmFsMg==";

        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "multi-doc-yaml",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = multiDocYaml
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();

        // Multi-doc YAML is applied as a single call to ApplyYamlAsync
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(It.Is<string>(y => y.Contains("apiVersion"))), Times.Once);
    }
}
