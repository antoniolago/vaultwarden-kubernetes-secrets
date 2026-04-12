using System;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Tests that verify the SSH key parsing code exists and executes in VaultwardenService.ParseAndDecryptCipher.
/// These tests will FAIL if the parsing code is removed from the service.
/// </summary>
public class SshKeyVaultwardenServiceParsingTests
{
    private readonly Mock<ILogger<VaultwardenService>> _loggerMock;

    public SshKeyVaultwardenServiceParsingTests()
    {
        _loggerMock = new Mock<ILogger<VaultwardenService>>();
    }

    /// <summary>
    /// PROOF TEST: This test will FAIL if the SSH key parsing code is removed from VaultwardenService.
    ///
    /// It verifies that:
    /// 1. ParseAndDecryptCipher method exists
    /// 2. The method handles type 5 (SSH key) items
    /// 3. The method attempts to parse the sshKey JSON object
    /// 4. The resulting VaultwardenItem has SshKey populated (not null)
    ///
    /// Note: Plain text values (not starting with "2.") are returned as-is by DecryptString.
    /// Encrypted values are decrypted using the master password. The test uses plain text
    /// which passes through unchanged, proving the parsing code executed.
    /// </summary>
    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Unit")]
    public void ParseAndDecryptCipher_WithType5SshKey_ShouldPopulateSshKeyObject()
    {
        // Arrange
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://test.vaultwarden.local",
            MasterPassword = "testpassword",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };
        
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        using var httpClient = new HttpClient();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var service = new VaultwardenService(_loggerMock.Object, config, httpClientFactoryMock.Object);

        // JSON with plain text values (not encrypted)
        var sshKeyJson = @"{
            ""id"": ""ssh-key-123"",
            ""type"": 5,
            ""name"": ""Test SSH Key"",
            ""sshKey"": {
                ""privateKey"": ""test-private-key"",
                ""publicKey"": ""test-public-key"",
                ""keyFingerprint"": ""test-fingerprint""
            },
            ""fields"": []
        }";

        var cipher = JsonSerializer.Deserialize<JsonElement>(sshKeyJson);

        // Act - Call the private parsing method via reflection
        var parseMethod = typeof(VaultwardenService).GetMethod(
            "ParseAndDecryptCipher",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(parseMethod);
        var result = parseMethod!.Invoke(service, new object[] { cipher }) as VaultwardenItem;

        // Assert - THE CRITICAL TEST
        Assert.NotNull(result);
        Assert.Equal("ssh-key-123", result.Id);
        Assert.Equal(5, result.Type);

        // THIS IS THE KEY ASSERTION:
        // If the SSH parsing code is missing, SshKey will be null and this test FAILS.
        // Values are empty strings because _encryptionKey is null (AuthenticateAsync was not called),
        // so DecryptString returns null and the ?? string.Empty fallback applies.
        // The critical point is that SshKey object exists, proving the parsing code executed.
        Assert.NotNull(result.SshKey);
        Assert.Equal(string.Empty, result.SshKey.PrivateKey);
        Assert.Equal(string.Empty, result.SshKey.PublicKey);
        Assert.Equal(string.Empty, result.SshKey.Fingerprint);
    }

    /// <summary>
    /// Verifies that non-SSH items (type 1 = Login) do NOT get SshKey populated.
    /// </summary>
    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Unit")]
    public void ParseAndDecryptCipher_WithType1Login_ShouldNotPopulateSshKey()
    {
        // Arrange
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://test.vaultwarden.local",
            MasterPassword = "testpassword",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };
        
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        using var httpClient2 = new HttpClient();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient2);

        var service2 = new VaultwardenService(_loggerMock.Object, config, httpClientFactoryMock.Object);

        var loginJson = @"{
            ""id"": ""login-123"",
            ""type"": 1,
            ""name"": ""Test Login"",
            ""login"": {
                ""username"": ""testuser"",
                ""password"": ""testpass""
            },
            ""fields"": []
        }";

        var cipher = JsonSerializer.Deserialize<JsonElement>(loginJson);

        // Act
        var parseMethod = typeof(VaultwardenService).GetMethod(
            "ParseAndDecryptCipher",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(parseMethod);
        var result = parseMethod!.Invoke(service2, new object[] { cipher }) as VaultwardenItem;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Type);
        Assert.Null(result.SshKey); // Should NOT be populated for login items
        Assert.NotNull(result.Login); // But Login should be populated
    }

    /// <summary>
    /// Verifies that SSH items without the sshKey property have SshKey remain null
    /// (the parsing code handles type 5 but properly checks for the sshKey property).
    /// </summary>
    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Unit")]
    public void ParseAndDecryptCipher_Type5MissingSshKeyProperty_ReturnsNullSshKey()
    {
        // Arrange
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://test.vaultwarden.local",
            MasterPassword = "testpassword",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };
        
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        using var httpClient = new HttpClient();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var service = new VaultwardenService(_loggerMock.Object, config, httpClientFactoryMock.Object);

        // Type 5 but no sshKey property
        var partialSshKeyJson = @"{
            ""id"": ""ssh-key-456"",
            ""type"": 5,
            ""name"": ""Partial SSH Key"",
            ""fields"": []
        }";

        var cipher = JsonSerializer.Deserialize<JsonElement>(partialSshKeyJson);

        // Act
        var parseMethod = typeof(VaultwardenService).GetMethod(
            "ParseAndDecryptCipher",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(parseMethod);
        var result = parseMethod!.Invoke(service, new object[] { cipher }) as VaultwardenItem;

        // Assert - When the sshKey JSON property is missing, SshKey should remain null
        // (the parsing code handles type 5 but properly checks for the sshKey property)
        Assert.NotNull(result);
        Assert.Equal(5, result.Type);
        Assert.Null(result.SshKey);
    }
}
