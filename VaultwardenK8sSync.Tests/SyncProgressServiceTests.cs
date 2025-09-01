using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using FluentAssertions;

namespace VaultwardenK8sSync.Tests;

public class SyncProgressServiceTests
{
    private readonly Mock<ILogger<SyncProgressService>> _loggerMock;
    private readonly Mock<ISyncService> _syncServiceMock;
    private readonly SyncSettings _syncConfig;
    private readonly SyncProgressService _service;

    public SyncProgressServiceTests()
    {
        _loggerMock = new Mock<ILogger<SyncProgressService>>();
        _syncServiceMock = new Mock<ISyncService>();
        _syncConfig = new SyncSettings();
        _service = new SyncProgressService(_loggerMock.Object, _syncServiceMock.Object, _syncConfig);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act & Assert
        _service.Should().NotBeNull();
    }

    [Fact]
    public async Task SyncWithProgressAsync_WithSuccessfulSync_ShouldReturnSummary()
    {
        // Arrange
        var expectedSummary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            OverallSuccess = true,
            HasChanges = true
        };
        expectedSummary.AddNamespace(new NamespaceSummary
        {
            Name = "default",
            Created = 5,
            Updated = 2,
            Success = true
        });

        _syncServiceMock.Setup(s => s.SyncAsync())
            .ReturnsAsync(expectedSummary);

        // Act
        var result = await _service.SyncWithProgressAsync();

        // Assert
        result.Should().NotBeNull();
        result.OverallSuccess.Should().BeTrue();
        result.HasChanges.Should().BeTrue();
        _syncServiceMock.Verify(s => s.SyncAsync(), Times.Once);
    }

    [Fact]
    public async Task SyncWithProgressAsync_WithFailedSync_ShouldReturnSummary()
    {
        // Arrange
        var expectedSummary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow,
            OverallSuccess = false,
            HasChanges = false
        };

        _syncServiceMock.Setup(s => s.SyncAsync())
            .ReturnsAsync(expectedSummary);

        // Act
        var result = await _service.SyncWithProgressAsync();

        // Assert
        result.Should().NotBeNull();
        result.OverallSuccess.Should().BeFalse();
        _syncServiceMock.Verify(s => s.SyncAsync(), Times.Once);
    }

    [Fact]
    public async Task SyncWithProgressAsync_WithSyncException_ShouldThrowException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Sync failed");
        _syncServiceMock.Setup(s => s.SyncAsync())
            .ThrowsAsync(expectedException);

        // Act & Assert
        await _service.Invoking(s => s.SyncWithProgressAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Sync failed");
        
        _syncServiceMock.Verify(s => s.SyncAsync(), Times.Once);
    }
}
