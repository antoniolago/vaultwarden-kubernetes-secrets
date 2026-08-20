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

    // Regression: an item with Kubernetes YAML in NOTES plus a `namespaces` custom field
    // must produce a Secret that carries the YAML's data (e.g. stringData), NOT an empty
    // one. Prior behavior: the YAML was applied via ApplyYamlAsync() but then the normal
    // secret path (SyncSecretAsync -> CreateSecretAsync) created/overwrote the Secret with
    // an empty combinedSecretData, yielding a Secret with no data and managed-keys: [].
    [Fact]
    public async Task SyncAsync_YamlSecretInNotesWithNamespaces_ShouldCarryYamlData_NotEmpty()
    {
        const string yamlWithStringData = @"apiVersion: v1
kind: Secret
metadata:
  name: netbird-setup
  namespace: monitoring
type: Opaque
stringData:
  NETBIRD_SETUP_KEY: supersecretvalue";

        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "netbird-setup",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = yamlWithStringData,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "monitoring", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("monitoring"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync("monitoring"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("monitoring"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("monitoring", "netbird-setup"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("monitoring", "netbird-setup"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("monitoring", "netbird-setup"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("monitoring", "netbird-setup"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "monitoring", "netbird-setup", It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()))
            .ReturnsAsync(OperationResult.Successful());
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();

        // The Secret created by the normal path MUST NOT be empty: it should carry the
        // value defined in the YAML's stringData. This is the regression we fixed.
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            "monitoring", "netbird-setup",
            It.Is<Dictionary<string, string>>(d => d.ContainsKey("NETBIRD_SETUP_KEY") && d["NETBIRD_SETUP_KEY"] == "supersecretvalue"),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()),
            Times.Once);
    }

    // A YAML Secret with a `namespaces` custom field listing multiple namespaces must be
    // created with the YAML's data in EACH declared namespace (not just the one hardcoded
    // in the manifest, and never as an empty secret).
    [Fact]
    public async Task SyncAsync_YamlSecretInNotesWithMultipleNamespaces_ShouldCreateWithDataInEach()
    {
        const string yamlWithStringData = @"apiVersion: v1
kind: Secret
metadata:
  name: netbird-setup
  namespace: monitoring
type: Opaque
stringData:
  NETBIRD_SETUP_KEY: supersecretvalue";

        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "netbird-setup",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = yamlWithStringData,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "monitoring,other", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("monitoring")).ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("other")).ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(It.IsAny<string>())).ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(It.IsAny<string>())).ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), "netbird-setup")).ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(It.IsAny<string>(), "netbird-setup")).ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(It.IsAny<string>(), "netbird-setup")).ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(It.IsAny<string>(), "netbird-setup")).ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                It.IsAny<string>(), "netbird-setup", It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()))
            .ReturnsAsync(OperationResult.Successful());
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();

        // The Secret must be created with its YAML data in BOTH namespaces.
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            "monitoring", "netbird-setup",
            It.Is<Dictionary<string, string>>(d => d.ContainsKey("NETBIRD_SETUP_KEY") && d["NETBIRD_SETUP_KEY"] == "supersecretvalue"),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()),
            Times.Once);
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            "other", "netbird-setup",
            It.Is<Dictionary<string, string>>(d => d.ContainsKey("NETBIRD_SETUP_KEY") && d["NETBIRD_SETUP_KEY"] == "supersecretvalue"),
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

    [Fact]
    public async Task SyncAsync_WhenYamlCreatesSameSecret_ShouldNotDetectFalseDrift()
    {
        // Regression test: YAML-in-notes items are processed BEFORE the namespace sync loop.
        // Previously they were processed AFTER, which meant ApplyYamlAsync would ReplaceSecret
        // and overwrite the content-hash annotation that the sync just wrote.
        // This caused false-positive drift detection on every sync cycle.
        //
        // Scenario:
        // 1. YAML-only item: creates Secret "api-token" in "test-ns" (no namespaces field)
        // 2. Namespaced item: targets "test-ns", secret-name="api-token", password="my-token"
        //
        // After SyncAsync, the secret should have the correct hash annotation.
        // A second SyncAsync should NOT detect drift (HasChanges = false).

        var secretExistsTracker = new Dictionary<string, bool>();
        var secretAnnotationsTracker = new Dictionary<string, Dictionary<string, string>>();

        var apiSecretYaml = @"apiVersion: v1
kind: Secret
metadata:
  name: api-token
  namespace: test-ns
type: Opaque
data:
  api-key: bXktdG9rZW4=";

        var yamlOnlyItem = new VaultwardenItem
        {
            Id = "item-1",
            Name = "api-token-yaml",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = apiSecretYaml
        };

        var namespacedItem = new VaultwardenItem
        {
            Id = "item-2",
            Name = "api-token-managed",
            Type = 1,
            Password = "my-token",
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "test-ns", Type = 0 },
                new() { Name = "secret-name", Value = "api-token", Type = 0 },
                new() { Name = "secret-key-password", Value = "api-key", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { yamlOnlyItem, namespacedItem });

        // ApplyYamlAsync simulates replacing the secret WITHOUT hash annotation
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .Callback<string>(yaml =>
            {
                secretExistsTracker["test-ns/api-token"] = true;
                // Intentionally NOT setting hash annotation — this is what caused the bug
                // The YAML manifest overwrites the entire Secret, annotations included
            })
            .ReturnsAsync(OperationResult.Successful());

        // Namespace sync path setup
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("test-ns"))
            .ReturnsAsync(true);

        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("test-ns", "api-token"))
            .ReturnsAsync(() => secretExistsTracker.GetValueOrDefault("test-ns/api-token"));

        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("test-ns", "api-token"))
            .ReturnsAsync(() => secretAnnotationsTracker.GetValueOrDefault("test-ns/api-token"));

        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("test-ns", "api-token"))
            .ReturnsAsync(() =>
            {
                if (secretExistsTracker.GetValueOrDefault("test-ns/api-token"))
                    return new Dictionary<string, string> { { "api-key", "my-token" } };
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("test-ns", "api-token"))
            .ReturnsAsync(() =>
            {
                if (secretExistsTracker.GetValueOrDefault("test-ns/api-token"))
                    return "Opaque";
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "test-ns", "api-token",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string?>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data,
                Dictionary<string, string> annotations, Dictionary<string, string> labels, string? type) =>
            {
                secretExistsTracker[$"{ns}/{name}"] = true;
                if (annotations != null)
                    secretAnnotationsTracker[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
                "test-ns", "api-token",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data,
                Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    secretAnnotationsTracker[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync("test-ns"))
            .ReturnsAsync(() =>
                secretExistsTracker
                    .Where(kv => kv.Key.StartsWith("test-ns/"))
                    .Select(kv => kv.Key.Split('/')[1])
                    .ToList());

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "test-ns" });

        // DB hash persistence
        var dbHash = "";
        _dbLoggerMock.Setup(x => x.UpdateSecretHashAsync("test-ns", "api-token", It.IsAny<string>()))
            .Callback((string ns, string name, string hash) => { dbHash = hash; })
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.GetSecretHashAsync("test-ns", "api-token"))
            .ReturnsAsync(() => dbHash);

        // Act - First sync
        var firstSync = await _syncService.SyncAsync();

        // Assert - First sync succeeded and created the secret with hash annotation
        firstSync.OverallSuccess.Should().BeTrue();
        secretExistsTracker.GetValueOrDefault("test-ns/api-token").Should().BeTrue();
        secretAnnotationsTracker["test-ns/api-token"]
            .Should().ContainKey(Constants.Kubernetes.HashAnnotationKey,
                "the namespace sync should have written the hash annotation after YAML was applied");

        // Act - Second sync (same items, nothing changed)
        var secondSync = await _syncService.SyncAsync();

        // Assert - Second sync should NOT detect drift (annotation is correct)
        secondSync.OverallSuccess.Should().BeTrue();
        secondSync.HasChanges.Should().BeFalse(
            "second sync should detect no drift because the hash annotation is still correct");
    }

    [Fact]
    public async Task SyncAsync_WhenYamlCreatesSecretInDifferentNamespace_ShouldProcessBothIndependently()
    {
        // YAML creates a Secret in namespace "infra" that the sync does NOT manage,
        // and a namespaced item targets "default".
        // Both should work independently without interference.

        var secretExistsTracker = new Dictionary<string, bool>();
        var secretAnnotationsTracker = new Dictionary<string, Dictionary<string, string>>();

        var infraSecretYaml = @"apiVersion: v1
kind: Secret
metadata:
  name: infra-token
  namespace: infra
type: Opaque
data:
  token: c29tZXRva2Vu";

        var yamlItem = new VaultwardenItem
        {
            Id = "item-1",
            Name = "infra-token-yaml",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = infraSecretYaml
        };

        var namespacedItem = new VaultwardenItem
        {
            Id = "item-2",
            Name = "app-secret",
            Type = 1,
            Password = "app-password",
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { yamlItem, namespacedItem });

        // ApplyYamlAsync creates secret without annotation
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .Callback<string>(yaml => { secretExistsTracker["infra/infra-token"] = true; })
            .ReturnsAsync(OperationResult.Successful());

        // Namespace sync for "default"
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("default"))
            .ReturnsAsync(true);

        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "app-secret"))
            .ReturnsAsync(() => secretExistsTracker.GetValueOrDefault("default/app-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "app-secret"))
            .ReturnsAsync(() => secretAnnotationsTracker.GetValueOrDefault("default/app-secret"));

        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "app-secret"))
            .ReturnsAsync(() =>
            {
                if (secretExistsTracker.GetValueOrDefault("default/app-secret"))
                    return new Dictionary<string, string> { { "password", "app-password" } };
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("default", "app-secret"))
            .ReturnsAsync(() =>
            {
                if (secretExistsTracker.GetValueOrDefault("default/app-secret"))
                    return "Opaque";
                return null;
            });

        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "default", "app-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string?>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data,
                Dictionary<string, string> annotations, Dictionary<string, string> labels, string? type) =>
            {
                secretExistsTracker[$"{ns}/{name}"] = true;
                if (annotations != null)
                    secretAnnotationsTracker[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
                "default", "app-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string ns, string name, Dictionary<string, string> data,
                Dictionary<string, string> annotations, Dictionary<string, string> labels) =>
            {
                if (annotations != null)
                    secretAnnotationsTracker[$"{ns}/{name}"] = new Dictionary<string, string>(annotations);
                return OperationResult.Successful();
            });

        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(It.IsAny<string>()))
            .ReturnsAsync((string ns) =>
                secretExistsTracker
                    .Where(kv => kv.Key.StartsWith($"{ns}/"))
                    .Select(kv => kv.Key.Split('/')[1])
                    .ToList());

        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default", "infra" });

        // DB hash tracking
        var dbHash = "";
        _dbLoggerMock.Setup(x => x.UpdateSecretHashAsync("default", "app-secret", It.IsAny<string>()))
            .Callback((string ns, string name, string hash) => { dbHash = hash; })
            .Returns(Task.CompletedTask);
        _dbLoggerMock.Setup(x => x.GetSecretHashAsync("default", "app-secret"))
            .ReturnsAsync(() => dbHash);

        var syncService = _syncService;

        // Act - First sync
        var firstSync = await syncService.SyncAsync();

        // Assert
        firstSync.OverallSuccess.Should().BeTrue();
        secretExistsTracker.GetValueOrDefault("infra/infra-token").Should().BeTrue();
        secretExistsTracker.GetValueOrDefault("default/app-secret").Should().BeTrue();
        secretAnnotationsTracker["default/app-secret"]
            .Should().ContainKey(Constants.Kubernetes.HashAnnotationKey);

        // Act - Second sync: nothing changed
        var secondSync = await syncService.SyncAsync();

        // Assert - No false drift
        secondSync.OverallSuccess.Should().BeTrue();
        secondSync.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task SyncAsync_YamlIsAppliedBeforeNamespaceSync_TestOrdering()
    {
        // Verify that ApplyYamlAsync is called BEFORE CreateSecretAsync/UpdateSecretAsync.
        // This is the key behavioral change that fixes the false-drift bug.

        var callOrder = new List<string>();

        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .Callback(() => callOrder.Add("ApplyYaml"))
            .ReturnsAsync(OperationResult.Successful());

        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                "test-ns", "ordering-secret",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string?>()))
            .Callback(() => callOrder.Add("CreateSecret"))
            .ReturnsAsync(OperationResult.Successful());

        var yamlItem = new VaultwardenItem
        {
            Id = "item-1",
            Name = "ordering-yaml",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = @"apiVersion: v1
kind: ConfigMap
metadata:
  name: ordering-cm
  namespace: test-ns
data:
  key: val"
        };

        var namespacedItem = new VaultwardenItem
        {
            Id = "item-2",
            Name = "ordering-secret",
            Type = 1,
            Password = "test-pass",
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "test-ns", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { yamlItem, namespacedItem });

        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("test-ns"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("test-ns", "ordering-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("test-ns", "ordering-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("test-ns", "ordering-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("test-ns", "ordering-secret"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync("test-ns"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("test-ns"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "test-ns" });

        var syncService = _syncService;

        await syncService.SyncAsync();

        // ApplyYaml should come before CreateSecret
        var applyYamlIndex = callOrder.IndexOf("ApplyYaml");
        var createSecretIndex = callOrder.IndexOf("CreateSecret");
        Assert.True(applyYamlIndex >= 0, "ApplyYaml should have been called");
        Assert.True(createSecretIndex >= 0, "CreateSecret should have been called");
        Assert.True(applyYamlIndex < createSecretIndex,
            $"ApplyYamlAsync should be called BEFORE CreateSecretAsync. Order was: {string.Join(" -> ", callOrder)}");
    }
}
