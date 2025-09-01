using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Infrastructure;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using FluentAssertions;

namespace VaultwardenK8sSync.Tests;

public class VaultwardenServiceTests
{
    private readonly Mock<ILogger<VaultwardenService>> _loggerMock;
    private readonly Mock<IProcessFactory> _processFactoryMock;
    private readonly Mock<IProcessRunner> _processRunnerMock;
    private readonly VaultwardenSettings _config;
    private readonly VaultwardenService _service;

    public VaultwardenServiceTests()
    {
        _loggerMock = new Mock<ILogger<VaultwardenService>>();
        _processFactoryMock = new Mock<IProcessFactory>();
        _processRunnerMock = new Mock<IProcessRunner>();
        _config = new VaultwardenSettings();
        _service = new VaultwardenService(_loggerMock.Object, _config, _processFactoryMock.Object, _processRunnerMock.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act & Assert
        _service.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WithMissingCredentials_ShouldReturnFalse()
    {
        // Arrange
        _config.ServerUrl = "https://vault.example.com";
        _config.ClientId = "";
        _config.ClientSecret = "";

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WithNullCredentials_ShouldReturnFalse()
    {
        // Arrange
        _config.ServerUrl = "https://vault.example.com";
        _config.ClientId = null;
        _config.ClientSecret = null;

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WithEmptyServerUrl_ShouldReturnFalse()
    {
        // Arrange
        _config.ServerUrl = "";
        _config.ClientId = "test-client-id";
        _config.ClientSecret = "test-client-secret";

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WithNullServerUrl_ShouldReturnFalse()
    {
        // Arrange
        _config.ServerUrl = null;
        _config.ClientId = "test-client-id";
        _config.ClientSecret = "test-client-secret";

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeFalse();
    }
}
