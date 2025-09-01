using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using FluentAssertions;

namespace VaultwardenK8sSync.Tests;

public class SyncSummaryFormatterTests
{
    [Fact]
    public void FormatSummary_WithEmptySummary_ShouldReturnBasicFormat()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow,
            OverallSuccess = true,
            HasChanges = false
        };

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("VAULTWARDEN K8S SYNC SUMMARY");
        result.Should().Contain("Status:");
        result.Should().Contain("Changes detected: No");
    }

    [Fact]
    public void FormatSummary_WithSuccessfulSync_ShouldIncludeSuccessDetails()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            OverallSuccess = true,
            HasChanges = true
        };
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "default",
            Created = 3,
            Updated = 2,
            Skipped = 1,
            Failed = 0,
            Success = true
        });

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().Contain("Status:");
        result.Should().Contain("Changes detected: Yes");
        result.Should().Contain("default");
        result.Should().Contain("Created:");
        result.Should().Contain("Updated:");
        result.Should().Contain("Up-To-Date:");
        result.Should().Contain("Failed:");
    }

    [Fact]
    public void FormatSummary_WithFailedSync_ShouldIncludeFailureDetails()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-8),
            EndTime = DateTime.UtcNow,
            OverallSuccess = false,
            HasChanges = false
        };
        summary.AddError("Authentication failed");
        summary.AddWarning("Some warnings occurred");

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().Contain("Status:");
        result.Should().Contain("Authentication failed");
        result.Should().Contain("Some warnings occurred");
    }

    [Fact]
    public void FormatSummary_WithNullSummary_ShouldReturnEmptyString()
    {
        // Act
        var result = SyncSummaryFormatter.FormatSummary(null);

        // Assert
        result.Should().BeEmpty();
    }
}
