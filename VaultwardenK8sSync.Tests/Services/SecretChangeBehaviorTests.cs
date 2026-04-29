using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using FieldInfo = VaultwardenK8sSync.Models.FieldInfo;

namespace VaultwardenK8sSync.Tests.Services;

[Collection("SyncService Sequential")]
[Trait("Category", "SecretChangeBehavior")]
public class SecretChangeBehaviorTests
{
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock;
    private readonly SyncSettings _syncConfig;
    private readonly SyncService _syncService;
    private const string HashAnnotationKey = Constants.Kubernetes.HashAnnotationKey;

    public SecretChangeBehaviorTests()
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

        _syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig,
            new DockerConfigJsonSettings());
    }

    #region Creation Tests

    [Fact]
    public async Task SecretCreation_FirstSync_CreatesNewSecret()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(1);
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string?>()),
            Times.Once);
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task SecretUpdate_DataChanged_UpdatesSecret()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "newpass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["password"] = "oldpass", ["username"] = "user" };
        var existingAnnotations = new Dictionary<string, string> { [HashAnnotationKey] = "old-hash" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingAnnotations);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsUpdated.Should().Be(1);
        _kubernetesServiceMock.Verify(x => x.UpdateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SecretUpdate_NoChanges_SkipsSecret()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["password"] = "pass", ["username"] = "user" };
        var existingAnnotations = new Dictionary<string, string> { [HashAnnotationKey] = "abc123" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingAnnotations);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsUpdated.Should().Be(1);
    }

    [Fact]
    public async Task SecretUpdate_HashChanged_DataSame_UpdatesAnnotations()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["password"] = "pass", ["username"] = "user" };
        var existingAnnotations = new Dictionary<string, string> { [HashAnnotationKey] = "different-hash" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingAnnotations);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.Is<Dictionary<string, string>>(a => a.ContainsKey(HashAnnotationKey)), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsUpdated.Should().Be(1);
        _kubernetesServiceMock.Verify(x => x.UpdateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.Is<Dictionary<string, string>>(a => a.ContainsKey(HashAnnotationKey)), It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }

    #endregion

    #region Type Change Tests

    [Fact]
    public async Task SecretTypeChange_OpaqueToTLS_DeletesAndRecreates()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "tls-secret",
            Type = 1,
            Login = new LoginInfo { Password = "cert-data" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/tls", Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["password"] = "old" };
        var existingAnnotations = new Dictionary<string, string> { [HashAnnotationKey] = "old-hash" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "tls-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "tls-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "tls-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "tls-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "tls-secret"))
            .ReturnsAsync(existingAnnotations);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "tls-secret"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync(namespaceName, "tls-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "tls-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), "kubernetes.io/tls"))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(namespaceName, "tls-secret"), Times.Once);
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            namespaceName, "tls-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), "kubernetes.io/tls"),
            Times.Once);
    }

    [Fact]
    public async Task SecretTypeChange_TLS_TO_Opaque_DeletesAndRecreates()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "tls-secret",
            Type = 1,
            Login = new LoginInfo { Password = "new-data" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["password"] = "old" };
        var existingAnnotations = new Dictionary<string, string> { [HashAnnotationKey] = "old-hash" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "tls-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "tls-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "tls-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "tls-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "tls-secret"))
            .ReturnsAsync(existingAnnotations);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "tls-secret"))
            .ReturnsAsync("kubernetes.io/tls");
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync(namespaceName, "tls-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "tls-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(namespaceName, "tls-secret"), Times.Once);
    }

    #endregion

    #region Name Change Tests

    [Fact]
    public async Task SecretNameChange_ItemRenamed_DeletesOldCreatesNew()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "new-secret-name",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "new-secret-name"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "new-secret-name"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "new-secret-name"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "new-secret-name"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "new-secret-name", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(1);
    }

    #endregion

    #region Field Change Tests

    [Fact]
    public async Task SecretFieldAdded_NewCustomField_UpdatesSecret()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "NEW_KEY", Value = "new-value", Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["password"] = "pass", ["username"] = "user" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(new Dictionary<string, string>());
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
            namespaceName, "test-secret",
            It.Is<Dictionary<string, string>>(d => d.ContainsKey("NEW_KEY")),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsUpdated.Should().Be(1);
    }

    [Fact]
    public async Task SecretFieldRemoved_CustomFieldDeleted_UpdatesSecret()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["password"] = "pass", ["username"] = "user", ["OLD_KEY"] = "old-value" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(new Dictionary<string, string>());
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
            namespaceName, "test-secret",
            It.Is<Dictionary<string, string>>(d => !d.ContainsKey("OLD_KEY")),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsUpdated.Should().Be(1);
    }

    #endregion

    #region Merging Tests

    [Fact]
    public async Task SecretMerging_MultipleItems_SameSecretName_CreatesMergedSecret()
    {
        var namespaceName = "default";
        var item1 = new VaultwardenItem
        {
            Id = "item-1",
            Name = "item-1",
            Type = 1,
            Login = new LoginInfo { Username = "user1", Password = "pass1" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "merged-secret", Type = 0 }
            }
        };
        var item2 = new VaultwardenItem
        {
            Id = "item-2",
            Name = "item-2",
            Type = 1,
            Login = new LoginInfo { Username = "user2", Password = "pass2" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "merged-secret", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item1, item2 });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "merged-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "merged-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "merged-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "merged-secret"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "merged-secret",
            It.Is<Dictionary<string, string>>(d => d.ContainsKey("username") && d.ContainsKey("password")),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(1);
    }

    [Fact]
    public async Task SecretUnmerging_OneItemRemovedFromMerged_UpdatesWithSingleItemData()
    {
        var namespaceName = "default";
        var item1 = new VaultwardenItem
        {
            Id = "item-1",
            Name = "item-1",
            Type = 1,
            Login = new LoginInfo { Username = "user1", Password = "pass1" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "merged-secret", Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["username"] = "user1", ["password"] = "pass1", ["username_2"] = "user2", ["password_2"] = "pass2" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item1 });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "merged-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "merged-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "merged-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "merged-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "merged-secret"))
            .ReturnsAsync(new Dictionary<string, string>());
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "merged-secret"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
            namespaceName, "merged-secret",
            It.Is<Dictionary<string, string>>(d => !d.ContainsKey("username_2") && !d.ContainsKey("password_2")),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsUpdated.Should().Be(1);
    }

    [Fact]
    public async Task SecretMerging_TypeConflict_ReturnsFailed()
    {
        var namespaceName = "default";
        var item1 = new VaultwardenItem
        {
            Id = "item-1",
            Name = "item-1",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "conflict-secret", Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
            }
        };
        var item2 = new VaultwardenItem
        {
            Id = "item-2",
            Name = "item-2",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "conflict-secret", Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/tls", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item1, item2 });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);

        var result = await _syncService.SyncAsync();

        result.TotalSecretsFailed.Should().Be(1);
    }

    #endregion

    #region YAML and stringData Tests

    [Fact]
    public async Task YAMLAttachmentAdded_SecureNote_AppliesYamlManifest()
    {
        var namespaceName = "default";
        var yamlContent = @"apiVersion: v1
kind: ConfigMap
metadata:
  name: test-config
  namespace: default
data:
  key: value";

        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "yaml-attachment",
            Type = 2,
            Notes = "",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "att-1", FileName = "config.yaml", Url = "/api/ciphers/item-1/attachment/att-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-1/attachment/att-1"))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(yamlContent));
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(yamlContent))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(yamlContent), Times.Once);
    }

    [Fact]
    public async Task StringDataNote_Changed_UpdatesSecretWithParsedData()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "stringdata-note",
            Type = 2,
            Notes = "stringData:\nDATABASE_URL=postgres://localhost\nAPI_KEY=newsecret",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "stringdata-note" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "stringdata-note" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "stringdata-note"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "stringdata-note"))
            .ReturnsAsync(new Dictionary<string, string> { ["DATABASE_URL"] = "old", ["API_KEY"] = "old" });
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "stringdata-note"))
            .ReturnsAsync(new Dictionary<string, string>());
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "stringdata-note"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
            namespaceName, "stringdata-note",
            It.Is<Dictionary<string, string>>(d => d["DATABASE_URL"].Contains("localhost") && d["API_KEY"] == "newsecret"),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsUpdated.Should().Be(1);
    }

    #endregion

    #region Context Filtering Tests

    [Fact]
    public async Task ContextFiltering_ItemMatches_CreatesSecret()
    {
        var syncConfig = new SyncSettings { ContextName = "production" };
        var syncService = CreateSyncServiceWithConfig(syncConfig);

        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "context-name", Value = "production", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetContextName())
            .Returns("production");
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, It.IsAny<string>()))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(1);
    }

    [Fact]
    public async Task ContextFiltering_ItemDoesNotMatch_SkipsSecret()
    {
        var syncConfig = new SyncSettings { ContextName = "production" };
        var syncService = CreateSyncServiceWithConfig(syncConfig);

        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "context-name", Value = "staging", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetContextName())
            .Returns("production");

        var result = await syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(0);
        result.TotalSecretsSkipped.Should().Be(0);
    }

    private SyncService CreateSyncServiceWithConfig(SyncSettings config)
    {
        return new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            config,
            new DockerConfigJsonSettings());
    }

    #endregion

    #region Orphan Cleanup Tests

    [Fact]
    public async Task OrphanCleanup_SecretNoLongerInVault_DeletesSecret()
    {
        var syncConfig = new SyncSettings { DeleteOrphans = true };
        var syncService = CreateSyncServiceWithConfig(syncConfig);

        var namespaceName = "default";

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "orphaned-secret" });
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(namespaceName))
            .ReturnsAsync(new List<string> { "orphaned-secret" });
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync(namespaceName, "orphaned-secret"))
            .ReturnsAsync((bool?)null);
        _kubernetesServiceMock.Setup(x => x.DeleteSecretAsync(namespaceName, "orphaned-secret"))
            .ReturnsAsync(true);

        var result = await syncService.CleanupOrphanedSecretsAsync();

        result.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(namespaceName, "orphaned-secret"), Times.Once);
    }

    [Fact]
    public async Task OrphanCleanup_PreserveExternallyManaged_RemovesKeysOnly()
    {
        var syncConfig = new SyncSettings { DeleteOrphans = true };
        var syncService = CreateSyncServiceWithConfig(syncConfig);

        var namespaceName = "default";

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem>());
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "mixed-secret" });
        _kubernetesServiceMock.Setup(x => x.GetSecretsWithManagedKeysAsync(namespaceName))
            .ReturnsAsync(new List<string> { "mixed-secret" });
        _kubernetesServiceMock.Setup(x => x.RemoveManagedKeysAsync(namespaceName, "mixed-secret"))
            .ReturnsAsync(true);

        var result = await syncService.CleanupOrphanedSecretsAsync();

        result.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _kubernetesServiceMock.Verify(x => x.RemoveManagedKeysAsync(namespaceName, "mixed-secret"), Times.Once);
    }

    #endregion

    #region Namespace and Multi-namespace Tests

    [Fact]
    public async Task MultiNamespace_SameSecret_CreatesInAllNamespaces()
    {
        var namespaceName1 = "ns1";
        var namespaceName2 = "ns2";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = $"{namespaceName1},{namespaceName2}", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName1, namespaceName2 });
        foreach (var ns in new[] { namespaceName1, namespaceName2 })
        {
            _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(ns))
                .ReturnsAsync(true);
            _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(ns))
                .ReturnsAsync(new List<string>());
            _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(ns))
                .ReturnsAsync(new List<string>());
            _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(ns, "test-secret"))
                .ReturnsAsync(false);
            _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(ns, "test-secret"))
                .ReturnsAsync((Dictionary<string, string>?)null);
            _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(ns, "test-secret"))
                .ReturnsAsync((Dictionary<string, string>?)null);
            _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(ns, "test-secret"))
                .ReturnsAsync((string?)null);
            _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                ns, "test-secret", It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
                .ReturnsAsync(OperationResult.Successful());
        }

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(2);
    }

    [Fact]
    public async Task NamespaceDoesNotExist_SecretCreationFails()
    {
        var namespaceName = "nonexistent";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(false);

        var result = await _syncService.SyncAsync();

        result.TotalSecretsFailed.Should().Be(1);
    }

    #endregion

    #region Dry Run and Edge Cases

    [Fact]
    public async Task DryRun_NoKubernetesChanges_CreatesNoSecrets()
    {
        var syncConfig = new SyncSettings { DryRun = true };
        var syncService = CreateSyncServiceWithConfig(syncConfig);

        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);

        var result = await syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(0);
        result.TotalSecretsSkipped.Should().Be(1);
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SecretWithAnnotations_CustomMetadataPreserved()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "app.kubernetes.io/owner=platform-team", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.Is<Dictionary<string, string>>(a => a.ContainsKey("app.kubernetes.io/owner")),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SecretWithLabels_CustomLabelsApplied()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-label", Value = "environment=production", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(),
            It.Is<Dictionary<string, string>>(l => l.ContainsKey("environment")),
            It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateFails_RetryAsCreate()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        var existingData = new Dictionary<string, string> { ["password"] = "old", ["username"] = "user" };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string> { "test-secret" });
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "test-secret"))
            .ReturnsAsync(existingData);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "test-secret"))
            .ReturnsAsync(new Dictionary<string, string>());
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "test-secret"))
            .ReturnsAsync("Opaque");
        _kubernetesServiceMock.Setup(x => x.UpdateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(OperationResult.Failed("not found"));
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            namespaceName, "test-secret", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task MixedSecretType_NonDefaultWins()
    {
        var namespaceName = "default";
        var item1 = new VaultwardenItem
        {
            Id = "item-1",
            Name = "item-1",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "type-test", Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "Opaque", Type = 0 }
            }
        };
        var item2 = new VaultwardenItem
        {
            Id = "item-2",
            Name = "item-2",
            Type = 1,
            Login = new LoginInfo { Username = "user2", Password = "pass2" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "type-test", Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item1, item2 });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "type-test"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "type-test"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "type-test"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "type-test"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "type-test", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), "kubernetes.io/dockerconfigjson"))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(1);
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            namespaceName, "type-test", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), "kubernetes.io/dockerconfigjson"),
            Times.Once);
    }

    [Fact]
    public async Task SSHKeyItem_HydratedAndSynced()
    {
        var namespaceName = "default";
        var item = new VaultwardenItem
        {
            Id = "item-1",
            Name = "ssh-key",
            Type = 5,
            SshKey = null,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            }
        };

        var hydratedItem = new VaultwardenItem
        {
            Id = "item-1",
            Name = "ssh-key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-rsa AAAABtest test@test",
                Fingerprint = "SHA256:abc123"
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.GetItemAsync("item-1"))
            .ReturnsAsync(hydratedItem);
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(namespaceName, "ssh-key"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(namespaceName, "ssh-key"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(namespaceName, "ssh-key"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(namespaceName, "ssh-key"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            namespaceName, "ssh-key",
            It.Is<Dictionary<string, string>>(d => d.ContainsKey("private-key") && d.ContainsKey("public-key") && d.ContainsKey("fingerprint")),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(1);
        _vaultwardenServiceMock.Verify(x => x.GetItemAsync("item-1"), Times.Once);
    }

    #endregion
}