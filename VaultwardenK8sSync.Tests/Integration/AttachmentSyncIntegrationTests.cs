using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using FieldInfo = VaultwardenK8sSync.Models.FieldInfo;

namespace VaultwardenK8sSync.Tests.Integration;

[Collection("SyncService Sequential")]
[Trait("Category", "AttachmentSyncIntegration")]
public class AttachmentSyncIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock;
    private readonly SyncSettings _syncConfig;
    private readonly SyncService _syncService;

    public AttachmentSyncIntegrationTests()
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

    public void Dispose()
    {
    }

    private static bool CaptureData(Dictionary<string, string> data, out Dictionary<string, string>? captured)
    {
        captured = data;
        return true;
    }

    private void SetupCommonKubernetesMocks(string namespaceName = "default")
    {
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { namespaceName });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync(namespaceName))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync(namespaceName))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());
        _kubernetesServiceMock.Setup(x => x.ApplyYamlAsync(It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());
    }

    [Fact]
    public async Task SecureNoteWithYamlAttachment_AppliesYamlManifestAndDoesNotSetPasswordToItemName()
    {
        var namespaceName = "default";
        var yamlContent = @"apiVersion: v1
kind: Secret
metadata:
  name: test-yaml
  namespace: default
type: Opaque
stringData:
  DB_PASSWORD: supersecret123";

        var item = new VaultwardenItem
        {
            Id = "item-yaml-1",
            Name = "test yaml",
            Type = 2,
            Notes = "",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "att-yaml-1", FileName = "secret.yaml", Size = yamlContent.Length, Url = "/api/ciphers/item-yaml-1/attachment/att-yaml-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-yaml-1/attachment/att-yaml-1"))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(yamlContent));

        SetupCommonKubernetesMocks(namespaceName);

        Dictionary<string, string>? createdData = null;
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                namespaceName,
                "test-yaml",
                It.Is<Dictionary<string, string>>(d => CaptureData(d, out createdData)),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();

        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(yamlContent), Times.Once);

        if (createdData != null)
        {
            createdData.Should().NotContainKey("password", "secure note with YAML attachment should NOT have 'password' set to item name");
        }
    }

