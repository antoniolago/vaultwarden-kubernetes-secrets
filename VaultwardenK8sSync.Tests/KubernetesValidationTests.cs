using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Configuration;
using Xunit;

namespace VaultwardenK8sSync.Tests;

public class KubernetesValidationTests
{
    private readonly SyncService _syncService;
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly SyncSettings _syncConfig;

    public KubernetesValidationTests()
    {
        _loggerMock = new Mock<ILogger<SyncService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
        _syncConfig = new SyncSettings();
        
        _syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _syncConfig);
    }

    [Theory]
    [InlineData("a", "a")] // Single character
    [InlineData("ab", "ab")] // Two characters
    [InlineData("a-b", "a-b")] // Valid hyphen
    [InlineData("a_b", "a-b")] // Underscore converted to hyphen
    [InlineData("a.b", "a-b")] // Dot converted to hyphen
    [InlineData("a/b", "a-b")] // Slash converted to hyphen
    [InlineData("a\\b", "a-b")] // Backslash converted to hyphen
    [InlineData("a:b", "a-b")] // Colon converted to hyphen
    [InlineData("a;b", "a-b")] // Semicolon converted to hyphen
    [InlineData("a,b", "a-b")] // Comma converted to hyphen
    [InlineData("a(b", "a-b")] // Parentheses converted to hyphen
    [InlineData("a[b", "a-b")] // Brackets converted to hyphen
    [InlineData("a{b", "a-b")] // Braces converted to hyphen
    [InlineData("a'b", "a-b")] // Single quote converted to hyphen
    [InlineData("a\"b", "a-b")] // Double quote converted to hyphen
    [InlineData("a`b", "a-b")] // Backtick converted to hyphen
    [InlineData("a~b", "a-b")] // Tilde converted to hyphen
    [InlineData("a!b", "a-b")] // Exclamation converted to hyphen
    [InlineData("a@b", "a-b")] // At converted to hyphen
    [InlineData("a#b", "a-b")] // Hash converted to hyphen
    [InlineData("a$b", "a-b")] // Dollar converted to hyphen
    [InlineData("a%b", "a-b")] // Percent converted to hyphen
    [InlineData("a^b", "a-b")] // Caret converted to hyphen
    [InlineData("a&b", "a-b")] // Ampersand converted to hyphen
    [InlineData("a*b", "a-b")] // Asterisk converted to hyphen
    [InlineData("a+b", "a-b")] // Plus converted to hyphen
    [InlineData("a=b", "a-b")] // Equals converted to hyphen
    [InlineData("a|b", "a-b")] // Pipe converted to hyphen
    [InlineData("a<b", "a-b")] // Less than converted to hyphen
    [InlineData("a>b", "a-b")] // Greater than converted to hyphen
    [InlineData("a?b", "a-b")] // Question mark converted to hyphen
    public void SanitizeSecretName_EdgeCases_ShouldHandleCorrectly(string input, string expected)
    {
        // Act
        var result = SanitizeSecretName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("a", "a")] // Single character
    [InlineData("ab", "ab")] // Two characters
    [InlineData("a-b", "a-b")] // Valid hyphen preserved
    [InlineData("a_b", "a_b")] // Valid underscore preserved
    [InlineData("a.b", "a.b")] // Dot should NOT be converted to underscore
    [InlineData("a/b", "a_b")] // Slash converted to underscore
    [InlineData("a\\b", "a_b")] // Backslash converted to underscore
    [InlineData("a:b", "a_b")] // Colon converted to underscore
    [InlineData("a;b", "a_b")] // Semicolon converted to underscore
    [InlineData("a,b", "a_b")] // Comma converted to underscore
    [InlineData("a(b", "a_b")] // Parentheses converted to underscore
    [InlineData("a[b", "a_b")] // Brackets converted to underscore
    [InlineData("a{b", "a_b")] // Braces converted to underscore
    [InlineData("a'b", "a_b")] // Single quote converted to underscore
    [InlineData("a\"b", "a_b")] // Double quote converted to underscore
    [InlineData("a`b", "a_b")] // Backtick converted to underscore
    [InlineData("a~b", "a_b")] // Tilde converted to underscore
    [InlineData("a!b", "a_b")] // Exclamation converted to underscore
    [InlineData("a@b", "a_b")] // At converted to underscore
    [InlineData("a#b", "a_b")] // Hash converted to underscore
    [InlineData("a$b", "a_b")] // Dollar converted to underscore
    [InlineData("a%b", "a_b")] // Percent converted to underscore
    [InlineData("a^b", "a_b")] // Caret converted to underscore
    [InlineData("a&b", "a_b")] // Ampersand converted to underscore
    [InlineData("a*b", "a_b")] // Asterisk converted to underscore
    [InlineData("a+b", "a_b")] // Plus converted to underscore
    [InlineData("a=b", "a_b")] // Equals converted to underscore
    [InlineData("a|b", "a_b")] // Pipe converted to underscore
    [InlineData("a<b", "a_b")] // Less than converted to underscore
    [InlineData("a>b", "a_b")] // Greater than converted to underscore
    [InlineData("a?b", "a_b")] // Question mark converted to underscore
    public void SanitizeFieldName_EdgeCases_ShouldHandleCorrectly(string input, string expected)
    {
        // Act
        var result = SanitizeFieldName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("test-se-cret-default", "test-se-cret-default")]
    [InlineData("my-secret-name", "my-secret-name")]
    [InlineData("api-key-secret", "api-key-secret")]
    [InlineData("database-password", "database-password")]
    [InlineData("config-path-secret", "config-path-secret")]
    public void SanitizeSecretName_ValidHyphenatedNames_ShouldPreserveHyphens(string input, string expected)
    {
        // Act
        var result = SanitizeSecretName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SMTP_PASSWORD", "SMTP_PASSWORD")]
    [InlineData("API-KEY", "API-KEY")]
    [InlineData("database-password", "database-password")]
    [InlineData("config_path_key", "config_path_key")]
    [InlineData("my-field-name", "my-field-name")]
    public void SanitizeFieldName_ValidFieldNames_ShouldPreserveFormatting(string input, string expected)
    {
        // Act
        var result = SanitizeFieldName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("...")]
    [InlineData("###")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void SanitizeSecretName_InvalidNames_ShouldThrowArgumentException(string input)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => SanitizeSecretName(input));
        Assert.Contains("Secret name cannot be null, empty, or whitespace", exception.Message);
    }

    [Theory]
    [InlineData("...")]
    [InlineData("---")]
    [InlineData("###")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void SanitizeFieldName_InvalidNames_ShouldThrowArgumentException(string input)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => SanitizeFieldName(input));
        
        // Check for appropriate error message based on the input
        if (string.IsNullOrWhiteSpace(input))
        {
            Assert.Contains("cannot be null, empty, or whitespace", exception.Message);
        }
        else if (input == "..." || input == "---")
        {
            Assert.Contains("must contain at least one alphanumeric character", exception.Message);
        }
        else
        {
            Assert.Contains("becomes empty after sanitization", exception.Message);
        }
    }

    [Fact]
    public void ExtractSecretDataAsync_WithComplexFieldNames_ShouldPreserveFormatting()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Complex Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SMTP_PASSWORD", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "API-KEY", Value = "api123", Type = 0 },
                new FieldInfo { Name = "Database Password", Value = "DB123", Type = 0 },
                new FieldInfo { Name = "config_path_key", Value = "config123", Type = 0 },
                new FieldInfo { Name = "my-field-name", Value = "field123", Type = 0 },
                new FieldInfo { Name = "123invalid", Value = "invalid123", Type = 0 },
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        Assert.Contains("SMTP_PASSWORD", result.Keys);
        Assert.Contains("API-KEY", result.Keys);
        Assert.Contains("Database_Password", result.Keys);
        Assert.Contains("config_path_key", result.Keys);
        Assert.Contains("my-field-name", result.Keys);
        Assert.Contains("123invalid", result.Keys);
        
        Assert.Equal("smtp123", result["SMTP_PASSWORD"]);
        Assert.Equal("api123", result["API-KEY"]);
        Assert.Equal("DB123", result["Database_Password"]);
        Assert.Equal("config123", result["config_path_key"]);
        Assert.Equal("field123", result["my-field-name"]);
        Assert.Equal("invalid123", result["123invalid"]);
    }

    [Fact]
    public void ExtractSecretDataAsync_WithMultipleHyphenatedNames_ShouldPreserveHyphens()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "test-se-cret-default",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SMTP_PASSWORD", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "API-KEY", Value = "api123", Type = 0 },
                new FieldInfo { Name = "database-password", Value = "db123", Type = 0 },
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        // Default fields should use hyphenated secret name
        Assert.Contains("test-se-cret-default", result.Keys);
        Assert.Contains("test-se-cret-default_username", result.Keys);
        
        // Custom fields should preserve their formatting
        Assert.Contains("SMTP_PASSWORD", result.Keys);
        Assert.Contains("API-KEY", result.Keys);
        Assert.Contains("database-password", result.Keys);
        
        Assert.Equal("testpass", result["test-se-cret-default"]);
        Assert.Equal("testuser", result["test-se-cret-default_username"]);
        Assert.Equal("smtp123", result["SMTP_PASSWORD"]);
        Assert.Equal("api123", result["API-KEY"]);
        Assert.Equal("db123", result["database-password"]);
    }

    // Helper methods to access private methods for testing
    private string SanitizeSecretName(string name)
    {
        var method = typeof(SyncService).GetMethod("SanitizeSecretName", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        try
        {
            return (string)method!.Invoke(null, new object[] { name })!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is ArgumentException)
        {
            throw ex.InnerException!;
        }
    }

    private string SanitizeFieldName(string fieldName)
    {
        var method = typeof(SyncService).GetMethod("SanitizeFieldName", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        try
        {
            return (string)method!.Invoke(null, new object[] { fieldName })!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is ArgumentException)
        {
            throw ex.InnerException!;
        }
    }

    private async Task<Dictionary<string, string>> ExtractSecretDataAsync(VaultwardenItem item)
    {
        var method = typeof(SyncService).GetMethod("ExtractSecretDataAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Dictionary<string, string>)await (Task<Dictionary<string, string>>)method!.Invoke(_syncService, new object[] { item })!;
    }
}
