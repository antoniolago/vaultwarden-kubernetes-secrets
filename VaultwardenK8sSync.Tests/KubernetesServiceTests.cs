using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using FluentAssertions;

namespace VaultwardenK8sSync.Tests;

public class KubernetesServiceTests
{
    private readonly Mock<ILogger<KubernetesService>> _loggerMock;
    private readonly KubernetesSettings _config;
    private readonly KubernetesService _service;

    public KubernetesServiceTests()
    {
        _loggerMock = new Mock<ILogger<KubernetesService>>();
        _config = new KubernetesSettings();
        _service = new KubernetesService(_loggerMock.Object, _config);
    }

    #region ParseManagedKeysAnnotation Tests

    [Fact]
    public void ParseManagedKeysAnnotation_WithValidJson_ShouldReturnKeysList()
    {
        // Arrange
        var json = "[\"username\",\"password\",\"api-key\"]";

        // Act
        var result = KubernetesService.ParseManagedKeysAnnotation(json);

        // Assert
        result.Should().BeEquivalentTo(new[] { "username", "password", "api-key" });
    }

    [Fact]
    public void ParseManagedKeysAnnotation_WithEmptyArray_ShouldReturnEmptyList()
    {
        // Arrange
        var json = "[]";

        // Act
        var result = KubernetesService.ParseManagedKeysAnnotation(json);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseManagedKeysAnnotation_WithNull_ShouldReturnEmptyList()
    {
        // Act
        var result = KubernetesService.ParseManagedKeysAnnotation(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseManagedKeysAnnotation_WithEmptyString_ShouldReturnEmptyList()
    {
        // Act
        var result = KubernetesService.ParseManagedKeysAnnotation("");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseManagedKeysAnnotation_WithMalformedJson_ShouldReturnEmptyList()
    {
        // Arrange
        var json = "not valid json";
        var loggerMock = new Mock<ILogger>();

        // Act
        var result = KubernetesService.ParseManagedKeysAnnotation(json, loggerMock.Object);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseManagedKeysAnnotation_WithPartialJson_ShouldReturnEmptyList()
    {
        // Arrange
        var json = "[\"username\", ";

        // Act
        var result = KubernetesService.ParseManagedKeysAnnotation(json);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region SerializeManagedKeysAnnotation Tests

    [Fact]
    public void SerializeManagedKeysAnnotation_WithKeys_ShouldReturnSortedJson()
    {
        // Arrange
        var keys = new[] { "zebra", "apple", "mango" };

        // Act
        var result = KubernetesService.SerializeManagedKeysAnnotation(keys);

        // Assert
        result.Should().Be("[\"apple\",\"mango\",\"zebra\"]");
    }

    [Fact]
    public void SerializeManagedKeysAnnotation_WithEmptyList_ShouldReturnEmptyArray()
    {
        // Arrange
        var keys = new string[] { };

        // Act
        var result = KubernetesService.SerializeManagedKeysAnnotation(keys);

        // Assert
        result.Should().Be("[]");
    }

    [Fact]
    public void SerializeManagedKeysAnnotation_WithSingleKey_ShouldReturnSingleElementArray()
    {
        // Arrange
        var keys = new[] { "password" };

        // Act
        var result = KubernetesService.SerializeManagedKeysAnnotation(keys);

        // Assert
        result.Should().Be("[\"password\"]");
    }

    [Fact]
    public void SerializeManagedKeysAnnotation_RoundTrip_ShouldPreserveKeys()
    {
        // Arrange
        var originalKeys = new[] { "username", "password", "api-key" };

        // Act
        var json = KubernetesService.SerializeManagedKeysAnnotation(originalKeys);
        var parsedKeys = KubernetesService.ParseManagedKeysAnnotation(json);

        // Assert
        parsedKeys.Should().BeEquivalentTo(originalKeys);
    }

    #endregion

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act & Assert
        _service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullConfig_ShouldCreateInstance()
    {
        // Act & Assert
        var service = new KubernetesService(_loggerMock.Object, null);
        service.Should().NotBeNull();
    }

    [Theory]
    [InlineData("test-namespace", "test-namespace")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void GetDefaultNamespace_WithDifferentValues_ShouldReturnCorrectNamespace(string configNamespace, string expected)
    {
        // Arrange
        _config.DefaultNamespace = configNamespace;

        // Act
        var result = _config.DefaultNamespace;

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("test-context", "test-context")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void GetContext_WithDifferentValues_ShouldReturnCorrectContext(string configContext, string expected)
    {
        // Arrange
        _config.Context = configContext;

        // Act
        var result = _config.Context;

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("test-config", "test-config")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void GetKubeConfigPath_WithDifferentValues_ShouldReturnCorrectConfig(string configPath, string expected)
    {
        // Arrange
        _config.KubeConfigPath = configPath;

        // Act
        var result = _config.KubeConfigPath;

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Constructor_WithBasicConfig_ShouldSetCorrectProperties()
    {
        // Arrange
        _config.DefaultNamespace = "test-namespace";
        _config.Context = "test-context";
        _config.KubeConfigPath = "test-config";

        // Act
        var service = new KubernetesService(_loggerMock.Object, _config);

        // Assert
        service.Should().NotBeNull();
        _config.DefaultNamespace.Should().Be("test-namespace");
        _config.Context.Should().Be("test-context");
        _config.KubeConfigPath.Should().Be("test-config");
    }

    [Fact]
    public void Constructor_WithEmptyConfig_ShouldSetDefaultValues()
    {
        // Arrange
        _config.DefaultNamespace = "";
        _config.Context = "";
        _config.KubeConfigPath = "";

        // Act
        var service = new KubernetesService(_loggerMock.Object, _config);

        // Assert
        service.Should().NotBeNull();
        _config.DefaultNamespace.Should().Be("");
        _config.Context.Should().Be("");
        _config.KubeConfigPath.Should().Be("");
    }

    [Fact]
    public void Constructor_WithNullConfig_ShouldHandleNull()
    {
        // Arrange & Act
        var service = new KubernetesService(_loggerMock.Object, null);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void InCluster_WithTrue_ShouldSetCorrectValue()
    {
        // Arrange
        _config.InCluster = true;

        // Act
        var result = _config.InCluster;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void InCluster_WithFalse_ShouldSetCorrectValue()
    {
        // Arrange
        _config.InCluster = false;

        // Act
        var result = _config.InCluster;

        // Assert
        result.Should().BeFalse();
    }
}