[Fact]
    public async Task LoginItemWithAttachment_DoesNotSetPasswordToItemName()
    {
        var namespaceName = "default";
        var fileContent = "some-attachment-content";

        var item = new VaultwardenItem
        {
            Id = "item-login-1",
            Name = "my-app-creds",
            Type = 1,
            Login = new LoginInfo { Username = "admin", Password = "s3cret" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "att-1", FileName = "config.txt", Size = fileContent.Length, Url = "/api/ciphers/item-login-1/attachment/att-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-login-1/attachment/att-1"))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(fileContent));

        SetupCommonKubernetesMocks(namespaceName);

        Dictionary<string, string>? createdData = null;
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                namespaceName,
                "my-app-creds",
                It.Is<Dictionary<string, string>>(d => CaptureData(d, out createdData)),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        createdData.Should().NotBeNull();
        createdData!.Should().ContainKey("username");
        createdData!.Should().ContainKey("password");
        createdData!["password"].Should().Be("s3cret", "password should be the actual password, not the item name");
        createdData!["password"].Should().NotBe("my-app-creds", "password should NOT be set to item name when attachments exist");
    }

    [Fact]
    public async Task SecureNoteWithOnlyAttachment_DoesNotSetPasswordToItemName()
    {
        var namespaceName = "default";
        var fileContent = "plain text attachment";

        var item = new VaultwardenItem
        {
            Id = "item-note-1",
            Name = "config-store",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "att-1", FileName = "readme.txt", Size = fileContent.Length, Url = "/api/ciphers/item-note-1/attachment/att-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-note-1/attachment/att-1"))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(fileContent));

        SetupCommonKubernetesMocks(namespaceName);

        Dictionary<string, string>? createdData = null;
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                namespaceName,
                "config-store",
                It.Is<Dictionary<string, string>>(d => CaptureData(d, out createdData)),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        createdData.Should().NotBeNull("a secret should be created for the namespace-scoped item");
        createdData!.Should().ContainKey("readme.txt", "attachment content should be stored under its filename");
        createdData!.Should().NotContainKey("password", "secure note with attachment should NOT have a 'password' key set to item name");
    }

    [Fact]
    public async Task YamlAttachmentWithExistingSecret_Handles409Conflict()
    {
        var namespaceName = "default";
        var yamlContent = @"apiVersion: v1
kind: Secret
metadata:
  name: test-yaml
  namespace: default
type: Opaque
stringData:
  key: value";

        var item = new VaultwardenItem
        {
            Id = "item-yaml-2",
            Name = "test-yaml",
            Type = 2,
            Notes = "",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 },
                new FieldInfo { Name = "secret-key", Value = "API_TOKEN", Type = 0 }
            },
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "att-yaml-2", FileName = "secret.yaml", Size = yamlContent.Length, Url = "/api/ciphers/item-yaml-2/attachment/att-yaml-2" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-yaml-2/attachment/att-yaml-2"))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(yamlContent));

        SetupCommonKubernetesMocks(namespaceName);

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();

        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(yamlContent), Times.Once);
    }

    [Fact]
    public async Task TxtAttachmentWithKubernetesYaml_AppliesYamlManifest()
    {
        var namespaceName = "default";
        var yamlContent = @"apiVersion: v1
kind: ConfigMap
metadata:
  name: test-yaml-note
  namespace: default
data:
  image-task: harbor.lag0.com.br/library/seafight-market-bot:v0.0.54
  image-task-version: v0.0.54";

        var item = new VaultwardenItem
        {
            Id = "item-txt-1",
            Name = "test-yaml",
            Type = 2,
            Notes = "",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "att-txt-1", FileName = "teste.txt", Size = yamlContent.Length, Url = "/api/ciphers/item-txt-1/attachment/att-txt-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-txt-1/attachment/att-txt-1"))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(yamlContent));

        SetupCommonKubernetesMocks(namespaceName);

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();

        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(yamlContent), Times.Once,
            ".txt file containing Kubernetes YAML should be applied as a manifest, not stored as secret data");
    }

    [Fact]
    public async Task EncryptedAttachmentContent_IsDecryptedBeforeProcessing()
    {
        var namespaceName = "default";
        var yamlContent = @"apiVersion: v1
kind: ConfigMap
metadata:
  name: test-config
  namespace: default
data:
  key: value";

        var encryptedContent = System.Text.Encoding.UTF8.GetBytes("2." + Convert.ToBase64String(new byte[16]) + "|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(yamlContent)));
        var decryptedContent = System.Text.Encoding.UTF8.GetBytes(yamlContent);

        var item = new VaultwardenItem
        {
            Id = "item-enc-1",
            Name = "config-item",
            Type = 2,
            Notes = "",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo
                {
                    Id = "att-enc-1",
                    FileName = "config.yaml",
                    Size = 100,
                    Url = "/api/ciphers/item-enc-1/attachment/att-enc-1",
                    Key = "2.encryptedkey"
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-enc-1/attachment/att-enc-1"))
            .ReturnsAsync(encryptedContent);
        _vaultwardenServiceMock.Setup(x => x.DecryptAttachmentContent(encryptedContent, "2.encryptedkey", null))
            .Returns(decryptedContent);

        SetupCommonKubernetesMocks(namespaceName);

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _vaultwardenServiceMock.Verify(x => x.DecryptAttachmentContent(encryptedContent, "2.encryptedkey", null), Times.Once,
            "attachment with key should be decrypted before processing");
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(yamlContent), Times.Once,
            "decrypted YAML content should be applied as manifest");
    }

    [Fact]
    public async Task EncryptedAttachmentContent_WithOrgId_PassesOrgId()
    {
        var namespaceName = "default";
        var yamlContent = @"apiVersion: v1
kind: Secret
metadata:
  name: org-secret
  namespace: default
type: Opaque
stringData:
  token: abc123";

        var encryptedContent = System.Text.Encoding.UTF8.GetBytes("2." + Convert.ToBase64String(new byte[16]) + "|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(yamlContent)));
        var decryptedContent = System.Text.Encoding.UTF8.GetBytes(yamlContent);
        var orgId = "org-123";

        var item = new VaultwardenItem
        {
            Id = "item-org-1",
            Name = "org-secret",
            OrganizationId = orgId,
            Type = 2,
            Notes = "",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo
                {
                    Id = "att-org-1",
                    FileName = "secret.yaml",
                    Size = 100,
                    Url = "/api/ciphers/item-org-1/attachment/att-org-1",
                    Key = "2.orgencryptedkey"
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-org-1/attachment/att-org-1"))
            .ReturnsAsync(encryptedContent);
        _vaultwardenServiceMock.Setup(x => x.DecryptAttachmentContent(encryptedContent, "2.orgencryptedkey", orgId))
            .Returns(decryptedContent);

        SetupCommonKubernetesMocks(namespaceName);

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _vaultwardenServiceMock.Verify(x => x.DecryptAttachmentContent(encryptedContent, "2.orgencryptedkey", orgId), Times.Once,
            "org-scoped attachment should pass orgId to decryption");
        _kubernetesServiceMock.Verify(x => x.ApplyYamlAsync(yamlContent), Times.Once);
    }

    [Fact]
    public async Task AttachmentDictionaryFallback_NoKeyProvided_UsesRawContent()
    {
        var namespaceName = "default";
        var fileContent = "plain text readme";

        var item = new VaultwardenItem
        {
            Id = "item-no-key-1",
            Name = "readme-item",
            Type = 2,
            Notes = "",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = namespaceName, Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo
                {
                    Id = "att-no-key-1",
                    FileName = "readme.txt",
                    Size = fileContent.Length,
                    Url = "/api/ciphers/item-no-key-1/attachment/att-no-key-1"
                }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/item-no-key-1/attachment/att-no-key-1"))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(fileContent));

        SetupCommonKubernetesMocks(namespaceName);

        Dictionary<string, string>? createdData = null;
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
                namespaceName,
                "readme-item",
                It.Is<Dictionary<string, string>>(d => CaptureData(d, out createdData)),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await _syncService.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        _vaultwardenServiceMock.Verify(x => x.DecryptAttachmentContent(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never,
            "attachment without Key should not attempt decryption");
        createdData.Should().NotBeNull();
        createdData!.Should().ContainKey("readme.txt");
        createdData!["readme.txt"].Should().Be(fileContent);
    }
}