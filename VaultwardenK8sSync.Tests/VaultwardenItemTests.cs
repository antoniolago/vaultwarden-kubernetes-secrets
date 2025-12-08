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

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithMultilineEqualsFormat_ParsesCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-annotation", 
                    Value = "app.kubernetes.io/version=1.2.3\nexample.com/owner=platform-team\nmonitoring.enabled=true",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("1.2.3", result["app.kubernetes.io/version"]);
        Assert.Equal("platform-team", result["example.com/owner"]);
        Assert.Equal("true", result["monitoring.enabled"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithColonFormat_ParsesCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-annotation", 
                    Value = "app.kubernetes.io/version: 1.2.3\nexample.com/owner: platform-team",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("1.2.3", result["app.kubernetes.io/version"]);
        Assert.Equal("platform-team", result["example.com/owner"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithMixedFormats_ParsesCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-annotation", 
                    Value = "key1=value1\nkey2: value2\nkey3=value3",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.Equal("value3", result["key3"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithComments_IgnoresComments()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-annotation", 
                    Value = "# This is a comment\nkey1=value1\n# Another comment\nkey2=value2",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithEmptyLines_IgnoresEmptyLines()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-annotation", 
                    Value = "key1=value1\n\n\nkey2=value2\n   \nkey3=value3",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.Equal("value3", result["key3"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithNoField_ReturnsEmpty()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>()
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithWhitespace_TrimsCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-annotation", 
                    Value = "  key1  =  value1  \n  key2  :  value2  ",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretLabels_WithMultilineEqualsFormat_ParsesCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-label", 
                    Value = "environment=production\nteam=backend\napp=myapp",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretLabels();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("production", result["environment"]);
        Assert.Equal("backend", result["team"]);
        Assert.Equal("myapp", result["app"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretLabels_WithColonFormat_ParsesCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-label", 
                    Value = "environment: production\nteam: backend",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretLabels();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("production", result["environment"]);
        Assert.Equal("backend", result["team"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretLabels_WithNoField_ReturnsEmpty()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>()
        };

        // Act
        var result = item.ExtractSecretLabels();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractIgnoredFields_IncludesSecretAnnotationsAndLabels()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>()
        };

        // Act
        var result = item.ExtractIgnoredFields();

        // Assert
        Assert.Contains("ignore-field", result);
        Assert.Contains("secret-annotation", result);
        Assert.Contains("secret-label", result);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithWindowsLineEndings_ParsesCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo 
                { 
                    Name = "secret-annotation", 
                    Value = "key1=value1\r\nkey2=value2\r\nkey3=value3",
                    Type = 0 
                }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.Equal("value3", result["key3"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithMultipleFields_CombinesAll()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-annotation", Value = "app.kubernetes.io/version=1.2.3", Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "example.com/owner=platform-team", Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "monitoring.enabled=true", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("1.2.3", result["app.kubernetes.io/version"]);
        Assert.Equal("platform-team", result["example.com/owner"]);
        Assert.Equal("true", result["monitoring.enabled"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretAnnotations_WithMultipleFieldsAndMultiline_CombinesAll()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-annotation", Value = "key1=value1\nkey2=value2", Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "key3=value3", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretAnnotations();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.Equal("value3", result["key3"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretLabels_WithMultipleFields_CombinesAll()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-label", Value = "environment=production", Type = 0 },
                new FieldInfo { Name = "secret-label", Value = "team=backend", Type = 0 },
                new FieldInfo { Name = "secret-label", Value = "app=myapp", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretLabels();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("production", result["environment"]);
        Assert.Equal("backend", result["team"]);
        Assert.Equal("myapp", result["app"]);
    }

    [Fact]
    [Trait("Category", "CustomFields")]
    public void ExtractSecretLabels_WithMultipleFieldsAndMultiline_CombinesAll()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-label", Value = "key1=value1\nkey2=value2", Type = 0 },
                new FieldInfo { Name = "secret-label", Value = "key3=value3", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretLabels();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.Equal("value3", result["key3"]);
    }
}
