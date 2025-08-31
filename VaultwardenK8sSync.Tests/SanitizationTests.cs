using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Configuration;
using Xunit;

namespace VaultwardenK8sSync.Tests;

[Collection("Sanitization Tests")]
[Trait("Category", "Sanitization")]
public class SanitizationTests
{
    private readonly SyncService _syncService;
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly SyncSettings _syncConfig;

    public SanitizationTests()
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
    [Trait("Category", "Sanitization")]
    [Trait("Category", "SecretNames")]
    [InlineData("Test Secret", "test-secret")]
    [InlineData("My Secret Name", "my-secret-name")]
    [InlineData("API_Key_Secret", "api-key-secret")]
    [InlineData("Database.Password", "database-password")]
    [InlineData("Config/Path/Secret", "config-path-secret")]
    [InlineData("Secret with (parentheses)", "secret-with-parentheses")]
    [InlineData("Secret with [brackets]", "secret-with-brackets")]
    [InlineData("Secret with {braces}", "secret-with-braces")]
    [InlineData("Secret with 'quotes'", "secret-with-quotes")]
    [InlineData("Secret with \"double quotes\"", "secret-with-double-quotes")]
    [InlineData("Secret with `backticks`", "secret-with-backticks")]
    [InlineData("Secret with ~tilde~", "secret-with-tilde")]
    [InlineData("Secret with !exclamation!", "secret-with-exclamation")]
    [InlineData("Secret with @at@", "secret-with-at")]
    [InlineData("Secret with #hash#", "secret-with-hash")]
    [InlineData("Secret with $dollar$", "secret-with-dollar")]
    [InlineData("Secret with %percent%", "secret-with-percent")]
    [InlineData("Secret with ^caret^", "secret-with-caret")]
    [InlineData("Secret with &ampersand&", "secret-with-ampersand")]
    [InlineData("Secret with *asterisk*", "secret-with-asterisk")]
    [InlineData("Secret with +plus+", "secret-with-plus")]
    [InlineData("Secret with =equals=", "secret-with-equals")]
    [InlineData("Secret with |pipe|", "secret-with-pipe")]
    [InlineData("Secret with <less>", "secret-with-less")]
    [InlineData("Secret with >greater>", "secret-with-greater")]
    [InlineData("Secret with ?question?", "secret-with-question")]
    [InlineData("test-se-cret-default", "test-se-cret-default")]
    [InlineData("my-secret-name", "my-secret-name")]
    [InlineData("api-key-secret", "api-key-secret")]
    public void SanitizeSecretName_ValidNames_ShouldSanitizeCorrectly(string input, string expected)
    {
        // Act
        var result = SanitizeSecretName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [Trait("Category", "Sanitization")]
    [Trait("Category", "SecretNames")]
    [Trait("Category", "Validation")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SanitizeSecretName_EmptyOrNull_ShouldThrowArgumentException(string input)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => SanitizeSecretName(input));
        Assert.Contains("cannot be null, empty, or whitespace", exception.Message);
    }

