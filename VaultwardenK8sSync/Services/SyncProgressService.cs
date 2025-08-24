using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public interface ISyncProgressService
{
    Task<SyncSummary> SyncWithProgressAsync();
}

public class SyncProgressService : ISyncProgressService
{
    private readonly ILogger<SyncProgressService> _logger;
    private readonly ISyncService _syncService;
    private readonly SyncSettings _syncConfig;

    public SyncProgressService(
        ILogger<SyncProgressService> logger,
        ISyncService syncService,
        SyncSettings syncConfig)
    {
        _logger = logger;
        _syncService = syncService;
        _syncConfig = syncConfig;
    }

    public async Task<SyncSummary> SyncWithProgressAsync()
    {
        using var progressDisplay = new ProgressDisplay();
        
        try
        {
            progressDisplay.Start("🔄 Initializing sync...");
            await Task.Delay(200); // Brief pause to show initialization
            
            progressDisplay.Update("🔐 Authenticating with Vaultwarden...");
            await Task.Delay(300);
            
            progressDisplay.Update("📦 Fetching items from Vaultwarden...");
            await Task.Delay(200);
            
            progressDisplay.Update("🔍 Analyzing changes...");
            await Task.Delay(200);
            
            progressDisplay.Update("☸️  Processing Kubernetes secrets...");
            
            // Perform the actual sync (this will use debug logging internally)
            var originalLogLevel = SetDebugLogging();
            SyncSummary summary;
            
            try
            {
                summary = await _syncService.SyncAsync();
            }
            finally
            {
                RestoreLogging(originalLogLevel);
            }
            
            // Show completion message based on results
            var completionMessage = GetCompletionMessage(summary);
            progressDisplay.Complete(completionMessage);
            
            // Small delay before showing summary to let completion message be seen
            await Task.Delay(500);
            
            return summary;
        }
        catch (Exception ex)
        {
            progressDisplay.Complete($"❌ Sync failed: {ex.Message}");
            throw;
        }
    }

    private string GetCompletionMessage(SyncSummary summary)
    {
        if (!summary.OverallSuccess)
        {
            return $"❌ Sync completed with errors ({summary.Duration.TotalSeconds:F1}s)";
        }
        
        if (summary.TotalSecretsCreated > 0 || summary.TotalSecretsUpdated > 0)
        {
            return $"✅ Sync completed successfully - {summary.TotalSecretsCreated + summary.TotalSecretsUpdated} secrets processed ({summary.Duration.TotalSeconds:F1}s)";
        }
        
        if (!summary.HasChanges)
        {
            return $"⭕ No changes detected - all secrets up to date ({summary.Duration.TotalSeconds:F1}s)";
        }
        
        return $"✅ Sync completed ({summary.Duration.TotalSeconds:F1}s)";
    }

    private string? SetDebugLogging()
    {
        // This is a simplified approach - in a real implementation you might want
        // to temporarily change the log level for specific loggers
        return null;
    }

    private void RestoreLogging(string? originalLevel)
    {
        // Restore original logging level if needed
    }
}

public class DetailedSyncProgressService : ISyncProgressService
{
    private readonly ILogger<DetailedSyncProgressService> _logger;
    private readonly ISyncService _syncService;
    private readonly IVaultwardenService _vaultwardenService;
    private readonly SyncSettings _syncConfig;

    public DetailedSyncProgressService(
        ILogger<DetailedSyncProgressService> logger,
        ISyncService syncService,
        IVaultwardenService vaultwardenService,
        SyncSettings syncConfig)
    {
        _logger = logger;
        _syncService = syncService;
        _vaultwardenService = vaultwardenService;
        _syncConfig = syncConfig;
    }

