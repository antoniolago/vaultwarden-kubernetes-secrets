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
        result.Should().Contain("CREATED:"); // Namespace shows in CREATED because it has Created > 0
        result.Should().Contain("[C:3, U:2, S:1]"); // Stats show all counts
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

    [Fact]
    public void FormatSummary_WithColumnarLayout_ShouldGroupByStatus()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            OverallSuccess = true,
            HasChanges = true
        };

        // Add namespace with created secrets
        var nsCreated = new NamespaceSummary
        {
            Name = "production",
            Created = 3,
            Updated = 0,
            Skipped = 0,
            Failed = 0,
            Success = true,
            SourceItems = 3
        };
        summary.AddNamespace(nsCreated);

        // Add namespace with updated secrets
        var nsUpdated = new NamespaceSummary
        {
            Name = "staging",
            Created = 0,
            Updated = 2,
            Skipped = 0,
            Failed = 0,
            Success = true,
            SourceItems = 2
        };
        summary.AddNamespace(nsUpdated);

        // Add namespace with up-to-date secrets
        var nsUpToDate = new NamespaceSummary
        {
            Name = "development",
            Created = 0,
            Updated = 0,
            Skipped = 5,
            Failed = 0,
            Success = true,
            SourceItems = 5
        };
        summary.AddNamespace(nsUpToDate);

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().Contain("🆕 CREATED");
        result.Should().Contain("production");
        result.Should().Contain("🔄 UPDATED");
        result.Should().Contain("staging");
        result.Should().Contain("✅ UP-TO-DATE");
        result.Should().Contain("development");
    }

    [Fact]
    public void FormatSummary_WithFailedNamespaces_ShouldShowInFailedColumn()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            OverallSuccess = false,
            HasChanges = false
        };

        // Add namespace with failures
        var nsFailed = new NamespaceSummary
        {
            Name = "production",
            Created = 0,
            Updated = 0,
            Skipped = 0,
            Failed = 2,
            Success = false,
            SourceItems = 2
        };
        nsFailed.Errors.Add("Failed to create secret: permission denied");
        summary.AddNamespace(nsFailed);

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().Contain("❌ FAILED");
        result.Should().Contain("production");
        result.Should().Contain("F:2");
        result.Should().Contain("perm"); // Truncated in horizontal layout
    }

    [Fact]
    public void FormatSummary_WithNamespaceNotFound_ShouldShowInNotFoundColumn()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-10),
            EndTime = DateTime.UtcNow,
            OverallSuccess = false,
            HasChanges = false
        };

        // Add namespace that doesn't exist
        var nsNotFound = new NamespaceSummary
        {
            Name = "nonexistent",
            Created = 0,
            Updated = 0,
            Skipped = 0,
            Failed = 0,
            Success = false,
            SourceItems = 0
        };
        nsNotFound.Errors.Add("Namespace 'nonexistent' not found in cluster");
        summary.AddNamespace(nsNotFound);

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().Contain("⚠️  NOT FOUND");
        result.Should().Contain("nonexistent");
        result.Should().Contain("not f"); // Truncated in horizontal layout
    }

    [Fact]
    public void FormatSummary_WithMixedStatuses_ShouldShowAllColumns()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-15),
            EndTime = DateTime.UtcNow,
            OverallSuccess = false,
            HasChanges = true
        };

        // Created
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "prod-new",
            Created = 5,
            Success = true,
            SourceItems = 5
        });

        // Updated
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "prod-existing",
            Updated = 3,
            Success = true,
            SourceItems = 3
        });

        // Up-to-date
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "staging",
            Skipped = 10,
            Success = true,
            SourceItems = 10
        });

        // Failed
        var nsFailed = new NamespaceSummary
        {
            Name = "qa",
            Failed = 2,
            Success = false,
            SourceItems = 2
        };
        nsFailed.Errors.Add("Authentication error");
        summary.AddNamespace(nsFailed);

        // Not found
        var nsNotFound = new NamespaceSummary
        {
            Name = "dev-temp",
            Success = false,
            SourceItems = 0
        };
        nsNotFound.Errors.Add("Namespace does not exist");
        summary.AddNamespace(nsNotFound);

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().Contain("🆕 CREATED");
        result.Should().Contain("prod-new");
        result.Should().Contain("C:5");
        
        result.Should().Contain("🔄 UPDATED");
        result.Should().Contain("prod-existing");
        result.Should().Contain("U:3");
        
        result.Should().Contain("✅ UP-TO-DATE");
        result.Should().Contain("staging");
        result.Should().Contain("S:10");
        
        result.Should().Contain("❌ FAILED");
        result.Should().Contain("qa");
        result.Should().Contain("F:2");
        result.Should().Contain("Authentication error");
        
        result.Should().Contain("⚠️  NOT FOUND");
        result.Should().Contain("dev-temp");
        result.Should().Contain("does not exist");
    }

    [Fact]
    public void FormatSummary_WithLongErrors_ShouldTruncate()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow,
            OverallSuccess = false,
            HasChanges = false
        };

        var nsFailed = new NamespaceSummary
        {
            Name = "production",
            Failed = 1,
            Success = false,
            SourceItems = 1
        };
        nsFailed.Errors.Add("This is a very long error message that should be truncated because it exceeds the maximum length allowed for display in the summary");
        summary.AddNamespace(nsFailed);

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().Contain("...");
        result.Should().NotContain("maximum length allowed for display");
    }

    [Fact]
    public void FormatSummary_WithMultipleErrorsPerNamespace_ShouldLimitToOne()
    {
        // Arrange
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow,
            OverallSuccess = false,
            HasChanges = false
        };

        var nsFailed = new NamespaceSummary
        {
            Name = "production",
            Failed = 3,
            Success = false,
            SourceItems = 3
        };
        nsFailed.Errors.Add("Error 1: First error");
        nsFailed.Errors.Add("Error 2: Second error");
        nsFailed.Errors.Add("Error 3: Third error");
        nsFailed.Errors.Add("Error 4: Fourth error");
        summary.AddNamespace(nsFailed);

        // Act
        var result = SyncSummaryFormatter.FormatSummary(summary);

        // Assert
        result.Should().Contain("Error 1: First error");
        result.Should().Contain("Error 2: Second error");
        result.Should().Contain("Error 3: Third error");
        result.Should().NotContain("Error 4: Fourth error"); // Only first 3 shown
        result.Should().Contain("and 1 more error(s)"); // Should indicate there are more
    }
}
