using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Configuration;
using Xunit;
using System.Reflection;
using System.Text;
using FluentAssertions;
using FieldInfo = VaultwardenK8sSync.Models.FieldInfo;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Tests for complex data types and edge cases that production systems may encounter
/// </summary>
[Collection("SyncService Sequential")]
[Trait("Category", "ComplexData")]
public class ComplexDataTests : IDisposable
{
    private readonly SyncService _syncService;
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly Mock<IDatabaseLoggerService> _dbLoggerMock;
    private readonly SyncSettings _syncConfig;

    public ComplexDataTests()
    {
        _loggerMock = new Mock<ILogger<SyncService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
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
            _syncConfig);
    }

    private async Task<Dictionary<string, string>> ExtractSecretDataAsync(VaultwardenItem item)
    {
        var method = typeof(SyncService).GetMethod("ExtractSecretDataAsync", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Dictionary<string, string>)await (Task<Dictionary<string, string>>)method!.Invoke(_syncService, new object[] { item })!;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region Special Characters in Passwords

    [Fact]
    [Trait("Category", "SpecialCharacters")]
    public async Task Password_WithSingleQuotes_ShouldBePreserved()
    {
        // Arrange
        var password = "It's a 'secret' password";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "SpecialCharacters")]
    public async Task Password_WithDoubleQuotes_ShouldBePreserved()
    {
        // Arrange
        var password = "He said \"hello\" and \"goodbye\"";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "SpecialCharacters")]
    public async Task Password_WithBackslashes_ShouldBePreserved()
    {
        // Arrange
        var password = "C:\\Windows\\System32\\config";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "SpecialCharacters")]
    public async Task Password_WithNewlines_ShouldBePreserved()
    {
        // Arrange
        var password = "line1\nline2\nline3";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "SpecialCharacters")]
    public async Task Password_WithBackticks_ShouldBePreserved()
    {
        // Arrange
        var password = "`echo \"not executed\"`";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "SpecialCharacters")]
    public async Task Password_WithDollarSigns_ShouldNotBeExpanded()
    {
        // Arrange
        var password = "$HOME and ${PATH} and $(echo test)";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password, "Environment variables should not be expanded");
    }

    #endregion

    #region Unicode and International Characters

    [Fact]
    [Trait("Category", "Unicode")]
    public async Task Password_WithEmoji_ShouldBePreserved()
    {
        // Arrange
        var password = "🔐 secure_🔑 key_🛡️";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "Unicode")]
    public async Task Password_WithChinese_ShouldBePreserved()
    {
        // Arrange
        var password = "密码123";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "用户",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "Unicode")]
    public async Task Password_WithArabic_ShouldBePreserved()
    {
        // Arrange
        var password = "كلمةالسر123";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "مستخدم",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "Unicode")]
    public async Task Password_WithCyrillic_ShouldBePreserved()
    {
        // Arrange
        var password = "Пароль123";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "пользователь",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Test-Item");
        result["Test-Item"].Should().Be(password);
    }

    #endregion

    #region Multi-line Content (Certificates, Keys)

    [Fact]
    [Trait("Category", "MultiLine")]
    public async Task SecureNote_WithPEMCertificate_ShouldBePreserved()
    {
        // Arrange
        var certificate = @"-----BEGIN CERTIFICATE-----
MIIDXTCCAkWgAwIBAgIJAKL0UG+mRkSvMA0GCSqGSIb3DQEBCwUAMEUxCzAJBgNV
BAYTAkFVMRMwEQYDVQQIDApTb21lLVN0YXRlMSEwHwYDVQQKDBhJbnRlcm5ldCBX
aWRnaXRzIFB0eSBMdGQwHhcNMjAwMTAxMDAwMDAwWhcNMzAwMTAxMDAwMDAwWjBF
MQswCQYDVQQGEwJBVTETMBEGA1UECAwKU29tZS1TdGF0ZTEhMB8GA1UECgwYSW50
ZXJuZXQgV2lkZ2l0cyBQdHkgTHRkMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIB
CgKCAQEA12345678901234567890
-----END CERTIFICATE-----";
        
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "TLS Certificate",
            Type = 2, // Secure Note
            SecureNote = new SecureNoteInfo
            {
                Type = 0
            },
            Notes = certificate,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "cert-type", Value = "TLS", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("TLS-Certificate");
        result["TLS-Certificate"].Should().Contain("BEGIN CERTIFICATE");
        result["TLS-Certificate"].Should().Contain("END CERTIFICATE");
        result["TLS-Certificate"].Should().Contain("\n", "Multi-line should be preserved");
    }

    [Fact]
    [Trait("Category", "MultiLine")]
    public async Task SecureNote_WithPrivateKey_ShouldBePreserved()
    {
        // Arrange
        var privateKey = @"-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC0SJKyS5fNq5uV
k8xRITwQEQPn7yS8rN5zK5VvF0bvGdXPRm7iKTBKpT9QmZV5O8Wy6JyLZJKwVGHm
xFaG3D4qR8qZzD5W3bKJ5xP9qR3W9zY7pE6Nq5uVk8xRITwQEQPn7yS8rN5zK5Vv
F0bvGdXPRm7iKTBKpT9QmZV5O8Wy6JyLZJKwVGHmxFaG3D4qR8qZzD5W3bKJ5xP9
-----END PRIVATE KEY-----";
        
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Private Key",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = privateKey
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Private-Key");
        result["Private-Key"].Should().Contain("BEGIN PRIVATE KEY");
        result["Private-Key"].Should().Contain("END PRIVATE KEY");
    }

    [Fact]
    [Trait("Category", "MultiLine")]
    public async Task SecureNote_WithTrailingNewline_ShouldBePreserved()
    {
        // Arrange
        var noteContent = "content-with-trailing-newline\n";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Trailing Newline Note",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = noteContent
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Trailing-Newline-Note");
        result["Trailing-Newline-Note"].Should().Be(noteContent);
    }

    [Fact]
    [Trait("Category", "MultiLine")]
    public async Task SecureNote_WithLeadingNewline_ShouldBePreserved()
    {
        // Arrange
        var noteContent = "\ncontent-with-leading-newline";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Leading Newline Note",
            Type = 2,
            SecureNote = new SecureNoteInfo { Type = 0 },
            Notes = noteContent
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Leading-Newline-Note");
        result["Leading-Newline-Note"].Should().Be(noteContent);
    }

    #endregion

    #region Large Data Tests

    [Fact]
    [Trait("Category", "LargeData")]
    public async Task Password_With1KBLength_ShouldBeHandled()
    {
        // Arrange
        var password = new string('A', 1024); // 1KB
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Large Password",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Large-Password");
        result["Large-Password"].Should().HaveLength(1024);
        result["Large-Password"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "LargeData")]
    public async Task Password_With10KBLength_ShouldBeHandled()
    {
        // Arrange
        var password = new string('B', 10240); // 10KB
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Very Large Password",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Very-Large-Password");
        result["Very-Large-Password"].Should().HaveLength(10240);
    }

    [Fact]
    [Trait("Category", "LargeData")]
    public async Task SecretData_WithManyCustomFields_ShouldBeHandled()
    {
        // Arrange - Create item with 50 custom fields
        var fields = new List<FieldInfo>();
        for (int i = 0; i < 50; i++)
        {
            fields.Add(new FieldInfo 
            { 
                Name = $"FIELD_{i:D3}", 
                Value = $"value_{i}", 
                Type = 0 
            });
        }

        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Many Fields",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = fields
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().HaveCountGreaterOrEqualTo(50);
        result.Should().ContainKey("FIELD_000");
        result.Should().ContainKey("FIELD_049");
        result["FIELD_000"].Should().Be("value_0");
        result["FIELD_049"].Should().Be("value_49");
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait("Category", "EdgeCases")]
    public async Task Password_Empty_ShouldBeHandled()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Empty Password",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = ""
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Empty-Password");
        result["Empty-Password"].Should().NotBeNull();
        // Empty password results in item name as fallback
        result["Empty-Password"].Should().Be("Empty Password");
    }

    [Fact]
    [Trait("Category", "EdgeCases")]
    public async Task Password_WhitespaceOnly_ShouldBePreserved()
    {
        // Arrange
        var password = "   \t\n   ";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Whitespace Password",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = password
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("Whitespace-Password");
        result["Whitespace-Password"].Should().Be(password);
    }

    [Fact]
    [Trait("Category", "EdgeCases")]
    public async Task CustomField_WithOnlySpecialCharacters_ShouldBeHandled()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Special Field",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "special_chars", Value = "!@#$%^&*()_+-=[]{}|;':\",./<>?", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("special_chars");
        result["special_chars"].Should().Be("!@#$%^&*()_+-=[]{}|;':\",./<>?");
    }

    [Fact]
    [Trait("Category", "EdgeCases")]
    public async Task CustomField_WithControlCharacters_ShouldBeHandled()
    {
        // Arrange
        var value = "text\x00\x01\x02\x03\x04text"; // Including null bytes and control chars
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Control Chars",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "control_field", Value = value, Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("control_field");
        // Control characters may be stripped/normalized - just verify the field is present
        result["control_field"].Should().NotBeNull();
        result["control_field"].Should().Contain("text");
    }

    #endregion

    #region JSON and Structured Data

    [Fact]
    [Trait("Category", "StructuredData")]
    public async Task CustomField_WithJSONValue_ShouldBePreserved()
    {
        // Arrange
        var jsonValue = @"{""api_key"": ""secret123"", ""endpoint"": ""https://api.example.com"", ""nested"": {""value"": true}}";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "JSON Config",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "config_json", Value = jsonValue, Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("config_json");
        result["config_json"].Should().Be(jsonValue);
        result["config_json"].Should().Contain("api_key");
        result["config_json"].Should().Contain("endpoint");
    }

    [Fact]
    [Trait("Category", "StructuredData")]
    public async Task CustomField_WithYAMLValue_ShouldBePreserved()
    {
        // Arrange
        var yamlValue = @"apiVersion: v1
kind: Config
metadata:
  name: my-config
data:
  key: value
  nested:
    subkey: subvalue";
        
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "YAML Config",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "config_yaml", Value = yamlValue, Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("config_yaml");
        result["config_yaml"].Should().Be(yamlValue);
        result["config_yaml"].Should().Contain("apiVersion");
        result["config_yaml"].Should().Contain("kind");
    }

    [Fact]
    [Trait("Category", "StructuredData")]
    public async Task CustomField_WithBase64Data_ShouldBePreserved()
    {
        // Arrange
        var base64Value = Convert.ToBase64String(Encoding.UTF8.GetBytes("This is binary data that was base64 encoded"));
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Base64 Data",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "binary_data", Value = base64Value, Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("binary_data");
        result["binary_data"].Should().Be(base64Value);
        
        // Verify it can be decoded back
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(result["binary_data"]));
        decoded.Should().Be("This is binary data that was base64 encoded");
    }

    #endregion

    #region Real-World Examples

    [Fact]
    [Trait("Category", "RealWorld")]
    public async Task RealWorld_DatabaseConnectionString_ShouldBeHandled()
    {
        // Arrange
        var connectionString = "Server=myserver.database.windows.net;Database=mydb;User Id=admin@myserver;Password=P@ssw0rd!#$%;Encrypt=True;TrustServerCertificate=False;";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "DB Connection",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "admin",
                Password = "P@ssw0rd!#$%"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "CONNECTION_STRING", Value = connectionString, Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey("CONNECTION_STRING");
        result["CONNECTION_STRING"].Should().Be(connectionString);
    }

    [Fact]
    [Trait("Category", "RealWorld")]
    public async Task RealWorld_AWSCredentials_ShouldBeHandled()
    {
        // Arrange
        var secretKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "AWS Credentials",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "AKIAIOSFODNN7EXAMPLE",
                Password = secretKey
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "AWS_ACCESS_KEY_ID", Value = "AKIAIOSFODNN7EXAMPLE", Type = 0 },
                new FieldInfo { Name = "AWS_SECRET_ACCESS_KEY", Value = secretKey, Type = 0 },
                new FieldInfo { Name = "AWS_REGION", Value = "us-east-1", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKeys("AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_REGION");
        result["AWS_SECRET_ACCESS_KEY"].Should().Contain("/");
        result["AWS_SECRET_ACCESS_KEY"].Should().Be(secretKey);
    }

    [Fact]
    [Trait("Category", "RealWorld")]
    public async Task RealWorld_DockerHubCredentials_ShouldBeHandled()
    {
        // Arrange
        var dockerConfigJson = @"{""auths"":{""https://index.docker.io/v1/"":{""auth"":""dXNlcjpwYXNzd29yZA==""}}}";
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Docker Hub",
            Type = 1,
            Login = new LoginInfo
            {
                Username = "dockeruser",
                Password = "dockerpassword"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = ".dockerconfigjson", Value = dockerConfigJson, Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        result.Should().ContainKey(".dockerconfigjson");
        result[".dockerconfigjson"].Should().Be(dockerConfigJson);
    }

    #endregion
}