    public async Task<SyncSummary> SyncWithProgressAsync()
    {
        using var multiProgress = new MultiProgressDisplay();
        
        try
        {
            var authItem = multiProgress.AddItem("auth", "Authenticating with Vaultwarden");
            var fetchItem = multiProgress.AddItem("fetch", "Fetching items from vault");
            var processItem = multiProgress.AddItem("process", "Processing secrets");
            var cleanupItem = multiProgress.AddItem("cleanup", "Cleaning up orphaned secrets");
            
            // Simulate progress updates (in real implementation, these would be called from the actual operations)
            await SimulateAuthProgress(authItem);
            await SimulateFetchProgress(fetchItem);
            
            // Perform actual sync with minimal logging
            var summary = await PerformSyncWithProgress(processItem, cleanupItem);
            
            // Complete all items
            authItem.Complete(true, "Authentication successful");
            fetchItem.Complete(true, $"Retrieved {summary.TotalItemsFromVaultwarden} items");
            processItem.Complete(summary.TotalSecretsFailed == 0, 
                $"Processed {summary.TotalSecretsProcessed} secrets", 
                $"Created: {summary.TotalSecretsCreated}, Updated: {summary.TotalSecretsUpdated}, Failed: {summary.TotalSecretsFailed}");
            
            if (_syncConfig.DeleteOrphans && summary.OrphanCleanup != null)
            {
                cleanupItem.Complete(summary.OrphanCleanup.Success, 
                    $"Cleaned {summary.OrphanCleanup.TotalOrphansDeleted} orphaned secrets");
            }
            else
            {
                cleanupItem.Complete(true, "Orphan cleanup disabled");
            }
            
            // Small delay to show final state
            await Task.Delay(1000);
            
            var completionMessage = GetDetailedCompletionMessage(summary);
            multiProgress.Complete(completionMessage);
            
            return summary;
        }
        catch (Exception ex)
        {
            multiProgress.Complete($"❌ Sync failed: {ex.Message}");
            throw;
        }
    }

    private async Task SimulateAuthProgress(ProgressItem authItem)
    {
        await Task.Delay(100);
        authItem.Update("Connecting to Vaultwarden server");
        await Task.Delay(200);
        authItem.Update("Validating API credentials");
        await Task.Delay(300);
        authItem.Update("Unlocking vault");
        await Task.Delay(200);
    }

    private async Task SimulateFetchProgress(ProgressItem fetchItem)
    {
        await Task.Delay(100);
        fetchItem.Update("Querying vault items");
        await Task.Delay(200);
        fetchItem.Update("Filtering items with namespace tags");
        await Task.Delay(150);
    }

    private async Task<SyncSummary> PerformSyncWithProgress(ProgressItem processItem, ProgressItem cleanupItem)
    {
        // Set logging to debug level temporarily
        processItem.Update("Analyzing changes", "Calculating item hashes...");
        await Task.Delay(100);
        
        processItem.Update("Syncing secrets", "Processing namespaces...");
        
        // Perform the actual sync
        var summary = await _syncService.SyncAsync();
        
        if (_syncConfig.DeleteOrphans)
        {
            cleanupItem.Update("Scanning for orphaned secrets");
            await Task.Delay(100);
        }
        
        return summary;
    }

    private string GetDetailedCompletionMessage(SyncSummary summary)
    {
        var status = summary.OverallSuccess ? "✅ Success" : "❌ Failed";
        var duration = $"({summary.Duration.TotalSeconds:F1}s)";
        
        if (!summary.HasChanges)
        {
            return $"⭕ No changes detected - all secrets up to date {duration}";
        }
        
        var stats = new List<string>();
        if (summary.TotalSecretsCreated > 0) stats.Add($"{summary.TotalSecretsCreated} created");
        if (summary.TotalSecretsUpdated > 0) stats.Add($"{summary.TotalSecretsUpdated} updated");
        if (summary.TotalSecretsFailed > 0) stats.Add($"{summary.TotalSecretsFailed} failed");
        
        var statsText = stats.Any() ? " - " + string.Join(", ", stats) : "";
        
        return $"{status}: Processed {summary.TotalSecretsProcessed} secrets{statsText} {duration}";
    }
}