    [Theory]
    [Trait("Category", "Sanitization")]
    [Trait("Category", "SecretNames")]
    [Trait("Category", "Validation")]
    [InlineData("---")]
    [InlineData("...")]
    [InlineData("###")]
    [InlineData("***")]
    public void SanitizeSecretName_OnlySpecialCharacters_ShouldThrowArgumentException(string input)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => SanitizeSecretName(input));
        Assert.Contains("becomes empty after sanitization", exception.Message);
    }

    [Theory]
    [Trait("Category", "Sanitization")]
    [Trait("Category", "FieldNames")]
    [InlineData("API_KEY", "API_KEY")]
    [InlineData("Database Password", "Database_Password")]
    [InlineData("secret-key", "secret-key")]
    [InlineData("my.field.name", "my_field_name")]
    [InlineData("config/path/key", "config_path_key")]
    [InlineData("Field with (parentheses)", "Field_with_parentheses")]
    [InlineData("Field with [brackets]", "Field_with_brackets")]
    [InlineData("Field with {braces}", "Field_with_braces")]
    [InlineData("Field with 'quotes'", "Field_with_quotes")]
    [InlineData("Field with \"double quotes\"", "Field_with_double_quotes")]
    [InlineData("Field with `backticks`", "Field_with_backticks")]
    [InlineData("Field with ~tilde~", "Field_with_tilde")]
    [InlineData("Field with !exclamation!", "Field_with_exclamation")]
    [InlineData("Field with @at@", "Field_with_at")]
    [InlineData("Field with #hash#", "Field_with_hash")]
    [InlineData("Field with $dollar$", "Field_with_dollar")]
    [InlineData("Field with %percent%", "Field_with_percent")]
    [InlineData("Field with ^caret^", "Field_with_caret")]
    [InlineData("Field with &ampersand&", "Field_with_ampersand")]
    [InlineData("Field with *asterisk*", "Field_with_asterisk")]
    [InlineData("Field with +plus+", "Field_with_plus")]
    [InlineData("Field with =equals=", "Field_with_equals")]
    [InlineData("Field with |pipe|", "Field_with_pipe")]
    [InlineData("Field with <less>", "Field_with_less")]
    [InlineData("Field with >greater>", "Field_with_greater")]
    [InlineData("Field with ?question?", "Field_with_question")]
    [InlineData("SMTP_PASSWORD", "SMTP_PASSWORD")]
    [InlineData("smtp_password", "smtp_password")]
    [InlineData("API-KEY", "API-KEY")]
    [InlineData("database-password", "database-password")]
    public void SanitizeFieldName_ValidNames_ShouldSanitizeCorrectly(string input, string expected)
    {
        // Act
        var result = SanitizeFieldName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SanitizeFieldName_EmptyOrNull_ShouldThrowArgumentException(string input)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => SanitizeFieldName(input));
        Assert.Contains("cannot be null, empty, or whitespace", exception.Message);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("...")]
    [InlineData("###")]
    [InlineData("***")]
    public void SanitizeFieldName_OnlySpecialCharacters_ShouldThrowArgumentException(string input)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => SanitizeFieldName(input));
        Assert.Contains("becomes empty after sanitization", exception.Message);
    }

    [Theory]
    [InlineData("123invalid", "123invalid")] // Should be preserved, let Kubernetes handle validation
    [InlineData("1password", "1password")] // Should be preserved, let Kubernetes handle validation
    [InlineData("2fa_code", "2fa_code")] // Should be preserved, let Kubernetes handle validation
    public void SanitizeFieldName_StartsWithNumbers_ShouldPreserve(string input, string expected)
    {
        // Act
        var result = SanitizeFieldName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "FieldNames")]
    public void ExtractSecretDataAsync_WithCustomFieldNames_ShouldPreserveCase()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
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
                new FieldInfo { Name = "Database Password", Value = "db123", Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "my-secret", Type = 0 },
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        Assert.Contains("SMTP_PASSWORD", result.Keys);
        Assert.Contains("API-KEY", result.Keys);
        Assert.Contains("Database_Password", result.Keys);
        Assert.Equal("smtp123", result["SMTP_PASSWORD"]);
        Assert.Equal("api123", result["API-KEY"]);
        Assert.Equal("db123", result["Database_Password"]);
    }

    [Fact]
    public void ExtractSecretDataAsync_WithHyphenatedItemName_ShouldPreserveHyphensInDefaultFields()
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
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        Assert.Contains("test-se-cret-default", result.Keys);
        Assert.Contains("test-se-cret-default_username", result.Keys);
        Assert.Equal("testpass", result["test-se-cret-default"]);
        Assert.Equal("testuser", result["test-se-cret-default_username"]);
    }

    [Fact]
    public void ExtractSecretDataAsync_WithCustomSecretName_ShouldUseCustomNameForDefaultFields()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Original Item Name",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-name", Value = "my-custom-secret", Type = 0 },
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        Assert.Contains("my-custom-secret", result.Keys);
        Assert.Contains("my-custom-secret_username", result.Keys);
        Assert.Equal("testpass", result["my-custom-secret"]);
        Assert.Equal("testuser", result["my-custom-secret_username"]);
    }

    [Fact]
    public void ExtractSecretDataAsync_WithSSHKey_ShouldPreserveHyphensInSSHFields()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "my-ssh-key",
            Type = 5, // SSH Key
            SshKey = new SshKeyInfo
            {
                PrivateKey = "private-key-content",
                PublicKey = "public-key-content",
                Fingerprint = "fingerprint-content"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        Assert.Contains("my-ssh-key", result.Keys);
        Assert.Contains("my-ssh-key_public_key", result.Keys);
        Assert.Contains("my-ssh-key_fingerprint", result.Keys);
        Assert.Equal("private-key-content", result["my-ssh-key"]);
        Assert.Equal("public-key-content", result["my-ssh-key_public_key"]);
        Assert.Equal("fingerprint-content", result["my-ssh-key_fingerprint"]);
    }

    [Fact]
    public void ExtractSecretDataAsync_WithCustomSecretKeyNames_ShouldUseCustomNames()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-key-password", Value = "custom_password_key", Type = 0 },
                new FieldInfo { Name = "secret-key-username", Value = "custom_username_key", Type = 0 },
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        Assert.Contains("custom_password_key", result.Keys);
        Assert.Contains("custom_username_key", result.Keys);
        Assert.Equal("testpass", result["custom_password_key"]);
        Assert.Equal("testuser", result["custom_username_key"]);
    }

    [Fact]
    public void ExtractSecretDataAsync_WithIgnoredFields_ShouldNotIncludeIgnoredFields()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SMTP_PASSWORD", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "ignore-field", Value = "SMTP_PASSWORD", Type = 0 },
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        Assert.DoesNotContain("SMTP_PASSWORD", result.Keys);
        Assert.Contains("test-item", result.Keys); // Default password key (sanitized)
        Assert.Contains("test-item_username", result.Keys); // Default username key (sanitized)
    }

    [Fact]
    public void ExtractSecretDataAsync_WithMetadataFields_ShouldNotIncludeMetadataFields()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "my-secret", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "custom_key", Type = 0 },
                new FieldInfo { Name = "secret-key-username", Value = "custom_user", Type = 0 },
                new FieldInfo { Name = "ignore-field", Value = "field1,field2", Type = 0 },
                new FieldInfo { Name = "SMTP_PASSWORD", Value = "smtp123", Type = 0 }
            }
        };

        // Act
        var result = ExtractSecretDataAsync(item).Result;

        // Assert
        // Metadata fields should not be included
        Assert.DoesNotContain("namespaces", result.Keys);
        Assert.DoesNotContain("secret-name", result.Keys);
        Assert.DoesNotContain("secret-key-password", result.Keys);
        Assert.DoesNotContain("secret-key-username", result.Keys);
        Assert.DoesNotContain("ignore-field", result.Keys);
        
        // Custom fields should be included
        Assert.Contains("SMTP_PASSWORD", result.Keys);
        Assert.Equal("smtp123", result["SMTP_PASSWORD"]);
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
