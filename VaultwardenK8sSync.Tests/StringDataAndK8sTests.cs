using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Configuration;
using Xunit;
using VaultwardenK8sSync;
using System.Text;
using FluentAssertions;
using FieldInfo = VaultwardenK8sSync.Models.FieldInfo;

namespace VaultwardenK8sSync.Tests;

public class StringDataAndK8sTests : IDisposable
{
    private readonly SyncService _syncService;
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock;
    private readonly SyncSettings _syncConfig;

    public StringDataAndK8sTests()
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
        
        _syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            _syncConfig,
            new DockerConfigJsonSettings());
    }

    private async Task<Dictionary<string, string>> ExtractSecretDataAsync(VaultwardenItem item)
    {
        var method = typeof(SyncService).GetMethod("ExtractSecretDataAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Dictionary<string, string>)await (Task<Dictionary<string, string>>)method!.Invoke(_syncService, new object[] { item, "Opaque", null })!;
    }

    private async Task<(Dictionary<string, string> data, List<string> yamlManifests)> ExtractSecretDataWithYamlAsync(VaultwardenItem item)
    {
        var yamlManifests = new List<string>();
        var method = typeof(SyncService).GetMethod("ExtractSecretDataAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var data = (Dictionary<string, string>)await (Task<Dictionary<string, string>>)method!.Invoke(_syncService, new object[] { item, "Opaque", yamlManifests })!;
        return (data, yamlManifests);
    }

    #region stringData: Mode Tests

    [Fact]
    public async Task ExtractSecretDataAsync_WithNoteStartingWithStringData_ShouldParseKeyValuePairs()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "stringData-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "stringData:\nKEY1=value1\nKEY2: value2\nKEY3 = value3",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("KEY1", result.Keys);
        Assert.Contains("KEY2", result.Keys);
        Assert.Contains("KEY3", result.Keys);
        Assert.Equal("value1", result["KEY1"]);
        Assert.Equal("value2", result["KEY2"]);
        Assert.Equal("value3", result["KEY3"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithNoteStartingWithStringData_ShouldSkipComments()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "stringData-comments",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "stringData:\n# This is a comment\nKEY=value",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.DoesNotContain("# This is a comment", result.Keys);
        Assert.Contains("KEY", result.Keys);
        Assert.Equal("value", result["KEY"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithNoteStartingWithStringData_ShouldStopAtNextSection()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "stringData-sections",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "stringData:\nKEY1=value1\nOtherSection:\nKEY2=value2",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("KEY1", result.Keys);
        Assert.DoesNotContain("KEY2", result.Keys);
    }

    #endregion

    #region Kubernetes YAML Detection Tests

    [Fact]
    public void IsKubernetesYaml_WithValidK8sManifest_ShouldReturnTrue()
    {
        var yaml = @"apiVersion: v1
kind: ConfigMap
metadata:
  name: test
data:
  key: value";

        var result = SyncService.IsKubernetesYaml(yaml);
        Assert.True(result);
    }

    [Fact]
    public void IsKubernetesYaml_WithMultiDocumentYaml_ShouldReturnTrue()
    {
        var yaml = @"---
apiVersion: v1
kind: ConfigMap
metadata:
  name: test1
---
apiVersion: v1
kind: Secret
metadata:
  name: test2";

        var result = SyncService.IsKubernetesYaml(yaml);
        Assert.True(result);
    }

    [Fact]
    public void IsKubernetesYaml_WithInvalidYaml_ShouldReturnFalse()
    {
        var yaml = "This is not YAML";
        var result = SyncService.IsKubernetesYaml(yaml);
        Assert.False(result);
    }

    [Fact]
    public void IsKubernetesYaml_WithYamlMissingApiVersion_ShouldReturnFalse()
    {
        var yaml = @"kind: ConfigMap
metadata:
  name: test";

        var result = SyncService.IsKubernetesYaml(yaml);
        Assert.False(result);
    }

    [Fact]
    public void IsKubernetesYaml_WithYamlMissingKind_ShouldReturnFalse()
    {
        var yaml = @"apiVersion: v1
metadata:
  name: test";

        var result = SyncService.IsKubernetesYaml(yaml);
        Assert.False(result);
    }

    [Fact]
    public void IsKubernetesYaml_WithEmptyString_ShouldReturnFalse()
    {
        var result = SyncService.IsKubernetesYaml("");
        Assert.False(result);
    }

    [Fact]
    public void IsKubernetesYaml_WithNull_ShouldReturnFalse()
    {
        var result = SyncService.IsKubernetesYaml(null);
        Assert.False(result);
    }

    #endregion

    #region SSH Key Tests

    [Fact]
    public async Task ExtractSecretDataAsync_WithSshKeyItem_ShouldUsePrivateKeyAsDefaultKey()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "ssh-key-test",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-rsa AAAA...",
                Fingerprint = "SHA256:test"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("private-key", result.Keys);
        Assert.Contains("public-key", result.Keys);
        Assert.Contains("fingerprint", result.Keys);
        Assert.Contains("BEGIN OPENSSH PRIVATE KEY", result["private-key"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithSshKeyItemAndCustomPasswordKey_ShouldUseCustomKey()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "ssh-key-custom",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "private-key-content",
                PublicKey = "public-key-content",
                Fingerprint = "fingerprint"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "SSH_PRIVATE_KEY", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("SSH_PRIVATE_KEY", result.Keys);
        Assert.DoesNotContain("private-key", result.Keys);
        Assert.Equal("private-key-content", result["SSH_PRIVATE_KEY"]);
    }

    #endregion

    #region ParseStringDataLine Tests

    [Fact]
    public void ParseStringDataLine_WithEqualsSeparator_ShouldParseCorrectly()
    {
        var data = new Dictionary<string, string>();
        var method = typeof(SyncService).GetMethod("ParseStringDataLine", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        method!.Invoke(null, new object[] { "KEY=value", data });

        Assert.Contains("KEY", data.Keys);
        Assert.Equal("value", data["KEY"]);
    }

    [Fact]
    public void ParseStringDataLine_WithColonSeparator_ShouldParseCorrectly()
    {
        var data = new Dictionary<string, string>();
        var method = typeof(SyncService).GetMethod("ParseStringDataLine", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        method!.Invoke(null, new object[] { "KEY: value", data });

        Assert.Contains("KEY", data.Keys);
        Assert.Equal("value", data["KEY"]);
    }

    [Fact]
    public void ParseStringDataLine_WithEmptyLine_ShouldNotAdd()
    {
        var data = new Dictionary<string, string>();
        var method = typeof(SyncService).GetMethod("ParseStringDataLine", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        method!.Invoke(null, new object[] { "", data });

        Assert.Empty(data);
    }

    [Fact]
    public void ParseStringDataLine_WithCommentLine_ShouldNotAdd()
    {
        var data = new Dictionary<string, string>();
        var method = typeof(SyncService).GetMethod("ParseStringDataLine", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        method!.Invoke(null, new object[] { "# comment", data });

        Assert.Empty(data);
    }

    [Fact]
    public void ParseStringDataLine_WithDuplicateKey_ShouldNotOverwrite()
    {
        var data = new Dictionary<string, string> { { "KEY", "original" } };
        var method = typeof(SyncService).GetMethod("ParseStringDataLine", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        method!.Invoke(null, new object[] { "KEY=newvalue", data });

        Assert.Equal("original", data["KEY"]);
    }

    #endregion

    #region stringData with Multiline YAML Tests

    [Fact]
    public async Task ExtractSecretDataAsync_WithNoteStringDataContainingMultilineYaml_ShouldParseCorrectly()
    {
        var notesContent = @"stringData:
  config.yaml: |
    consoles:
      - name: ""EU Console""
        url: ""https://euce1-example.bla.net""
        token: ""REPLACE_ME_EU_TOKEN""
      - name: ""US Console""
        url: ""https://usea1-example.bla.net""
        token: ""REPLACE_ME_US_TOKEN""
  logging.yaml: |
     logging: DEBUG";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "multiline-yaml-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = notesContent,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("config.yaml", result.Keys);
        Assert.Contains("logging.yaml", result.Keys);
        Assert.Contains("consoles:", result["config.yaml"]);
        Assert.Contains("EU Console", result["config.yaml"]);
        Assert.Contains("US Console", result["config.yaml"]);
        Assert.Contains("logging: DEBUG", result["logging.yaml"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithNoteStringDataMultilineYaml_ShouldPreserveIndentation()
    {
        var notesContent = @"stringData:
  config.yaml: |
    level1:
      level2:
        level3: value";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "indentation-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = notesContent,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("config.yaml", result.Keys);
        Assert.Contains("level1:", result["config.yaml"]);
        Assert.Contains("level2:", result["config.yaml"]);
        Assert.Contains("level3: value", result["config.yaml"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithNoteStringDataMultilineYaml_EdgeCaseEmptyValue()
    {
        var notesContent = @"stringData:
  empty.yaml: |
  notempty.yaml: |
    key: value";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "empty-value-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = notesContent,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("empty.yaml", result.Keys);
        Assert.Contains("notempty.yaml", result.Keys);
    }

    #endregion

    #region Attachment Tests

    [Fact]
    public async Task ExtractSecretDataAsync_WithYamlAttachment_ShouldNotExtractSecretData()
    {
        var yamlContent = @"apiVersion: v1
kind: Secret
metadata:
  name: s1-report-config
type: Opaque
stringData:
  config.yaml: |
    consoles:
      - name: ""EU Console""
        url: ""https://euce1-example.bla.net""
        token: ""REPLACE_ME_EU_TOKEN""
      - name: ""US Console""
        url: ""https://usea1-example.bla.net""
        token: ""REPLACE_ME_US_TOKEN""
  logging.yaml: |
     logging: DEBUG";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "attachment-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "Some notes",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "attach-1", FileName = "config.yaml", Size = yamlContent.Length, Url = "/api/ciphers/test-id/attachment/attach-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-1"))
            .ReturnsAsync(Encoding.UTF8.GetBytes(yamlContent));

        var result = await ExtractSecretDataAsync(item);

        // Secret YAML attachments are NOT extracted into secret data
        Assert.DoesNotContain("config.yaml", result.Keys);
        Assert.DoesNotContain("logging.yaml", result.Keys);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithStringDataAttachment_ShouldParseKeyValuePairs()
    {
        var attachmentContent = @"stringData:
  config.yaml: |
    consoles:
      - name: ""EU Console""
        url: ""https://euce1-example.bla.net""
        token: ""REPLACE_ME_EU_TOKEN""
  logging.yaml: |
     logging: DEBUG";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "stringdata-attachment-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "Some notes",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "attach-1", FileName = "data.txt", Size = attachmentContent.Length, Url = "/api/ciphers/test-id/attachment/attach-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-1"))
            .ReturnsAsync(Encoding.UTF8.GetBytes(attachmentContent));

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("config.yaml", result.Keys);
        Assert.Contains("logging.yaml", result.Keys);
        Assert.Contains("consoles:", result["config.yaml"]);
        Assert.Contains("logging: DEBUG", result["logging.yaml"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithRegularAttachment_ShouldStoreWithFileName()
    {
        var fileContent = "This is plain text content";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "regular-attachment-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "Some notes",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "attach-1", FileName = "readme.txt", Size = fileContent.Length, Url = "/api/ciphers/test-id/attachment/attach-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-1"))
            .ReturnsAsync(Encoding.UTF8.GetBytes(fileContent));

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("readme.txt", result.Keys);
        Assert.Equal("This is plain text content", result["readme.txt"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithMultipleAttachments_ShouldProcessAll()
    {
        var yamlContent = "apiVersion: v1\nkind: ConfigMap";
        var textContent = "Some text";
        var stringDataContent = "stringData:\nKEY=value";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "multi-attachment-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "Some notes",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "attach-1", FileName = "config.yaml", Size = yamlContent.Length, Url = "/api/ciphers/test-id/attachment/attach-1" },
                new AttachmentInfo { Id = "attach-2", FileName = "readme.txt", Size = textContent.Length, Url = "/api/ciphers/test-id/attachment/attach-2" },
                new AttachmentInfo { Id = "attach-3", FileName = "data.txt", Size = stringDataContent.Length, Url = "/api/ciphers/test-id/attachment/attach-3" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-1")).ReturnsAsync(Encoding.UTF8.GetBytes(yamlContent));
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-2")).ReturnsAsync(Encoding.UTF8.GetBytes(textContent));
        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-3")).ReturnsAsync(Encoding.UTF8.GetBytes(stringDataContent));

        var result = await ExtractSecretDataAsync(item);

        Assert.DoesNotContain("__yaml_attachment__config.yaml", result.Keys);
        Assert.Contains("readme.txt", result.Keys);
        Assert.Contains("KEY", result.Keys);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithAttachmentDownloadFailure_ShouldContinueProcessing()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "failed-attachment-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "Some notes",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "attach-1", FileName = "failed.yaml", Size = 100, Url = "/api/ciphers/test-id/attachment/attach-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-1"))
            .ReturnsAsync((byte[]?)null);

        var result = await ExtractSecretDataAsync(item);

        Assert.DoesNotContain("__yaml_attachment__failed.yaml", result.Keys);
        Assert.DoesNotContain("failed.yaml", result.Keys);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithYmlAttachment_ShouldNotStoreInData()
    {
        var yamlContent = "apiVersion: v1\nkind: ConfigMap";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "yml-attachment-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "Some notes",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "attach-1", FileName = "config.yml", Size = yamlContent.Length, Url = "/api/ciphers/test-id/attachment/attach-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-1"))
            .ReturnsAsync(Encoding.UTF8.GetBytes(yamlContent));

        var result = await ExtractSecretDataAsync(item);

        Assert.DoesNotContain("__yaml_attachment__config.yml", result.Keys);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithTxtAttachmentContainingK8sYaml_ShouldApplyAsManifest()
    {
        var yamlContent = @"apiVersion: v1
kind: ConfigMap
metadata:
  name: test-config
  namespace: default
data:
  key: value";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "txt-yaml-test",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = "Some notes",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "attach-1", FileName = "teste.txt", Size = yamlContent.Length, Url = "/api/ciphers/test-id/attachment/attach-1" }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.DownloadAttachmentAsync("/api/ciphers/test-id/attachment/attach-1"))
            .ReturnsAsync(Encoding.UTF8.GetBytes(yamlContent));

        var (result, yamlManifests) = await ExtractSecretDataWithYamlAsync(item);

        Assert.Single(yamlManifests);
        Assert.Equal(yamlContent, yamlManifests[0]);
        Assert.DoesNotContain("teste.txt", result.Keys);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ExtractSecretDataAsync_WithStringDataNoteWithSpacesBeforeHeader_ShouldParse()
    {
        var notesContent = @"   
   stringData:
KEY=value";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "spaces-before-header",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = notesContent,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("KEY", result.Keys);
        Assert.Equal("value", result["KEY"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithStringDataMixedCaseHeader_ShouldParse()
    {
        var notesContent = @"STRINGDATA:
KEY=value";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "mixed-case-header",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = notesContent,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("KEY", result.Keys);
        Assert.Equal("value", result["KEY"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithStringDataInlineValues_ShouldParse()
    {
        var notesContent = @"stringData: KEY1=value1
KEY2=value2";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "inline-values",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = notesContent,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("KEY1", result.Keys);
        Assert.Contains("KEY2", result.Keys);
        Assert.Equal("value1", result["KEY1"]);
        Assert.Equal("value2", result["KEY2"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithStringDataValueContainingEquals_ShouldParseCorrectly()
    {
        var notesContent = @"stringData:
CONNECTION_STRING=Server=localhost;Database=mydb;User=admin";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "equals-in-value",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = notesContent,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("CONNECTION_STRING", result.Keys);
        Assert.Equal("Server=localhost;Database=mydb;User=admin", result["CONNECTION_STRING"]);
    }

    [Fact]
    public async Task ExtractSecretDataAsync_WithStringDataValueContainingColon_ShouldParseCorrectly()
    {
        var notesContent = @"stringData:
TIME: 12:30:45";

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "colon-in-value",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = notesContent,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.Contains("TIME", result.Keys);
        Assert.Equal("12:30:45", result["TIME"]);
    }

    [Fact]
    public void IsKubernetesYaml_WithMultiDocumentYamlWithDashes_ShouldReturnTrue()
    {
        var yaml = @"---
apiVersion: v1
kind: ConfigMap
metadata:
  name: test1
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: test2";

        var result = SyncService.IsKubernetesYaml(yaml);
        Assert.True(result);
    }

    [Fact]
    public void IsKubernetesYaml_WithNonK8sYaml_ShouldReturnFalse()
    {
        var yaml = @"name: John
age: 30
city: New York";

        var result = SyncService.IsKubernetesYaml(yaml);
        Assert.False(result);
    }

    [Fact]
    public void IsKubernetesYaml_WithInvalidYamlSyntax_ShouldReturnFalse()
    {
        var yaml = @"apiVersion: v1
kind: ConfigMap
  invalid: indentation: here";

        var result = SyncService.IsKubernetesYaml(yaml);
        Assert.False(result);
    }

    #endregion

    public void Dispose()
    {
    }

    #region Context-Name Filtering Tests

    [Fact]
    public async Task ExtractSecretDataAsync_WithContextNameField_ShouldNotIncludeInSecretData()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "context-test",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "admin",
                Password = "secret123"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 },
                new FieldInfo { Name = "context-name", Value = "production", Type = 0 }
            }
        };

        var result = await ExtractSecretDataAsync(item);

        Assert.DoesNotContain("context-name", result.Keys);
        Assert.Contains("username", result.Keys);
        Assert.Contains("password", result.Keys);
    }

    #endregion

    #region Context Name Extraction Tests

    [Fact]
    public void ExtractContextName_WithContextNameField_ShouldExtractValue()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "test-item",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "context-name", Value = "production", Type = 0 }
            }
        };

        var result = item.ExtractContextName();

        Assert.Equal("production", result);
    }

    [Fact]
    public void ExtractContextName_WithNoContextField_ShouldReturnNull()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "test-item",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        var result = item.ExtractContextName();

        Assert.Null(result);
    }

    [Fact]
    public void ExtractContextName_WithEmptyValue_ShouldReturnNullOrEmpty()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "test-item",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "context-name", Value = "", Type = 0 }
            }
        };

        var result = item.ExtractContextName();

        Assert.True(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void ExtractContextName_WithNoFields_ShouldReturnNull()
    {
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "test-item"
        };

        var result = item.ExtractContextName();

        Assert.Null(result);
    }

    #endregion

    #region Kubernetes Context Detection Tests

    [Fact]
    public void GetContextName_WhenSet_ShouldReturnDetectedContext()
    {
        var k8sServiceMock = new Mock<IKubernetesService>();
            k8sServiceMock.Setup(x => x.IsInitialized).Returns(true);
        
        k8sServiceMock.Setup(x => x.GetContextName())
            .Returns("production-us-east");
        
        var result = k8sServiceMock.Object.GetContextName();
        
        Assert.Equal("production-us-east", result);
    }

    [Fact]
    public void GetContextName_WhenNull_ShouldReturnNull()
    {
        var k8sServiceMock = new Mock<IKubernetesService>();
            k8sServiceMock.Setup(x => x.IsInitialized).Returns(true);
        
        k8sServiceMock.Setup(x => x.GetContextName())
            .Returns((string?)null);
        
        var result = k8sServiceMock.Object.GetContextName();
        
        Assert.Null(result);
    }

    #endregion

    #region Context Filtering Logic Tests

    [Fact]
    public async Task SyncService_ShouldFilterByContextName_WhenConfigured()
    {
        var syncConfigWithContext = new SyncSettings
        {
            ContextName = "production"
        };
        
        _kubernetesServiceMock.Setup(x => x.GetContextName())
            .Returns("production");
        
        var syncServiceWithContext = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _dbLoggerMock.Object,
            syncConfigWithContext,
            new DockerConfigJsonSettings());
        
        var itemWithMatchingContext = new VaultwardenItem
        {
            Id = "test-1",
            Name = "item-1",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "context-name", Value = "production", Type = 0 },
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };
        
        var itemWithDifferentContext = new VaultwardenItem
        {
            Id = "test-2",
            Name = "item-2",
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "context-name", Value = "staging", Type = 0 },
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { itemWithMatchingContext, itemWithDifferentContext });
        _kubernetesServiceMock.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        _kubernetesServiceMock.Setup(x => x.NamespaceExistsAsync("default"))
            .ReturnsAsync(true);
        _kubernetesServiceMock.Setup(x => x.GetExistingSecretNamesAsync("default"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.GetManagedSecretNamesAsync("default"))
            .ReturnsAsync(new List<string>());
        _kubernetesServiceMock.Setup(x => x.SecretExistsAsync("default", "item-1"))
            .ReturnsAsync(false);
        _kubernetesServiceMock.Setup(x => x.GetSecretDataAsync("default", "item-1"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretAnnotationsAsync("default", "item-1"))
            .ReturnsAsync((Dictionary<string, string>?)null);
        _kubernetesServiceMock.Setup(x => x.GetSecretTypeAsync("default", "item-1"))
            .ReturnsAsync((string?)null);
        _kubernetesServiceMock.Setup(x => x.CreateSecretAsync(
            "default", "item-1", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Successful());

        var result = await syncServiceWithContext.SyncAsync();

        result.OverallSuccess.Should().BeTrue();
        result.TotalSecretsCreated.Should().Be(1);
        _kubernetesServiceMock.Verify(x => x.CreateSecretAsync(
            "default", "item-1", It.IsAny<Dictionary<string, string>>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<string>()),
            Times.Once);
    }

    #endregion

    #region Hash Stability Tests

    [Fact]
    public void CalculateItemHash_SameItem_ShouldProduceIdenticalHash()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-1",
            Name = "test-secret",
            Type = 1,
            RevisionDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Password = "secret-password",
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Notes = "some notes",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "custom-field", Value = "custom-value", Type = 0 }
            },
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "att-1", FileName = "config.yaml", Size = 1024 }
            }
        };

        // Act - compute hash twice
        var hash1 = ComputeItemHash(item);
        var hash2 = ComputeItemHash(item);

        // Assert
        hash1.Should().Be(hash2, "hash should be deterministic for the same item");
    }

    [Fact]
    public void CalculateItemHash_SameItemWithAttachments_ShouldProduceIdenticalHash()
    {
        var item = new VaultwardenItem
        {
            Id = "test-2",
            Name = "test-secret-2",
            Type = 2, // Secure note
            RevisionDate = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            Notes = "stringData:\n  key1: value1\n  key2: value2",
            Attachments = new List<AttachmentInfo>
            {
                new AttachmentInfo { Id = "att-1", FileName = "config.yaml", Size = 512 },
                new AttachmentInfo { Id = "att-2", FileName = "data.txt", Size = 256 }
            }
        };

        var hash1 = ComputeItemHash(item);
        var hash2 = ComputeItemHash(item);
        var hash3 = ComputeItemHash(item);

        hash1.Should().Be(hash2);
        hash2.Should().Be(hash3);
    }

    [Fact]
    public void CalculateItemHash_NullAttachmentsVsEmptyList_ShouldProduceSameHash()
    {
        var itemWithNull = new VaultwardenItem
        {
            Id = "test-3",
            Name = "test-secret-3",
            Type = 1,
            RevisionDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Password = "pass",
            Attachments = null
        };

        var itemWithEmpty = new VaultwardenItem
        {
            Id = "test-3",
            Name = "test-secret-3",
            Type = 1,
            RevisionDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Password = "pass",
            Attachments = new List<AttachmentInfo>()
        };

        var hashNull = ComputeItemHash(itemWithNull);
        var hashEmpty = ComputeItemHash(itemWithEmpty);

        hashNull.Should().Be(hashEmpty, "null vs empty attachments should produce same hash");
    }

    [Fact]
    public void CalculateItemHash_SameContentDifferentRevisionDate_ShouldProduceSameHash()
    {
        // RevisionDate is intentionally excluded from hashing because Vaultwarden
        // updates it on metadata changes even when secret content hasn't changed.
        // Two items with identical content but different RevisionDate should hash the same.
        var item1 = new VaultwardenItem
        {
            Id = "test-4",
            Name = "test-secret-4",
            Type = 1,
            RevisionDate = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Password = "pass"
        };

        var item2 = new VaultwardenItem
        {
            Id = "test-4",
            Name = "test-secret-4",
            Type = 1,
            RevisionDate = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc), // 1 second later
            Password = "pass"
        };

        var hash1 = ComputeItemHash(item1);
        var hash2 = ComputeItemHash(item2);

        hash1.Should().Be(hash2, "identical content with different revision dates should produce the same hash");
    }

    private static string ComputeItemHash(VaultwardenItem item)
    {
        var method = typeof(SyncService).GetMethod("CalculateItemHash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, new object[] { item })!;
    }

    #endregion

}
