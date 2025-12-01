using VaultwardenK8sSync.Models;
using Xunit;

namespace VaultwardenK8sSync.Tests;

[Collection("VaultwardenItem Tests")]
[Trait("Category", "Unit")]
public class VaultwardenItemTests
{
    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretKeyPassword_WithSecretKeyPassword_ReturnsValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-key-password", Value = "my-password-key", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretKeyPassword();

        // Assert
        Assert.Equal("my-password-key", result);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretKeyPassword_WithSecretKey_ReturnsValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-key", Value = "my-key", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretKeyPassword();

        // Assert
        Assert.Equal("my-key", result);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretKeyPassword_WithBothFields_PrefersSecretKeyPassword()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-key-password", Value = "password-value", Type = 0 },
                new FieldInfo { Name = "secret-key", Value = "key-value", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretKeyPassword();

        // Assert
        Assert.Equal("password-value", result);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretKeyPassword_WithNoFields_ReturnsNull()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>()
        };

        // Act
        var result = item.ExtractSecretKeyPassword();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretKeyPassword_CaseInsensitive_ReturnsValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SECRET-KEY", Value = "my-key", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretKeyPassword();

        // Assert
        Assert.Equal("my-key", result);
    }
}
