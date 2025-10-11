using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using Xunit.Abstractions;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Demo class to visualize the columnar summary format.
/// Run this test to see how the summary looks with different namespace statuses.
/// </summary>
public class SummaryFormatterDemo
{
    private readonly ITestOutputHelper _output;

    public SummaryFormatterDemo(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DisplayColumnarSummaryDemo()
    {
        _output.WriteLine("=== COLUMNAR SUMMARY FORMAT DEMO ===\n");
        
        // Create a comprehensive mock summary
        var summary = CreateMockSummary();
        
        // Format and display
        var formatted = SyncSummaryFormatter.FormatSummary(summary);
        _output.WriteLine(formatted);
        
        _output.WriteLine("\n=== END OF DEMO ===");
    }
    
    private static SyncSummary CreateMockSummary()
    {
        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow.AddSeconds(-15),
            EndTime = DateTime.UtcNow,
            OverallSuccess = false,
            HasChanges = true,
            TotalItemsFromVaultwarden = 25,
            SyncNumber = 42
        };
        
        // 🆕 CREATED - Namespaces with newly created secrets
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "prod-new",
            Created = 5,
            Updated = 0,
            Skipped = 0,
            Failed = 0,
            Success = true,
            SourceItems = 5
        });
        
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "prod-api",
            Created = 3,
            Updated = 0,
            Skipped = 0,
            Failed = 0,
            Success = true,
            SourceItems = 3
        });
        
        // 🔄 UPDATED - Namespaces with updated secrets
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "staging",
            Created = 0,
            Updated = 4,
            Skipped = 2,
            Failed = 0,
            Success = true,
            SourceItems = 6
        });
        
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "prod-web",
            Created = 0,
            Updated = 2,
            Skipped = 0,
            Failed = 0,
            Success = true,
            SourceItems = 2
        });
        
        // ✅ UP-TO-DATE - Namespaces with no changes
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "development",
            Created = 0,
            Updated = 0,
            Skipped = 8,
            Failed = 0,
            Success = true,
            SourceItems = 8
        });
        
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "testing",
            Created = 0,
            Updated = 0,
            Skipped = 5,
            Failed = 0,
            Success = true,
            SourceItems = 5
        });
        
        // ❌ FAILED - Namespaces with failures
        var nsFailed1 = new NamespaceSummary
        {
            Name = "qa",
            Created = 1,
            Updated = 0,
            Skipped = 0,
            Failed = 2,
            Success = false,
            SourceItems = 3
        };
        nsFailed1.Errors.Add("Failed to create secret 'db-credentials': permission denied");
        nsFailed1.Errors.Add("Failed to update secret 'api-keys': resource conflict");
        summary.AddNamespace(nsFailed1);
        
        var nsFailed2 = new NamespaceSummary
        {
            Name = "integration",
            Created = 0,
            Updated = 0,
            Skipped = 0,
            Failed = 1,
            Success = false,
            SourceItems = 1
        };
        nsFailed2.Errors.Add("Authentication error: invalid service account token");
        summary.AddNamespace(nsFailed2);
        
        // ⚠️ NOT FOUND - Namespaces that don't exist
        var nsNotFound1 = new NamespaceSummary
        {
            Name = "temp-namespace",
            Created = 0,
            Updated = 0,
            Skipped = 0,
            Failed = 0,
            Success = false,
            SourceItems = 0
        };
        nsNotFound1.Errors.Add("Namespace 'temp-namespace' not found in cluster");
        summary.AddNamespace(nsNotFound1);
        
        var nsNotFound2 = new NamespaceSummary
        {
            Name = "old-project",
            Created = 0,
            Updated = 0,
            Skipped = 0,
            Failed = 0,
            Success = false,
            SourceItems = 0
        };
        nsNotFound2.Errors.Add("Namespace 'old-project' does not exist");
        summary.AddNamespace(nsNotFound2);
        
        // Add orphan cleanup info
        summary.OrphanCleanup = new OrphanCleanupSummary
        {
            Enabled = true,
            Success = true,
            TotalOrphansFound = 3,
            TotalOrphansDeleted = 3
        };
        
        // Add some warnings
        summary.AddWarning("Some items have no namespace configured and were skipped");
        
        return summary;
    }
}
