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

    [Fact]
    public async Task AuthenticateAsync_WithValidCredentials_ApiLoginSucceeds_UnlockSucceeds_ShouldReturnTrue()
    {
        // Arrange
        _config.ServerUrl = "https://vault.example.com";
        _config.ClientId = "test-client-id";
        _config.ClientSecret = "test-client-secret";
        _config.MasterPassword = "test-password";

        var loginProcess = new System.Diagnostics.Process();
        var unlockProcess = new System.Diagnostics.Process();
        var statusProcess = new System.Diagnostics.Process();

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("config server https://vault.example.com"))
            .Returns(statusProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("login --apikey --raw"))
            .Returns(loginProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess(It.Is<string>(s => s.Contains("status --raw"))))
            .Returns(statusProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("unlock --raw"))
            .Returns(unlockProcess);

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == statusProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult { Success = true, ExitCode = 0 });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == loginProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult { Success = true, ExitCode = 0 });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == statusProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult 
            { 
                Success = true, 
                ExitCode = 0, 
                Output = "{\"status\":\"locked\",\"token\":null}" 
            });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == unlockProcess), It.IsAny<int>(), It.Is<string>(s => s == "test-password")))
            .ReturnsAsync(new ProcessResult 
            { 
                Success = true, 
                ExitCode = 0, 
                Output = "session-token-12345" 
            });

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeTrue();
        _processRunnerMock.Verify(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == loginProcess), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        _processRunnerMock.Verify(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == unlockProcess), It.IsAny<int>(), It.Is<string>(s => s == "test-password")), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidCredentials_ApiLoginSucceeds_UnlockFails_ShouldReturnFalse()
    {
        // Arrange
        _config.ServerUrl = "https://vault.example.com";
        _config.ClientId = "test-client-id";
        _config.ClientSecret = "test-client-secret";
        _config.MasterPassword = "wrong-password";

        var loginProcess = new System.Diagnostics.Process();
        var unlockProcess = new System.Diagnostics.Process();
        var statusProcess = new System.Diagnostics.Process();

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("config server https://vault.example.com"))
            .Returns(statusProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("login --apikey --raw"))
            .Returns(loginProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess(It.Is<string>(s => s.Contains("status --raw"))))
            .Returns(statusProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("unlock --raw"))
            .Returns(unlockProcess);

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == statusProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult { Success = true, ExitCode = 0 });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == loginProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult { Success = true, ExitCode = 0 });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == statusProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult 
            { 
                Success = true, 
                ExitCode = 0, 
                Output = "{\"status\":\"locked\",\"token\":null}" 
            });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == unlockProcess), It.IsAny<int>(), It.Is<string>(s => s == "wrong-password")))
            .ReturnsAsync(new ProcessResult 
            { 
                Success = false, 
                ExitCode = 1, 
                Error = "Invalid master password." 
            });

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeFalse();
        _processRunnerMock.Verify(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == unlockProcess), It.IsAny<int>(), It.Is<string>(s => s == "wrong-password")), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiLoginFails_ShouldLogExitCodeErrorAndOutput()
    {
        // Arrange
        _config.ServerUrl = "https://vault.example.com";
        _config.ClientId = "test-client-id";
        _config.ClientSecret = "test-client-secret";

        var loginProcess = new System.Diagnostics.Process();
        var statusProcess = new System.Diagnostics.Process();

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("config server https://vault.example.com"))
            .Returns(statusProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("login --apikey --raw"))
            .Returns(loginProcess);

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == statusProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult { Success = true, ExitCode = 0 });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == loginProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult 
            { 
                Success = false, 
                ExitCode = 1, 
                Error = "Authentication failed",
                Output = "Some output"
            });

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => 
                    v.ToString()!.Contains("ExitCode") && 
                    v.ToString()!.Contains("1") &&
                    v.ToString()!.Contains("Error") &&
                    v.ToString()!.Contains("Output")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_UnlockFails_ShouldLogExitCodeErrorAndOutput()
    {
        // Arrange
        _config.ServerUrl = "https://vault.example.com";
        _config.ClientId = "test-client-id";
        _config.ClientSecret = "test-client-secret";
        _config.MasterPassword = "wrong-password";

        var loginProcess = new System.Diagnostics.Process();
        var unlockProcess = new System.Diagnostics.Process();
        var statusProcess = new System.Diagnostics.Process();

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("config server https://vault.example.com"))
            .Returns(statusProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("login --apikey --raw"))
            .Returns(loginProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess(It.Is<string>(s => s.Contains("status --raw"))))
            .Returns(statusProcess);

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("unlock --raw"))
            .Returns(unlockProcess);

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == statusProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult { Success = true, ExitCode = 0 });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == loginProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult { Success = true, ExitCode = 0 });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == statusProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult 
            { 
                Success = true, 
                ExitCode = 0, 
                Output = "{\"status\":\"locked\",\"token\":null}" 
            });

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == unlockProcess), It.IsAny<int>(), It.Is<string>(s => s == "wrong-password")))
            .ReturnsAsync(new ProcessResult 
            { 
                Success = false, 
                ExitCode = 1, 
                Error = "Invalid master password.",
                Output = "Some stdout output"
            });

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => 
                    v.ToString()!.Contains("ExitCode") && 
                    v.ToString()!.Contains("1") &&
                    v.ToString()!.Contains("Error") &&
                    v.ToString()!.Contains("Output")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_SetServerUrlFails_ShouldLogExitCodeErrorAndOutput()
    {
        // Arrange
        _config.ServerUrl = "https://vault.example.com";
        _config.ClientId = "test-client-id";
        _config.ClientSecret = "test-client-secret";

        var statusProcess = new System.Diagnostics.Process();

        _processFactoryMock
            .Setup(x => x.CreateBwProcess("config server https://vault.example.com"))
            .Returns(statusProcess);

        _processRunnerMock
            .Setup(x => x.RunAsync(It.Is<System.Diagnostics.Process>(p => p == statusProcess), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessResult 
            { 
                Success = false, 
                ExitCode = 1, 
                Error = "Failed to set server URL",
                Output = "Some output"
            });

        // Act
        var result = await _service.AuthenticateAsync();

        // Assert
        result.Should().BeFalse();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => 
                    v.ToString()!.Contains("ExitCode") && 
                    v.ToString()!.Contains("1") &&
                    v.ToString()!.Contains("Error") &&
                    v.ToString()!.Contains("Output")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
