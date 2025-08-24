using System.Text;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public static class SyncSummaryFormatter
{
    public static string FormatSummary(SyncSummary summary, bool isDryRun = false)
    {
        var sb = new StringBuilder();
        
        // Header with ASCII art
        sb.AppendLine();
        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║              🔄 VAULTWARDEN K8S SYNC SUMMARY 🔄               ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        
        // Compact overview (all on fewer lines)
        AppendCompactOverview(sb, summary, isDryRun);
        
        // Namespace details (more compact)
        if (summary.Namespaces.Any())
        {
            AppendCompactNamespaceDetails(sb, summary.Namespaces, isDryRun);
        }
        
        // Orphan cleanup (more compact)
        if (summary.OrphanCleanup != null && summary.OrphanCleanup.Enabled)
        {
            AppendCompactOrphanCleanup(sb, summary.OrphanCleanup, isDryRun);
        }
        
        // Errors and warnings (more compact)
        AppendCompactIssues(sb, summary);
        
        // Footer
        AppendFooter(sb, summary);
        
        return sb.ToString();
    }
    
    private static void AppendCompactOverview(StringBuilder sb, SyncSummary summary, bool isDryRun)
    {
        var dryRunTag = isDryRun ? " [DRY RUN]" : "";
        var statusIcon = summary.GetStatusIcon();
        var statusText = summary.GetStatusText();
        
        // Combine sync info and stats on fewer lines
        sb.AppendLine($"📊 Sync #{summary.SyncNumber}{dryRunTag}");
        sb.AppendLine($"⏱️  Duration: {summary.Duration.TotalSeconds:F1}s");
        sb.AppendLine($"🎯 Status: {statusIcon} {statusText}");
        sb.AppendLine($"📦 Items from Vaultwarden: {summary.TotalItemsFromVaultwarden}");
        sb.AppendLine($"🌐 Namespaces processed: {summary.TotalNamespaces}");
        sb.AppendLine($"🔄 Changes detected: {(summary.HasChanges ? "Yes" : "No")}");
        sb.AppendLine();
        
        // Compact stats on one line
        sb.AppendLine("📈 QUICK STATS");
        sb.AppendLine("├─────────────────────────────────────────");
        sb.AppendLine($"│ 🆕 Created: {summary.TotalSecretsCreated,4}");
        sb.AppendLine($"│ 🔄 Updated: {summary.TotalSecretsUpdated,4}");
        sb.AppendLine($"│ ✅ Up-To-Date: {summary.TotalSecretsSkipped,4}");
        sb.AppendLine($"│ ❌ Failed:  {summary.TotalSecretsFailed,4}");
        sb.AppendLine($"│ 🧹 Orphans: {summary.OrphanCleanup?.TotalOrphansDeleted ?? 0,4}");
        sb.AppendLine($"│ ➤  Total:   {summary.TotalSecretsProcessed,4}");
        sb.AppendLine("└─────────────────────────────────────────");
        sb.AppendLine();
    }
    
    private static void AppendCompactNamespaceDetails(StringBuilder sb, List<NamespaceSummary> namespaces, bool isDryRun)
    {
        sb.AppendLine("🌐 NAMESPACE DETAILS");
        sb.AppendLine("├─────────────────────────────────────────");
        
        foreach (var ns in namespaces.OrderBy(n => n.Name))
        {
            var statusIcon = ns.GetStatusIcon();
            
            // Compact namespace header with stats on one line
            var parts = new List<string>();
            if (ns.Created > 0) parts.Add($"🆕 {ns.Created}");
            if (ns.Updated > 0) parts.Add($"🔄 {ns.Updated}");
            if (ns.Skipped > 0) parts.Add($"✅ {ns.Skipped}");
            if (ns.Failed > 0) parts.Add($"❌ {ns.Failed}");
            
            var resultsText = parts.Any() ? $" → Results: {string.Join(" ", parts)}" : "";
            sb.AppendLine($"│ {statusIcon} {ns.Name}");
            sb.AppendLine($"│   └─ Items: {ns.SourceItems} → Secrets: {ns.Created + ns.Updated + ns.Skipped + ns.Failed}{resultsText}");
            
            // Show ALL secrets with outcomes (not filtered)
            var allSecrets = ns.Secrets.Where(s => 
                s.Outcome == ReconcileOutcome.Failed || 
                s.Outcome == ReconcileOutcome.Created ||
                s.Outcome == ReconcileOutcome.Updated ||
                s.Outcome == ReconcileOutcome.Skipped).ToList();
                
            if (allSecrets.Any())
            {
                foreach (var secret in allSecrets)
                {
                    var secretIcon = secret.GetStatusIcon();
                    var secretStatus = secret.GetStatusText();
                    sb.AppendLine($"│      • {secretIcon} {secret.Name}: {secretStatus}");
                }
            }
            
            sb.AppendLine("│");
        }
        
        sb.AppendLine("└─────────────────────────────────────────");
        sb.AppendLine();
    }
    
    private static void AppendCompactOrphanCleanup(StringBuilder sb, OrphanCleanupSummary cleanup, bool isDryRun)
    {
        // Only show if there were orphans found/deleted
        if (cleanup.TotalOrphansFound > 0)
        {
            var statusIcon = cleanup.GetStatusIcon();
            var dryRunText = isDryRun ? " (would delete)" : "";
            
            sb.AppendLine($"🧹 Orphan Cleanup: {statusIcon} {cleanup.TotalOrphansDeleted}/{cleanup.TotalOrphansFound} deleted{dryRunText}");
            sb.AppendLine();
        }
    }
    
    private static void AppendCompactIssues(StringBuilder sb, SyncSummary summary)
    {
        if (summary.Errors.Any() || summary.Warnings.Any())
        {
            sb.AppendLine("⚠️  ISSUES");
            sb.AppendLine("├─────────────────────────────────────────");
            
            foreach (var error in summary.Errors)
            {
                sb.AppendLine($"│ ❌ {error}");
            }
            
            foreach (var warning in summary.Warnings)
            {
                sb.AppendLine($"│ ⚠️  {warning}");
            }
            
            sb.AppendLine("└─────────────────────────────────────────");
            sb.AppendLine();
        }
    }
    
    private static void AppendFooter(StringBuilder sb, SyncSummary summary)
    {
        var statusIcon = summary.GetStatusIcon();
        var endTime = summary.EndTime.ToString("HH:mm:ss");
        
        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine($"║ {statusIcon} Sync completed at {endTime} - Next sync in configured interval ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
    }
}
