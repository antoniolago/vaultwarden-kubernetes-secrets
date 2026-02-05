using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace VaultwardenK8sSync.Tests;

[Collection("DeviceId Tests")]
[Trait("Category", "DeviceId")]
public class DeviceIdTests
{
    [Fact]
    [Trait("Category", "DeviceId")]
    public void GetOrGenerateDeviceId_WithExplicitDeviceId_ShouldReturnExplicitValue()
    {
        // Arrange
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://vault.example.com",
            ClientId = "user.abc123",
            DeviceId = "explicit-device-id-12345"
        };

        // Act
        var result = DeviceIdGenerator.GetOrGenerateDeviceId(config);

        // Assert
        Assert.Equal("explicit-device-id-12345", result);
    }

    [Theory]
    [Trait("Category", "DeviceId")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOrGenerateDeviceId_WithoutExplicitDeviceId_ShouldGenerateDeterministicId(string? deviceId)
    {
        // Arrange
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://vault.example.com",
            ClientId = "user.abc123",
            DeviceId = deviceId
        };

        // Act
        var result1 = DeviceIdGenerator.GetOrGenerateDeviceId(config);
        var result2 = DeviceIdGenerator.GetOrGenerateDeviceId(config);

        // Assert
        Assert.Equal(result1, result2); // Same input = same output
        Assert.Matches(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", result1); // Valid GUID format
    }

    [Fact]
    [Trait("Category", "DeviceId")]
    public void GetOrGenerateDeviceId_DifferentConfigs_ShouldGenerateDifferentIds()
    {
        // Arrange
        var config1 = new VaultwardenSettings
        {
            ServerUrl = "https://vault1.example.com",
            ClientId = "user.abc123"
        };
        var config2 = new VaultwardenSettings
        {
            ServerUrl = "https://vault2.example.com",
            ClientId = "user.abc123"
        };

        // Act
        var result1 = DeviceIdGenerator.GetOrGenerateDeviceId(config1);
        var result2 = DeviceIdGenerator.GetOrGenerateDeviceId(config2);

        // Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    [Trait("Category", "DeviceId")]
    public void GetOrGenerateDeviceId_SameConfigDifferentInstances_ShouldGenerateSameId()
    {
        // Arrange - simulate two different pod restarts with same config
        var config1 = new VaultwardenSettings
        {
            ServerUrl = "https://vault.example.com",
            ClientId = "user.abc123"
        };
        var config2 = new VaultwardenSettings
        {
            ServerUrl = "https://vault.example.com",
            ClientId = "user.abc123"
        };

        // Act
        var result1 = DeviceIdGenerator.GetOrGenerateDeviceId(config1);
        var result2 = DeviceIdGenerator.GetOrGenerateDeviceId(config2);

        // Assert
        Assert.Equal(result1, result2);
    }
}
