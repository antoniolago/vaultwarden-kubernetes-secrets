using VaultwardenK8sSync.Models;
using Xunit;

namespace VaultwardenK8sSync.Tests;

[Collection("Registry Config Tests")]
[Trait("Category", "Unit")]
public class DockerConfigJsonSyncTests
{
    #region ExtractDockerConfigJsonServer Tests

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractDockerConfigJsonServer_WithRegistryField_ReturnsValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "registry", Value = "ghcr.io", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractDockerConfigJsonServer();

        // Assert
        Assert.Equal("ghcr.io", result);
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractDockerConfigJsonServer_WithNoFields_ReturnsNull()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>()
        };

        // Act
        var result = item.ExtractDockerConfigJsonServer();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractDockerConfigJsonServer_CaseInsensitive_ReturnsValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "REGISTRY", Value = "docker.io", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractDockerConfigJsonServer();

        // Assert
        Assert.Equal("docker.io", result);
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractDockerConfigJsonServer_WithWhitespace_TrimsValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "registry", Value = "  quay.io  ", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractDockerConfigJsonServer();

        // Assert
        Assert.Equal("quay.io", result);
    }

    #endregion

    #region ExtractDockerConfigJsonEmail Tests

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractDockerConfigJsonEmail_WithEmailField_ReturnsValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "registry-email", Value = "user@example.com", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractDockerConfigJsonEmail();

        // Assert
        Assert.Equal("user@example.com", result);
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractDockerConfigJsonEmail_WithNoFields_ReturnsNull()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>()
        };

        // Act
        var result = item.ExtractDockerConfigJsonEmail();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractDockerConfigJsonEmail_CaseInsensitive_ReturnsValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "REGISTRY-EMAIL", Value = "admin@test.com", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractDockerConfigJsonEmail();

        // Assert
        Assert.Equal("admin@test.com", result);
    }

    #endregion

    #region ExtractIgnoredFields Tests

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractIgnoredFields_IncludesRegistryFields()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>()
        };

        // Act
        var result = item.ExtractIgnoredFields();

        // Assert
        Assert.Contains("docker-config-json-server", result);
        Assert.Contains("docker-config-json-email", result);
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractIgnoredFields_IncludesAllMetadataFields()
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
        Assert.Contains("secret-type", result);
        Assert.Contains("docker-config-json-server", result);
        Assert.Contains("docker-config-json-email", result);
    }

    #endregion

    #region Secret Type Tests

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractSecretType_WithDockerConfigJsonType_ReturnsType()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretType();

        // Assert
        Assert.Equal("kubernetes.io/dockerconfigjson", result);
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void ExtractSecretType_WithDockerConfigJsonCaseInsensitive_ReturnsCanonicalCasing()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-type", Value = "KUBERNETES.IO/DOCKERCONFIGJSON", Type = 0 }
            }
        };

        // Act
        var result = item.ExtractSecretType();

        // Assert
        Assert.Equal("kubernetes.io/dockerconfigjson", result);
    }

    #endregion
}
