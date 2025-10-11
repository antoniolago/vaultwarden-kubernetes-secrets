using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Demo to visualize the Spectre.Console responsive summary format.
/// Run: dotnet test --filter "DisplaySpectreConsoleSummaryDemo"
/// </summary>
public class SpectreConsoleSummaryDemo
{
    [Fact]
    public void DisplaySpectreConsoleSummaryDemo()
    {
        // Create a comprehensive mock summary
        var summary = CreateMockSummary();
        
        // Render using Spectre.Console
        SpectreConsoleSummaryFormatter.RenderSummary(summary);
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
        
        // 🆕 CREATED
        var nsCreated = new NamespaceSummary
        {
            Name = "prod-new",
            Created = 5,
            Success = true,
            SourceItems = 5
        };
        nsCreated.AddSecret(new SecretSummary { Name = "db-credentials", Outcome = ReconcileOutcome.Created });
        nsCreated.AddSecret(new SecretSummary { Name = "api-keys", Outcome = ReconcileOutcome.Created });
        nsCreated.AddSecret(new SecretSummary { Name = "smtp-config", Outcome = ReconcileOutcome.Created });
        summary.AddNamespace(nsCreated);
        
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "prod-api",
            Created = 3,
            Success = true,
            SourceItems = 3
        });
        
        // 🔄 UPDATED
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "staging",
            Updated = 4,
            Skipped = 2,
            Success = true,
            SourceItems = 6
        });
        
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "prod-web",
            Updated = 2,
            Success = true,
            SourceItems = 2
        });
        
        // ✅ UP-TO-DATE
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "development",
            Skipped = 8,
            Success = true,
            SourceItems = 8
        });
        
        summary.AddNamespace(new NamespaceSummary
        {
            Name = "testing",
            Skipped = 5,
            Success = true,
            SourceItems = 5
        });
        
        // ❌ FAILED
        var nsFailed1 = new NamespaceSummary
        {
            Name = "qa",
            Created = 1,
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
            Failed = 1,
            Success = false,
            SourceItems = 1
        };
        nsFailed2.Errors.Add("Authentication error: invalid service account token");
        summary.AddNamespace(nsFailed2);
        
        // ⚠️ NOT FOUND
        var nsNotFound1 = new NamespaceSummary
        {
            Name = "temp-namespace",
            Success = false,
            SourceItems = 0
        };
        nsNotFound1.Errors.Add("Namespace 'temp-namespace' not found in cluster");
        summary.AddNamespace(nsNotFound1);
        
        var nsNotFound2 = new NamespaceSummary
        {
            Name = "old-project",
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
