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

    #region In-Cluster Context Name Detection Tests

    // E2E regression tests for the context-name custom field.
    // The context-name field filters items by cluster, so the auto-detected
    // context name MUST be per-cluster (derived from the API server host),
    // NOT the constant "in-cluster". Otherwise every in-cluster deployment
    // reports the same context and the field is useless for multi-cluster.

    [Theory]
    [InlineData("https://10.96.0.1:443", "10.96.0.1")]
    [InlineData("https://us-cluster.lag0.lan:6443", "us-cluster.lag0.lan")]
    [InlineData("http://192.168.1.50:8080", "192.168.1.50")]
    [InlineData("https://k8s.example.com", "k8s.example.com")]
    public void DetectInClusterContextName_WithHost_ShouldStripSchemeAndPort(string host, string expected)
    {
        // Act
        var result = KubernetesService.DetectInClusterContextName(host);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DetectInClusterContextName_WithNullOrEmptyHost_ShouldFallbackToInCluster(string? host)
    {
        // Act
        var result = KubernetesService.DetectInClusterContextName(host);

        // Assert
        result.Should().Be("in-cluster");
    }

    [Fact]
    public void DetectInClusterContextName_DistinctClusters_ShouldProduceDistinctNames()
    {
        // Arrange - two different clusters with different API server hosts
        var clusterA = "https://10.96.0.1:443";
        var clusterB = "https://10.96.1.1:443";

        // Act
        var nameA = KubernetesService.DetectInClusterContextName(clusterA);
        var nameB = KubernetesService.DetectInClusterContextName(clusterB);

        // Assert
        nameA.Should().NotBe(nameB);
        nameA.Should().NotBe("in-cluster");
        nameB.Should().NotBe("in-cluster");
    }

    [Fact]
    public void InClusterMode_GetContextName_ShouldNotBeTheConstantInCluster_WhenHostDiffers()
    {
        // Arrange - simulate two distinct in-cluster deployments.
        // Regression for the bug where in-cluster mode hardcoded the detected
        // context name to "in-cluster", making the context-name filter useless
        // for distinguishing between clusters.
        var hostA = KubernetesService.DetectInClusterContextName("https://10.96.0.1:443");
        var hostB = KubernetesService.DetectInClusterContextName("https://10.96.2.1:443");

        // A cluster's detected context name must be derived from its own host,
        // distinguishable from another cluster's.
        hostA.Should().NotBe("in-cluster");
        hostB.Should().NotBe("in-cluster");
        hostA.Should().NotBe(hostB);
    }

    #endregion
}
