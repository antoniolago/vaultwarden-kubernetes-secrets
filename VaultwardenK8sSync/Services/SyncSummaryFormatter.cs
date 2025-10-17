using System.Text;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public static class SyncSummaryFormatter
{
    public static string FormatSummary(SyncSummary summary, bool isDryRun = false)
    {
        if (summary == null)
        {
            return string.Empty;
        }
        
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine();
        sb.AppendLine("=============================================================");
        sb.AppendLine("         🔄 VAULTWARDEN K8S SYNC SUMMARY 🔄");
        sb.AppendLine("=============================================================");
        sb.AppendLine();
        
        // Overview
        AppendOverview(sb, summary, isDryRun);
        
        // Namespace details
        if (summary.Namespaces?.Any() == true)
        {
            AppendNamespaceDetails(sb, summary.Namespaces, isDryRun);
        }
        
        // Footer
        AppendFooter(sb, summary);
        
        return sb.ToString();
    }
    
    private static void AppendOverview(StringBuilder sb, SyncSummary summary, bool isDryRun)
    {
        var dryRunTag = isDryRun ? " [DRY RUN]" : "";
        var statusIcon = summary.GetStatusIcon();
        var statusText = summary.GetStatusText();
        
        sb.AppendLine($"📊 Sync #{summary.SyncNumber}{dryRunTag}");
        sb.AppendLine($"⏱️  Duration: {summary.Duration.TotalSeconds:F1}s");
        sb.AppendLine($"🎯 Status: {statusIcon} {statusText}");
        sb.AppendLine($"📦 Items from Vaultwarden: {summary.TotalItemsFromVaultwarden}");
        sb.AppendLine($"🌐 Namespaces processed: {summary.TotalNamespaces}");
        sb.AppendLine($"🔄 Changes detected: {(summary.HasChanges ? "Yes" : "No")}");
        sb.AppendLine();
        
        // Stats
        sb.AppendLine("📈 STATISTICS:");
        sb.AppendLine($"   🆕 Created: {summary.TotalSecretsCreated}");
        sb.AppendLine($"   🔄 Updated: {summary.TotalSecretsUpdated}");
        sb.AppendLine($"   ✅ Up-to-date: {summary.TotalSecretsSkipped}");
        sb.AppendLine($"   ❌ Failed: {summary.TotalSecretsFailed}");
        sb.AppendLine($"   🧹 Orphans deleted: {summary.OrphanCleanup?.TotalOrphansDeleted ?? 0}");
        sb.AppendLine($"   ➤  Total processed: {summary.TotalSecretsProcessed}");
        sb.AppendLine();
        
        // Orphan cleanup
        if (summary.OrphanCleanup?.Enabled == true && summary.OrphanCleanup.TotalOrphansFound > 0)
        {
            var orphanStatusIcon = summary.OrphanCleanup.GetStatusIcon();
            var dryRunText = isDryRun ? " (would delete)" : "";
            sb.AppendLine("🧹 ORPHAN CLEANUP:");
            sb.AppendLine($"   {orphanStatusIcon} {summary.OrphanCleanup.TotalOrphansDeleted}/{summary.OrphanCleanup.TotalOrphansFound} deleted{dryRunText}");
            sb.AppendLine();
        }
        
        // Issues
        if (summary?.Errors?.Any() == true || summary?.Warnings?.Any() == true)
        {
            sb.AppendLine("⚠️  ISSUES:");
            
            if (summary.Errors?.Any() == true)
            {
                foreach (var error in summary.Errors)
                {
                    sb.AppendLine($"   ❌ {error}");
                }
            }
            
            if (summary.Warnings?.Any() == true)
            {
                foreach (var warning in summary.Warnings)
                {
                    sb.AppendLine($"   ⚠️  {warning}");
                }
            }
            
            sb.AppendLine();
        }
    }
    
    private static void AppendNamespaceDetails(StringBuilder sb, List<NamespaceSummary> namespaces, bool isDryRun)
    {
        if (namespaces == null || !namespaces.Any()) return;
        
        sb.AppendLine("🌐 NAMESPACE DETAILS:");
        sb.AppendLine();
        
        // Group namespaces by status
        var created = namespaces.Where(n => n.Created > 0 && n.Failed == 0).ToList();
        var updated = namespaces.Where(n => n.Updated > 0 && n.Created == 0 && n.Failed == 0).ToList();
        var upToDate = namespaces.Where(n => n.Skipped > 0 && n.Created == 0 && n.Updated == 0 && n.Failed == 0).ToList();
        var failed = namespaces.Where(n => n.Failed > 0 || !n.Success).ToList();
        var notFound = namespaces.Where(n => n.Errors.Any(e => e.Contains("not found") || e.Contains("does not exist"))).ToList();
        
        // Render each group
        if (created.Any())
        {
            sb.AppendLine("🆕 CREATED:");
            foreach (var ns in created.OrderBy(n => n.Name))
            {
                var stats = GetNamespaceStatsText(ns);
                sb.AppendLine($"   • {ns.Name}{stats}");
                foreach (var error in ns.Errors)
                {
                    sb.AppendLine($"     ↳ {error}");
                }
            }
            sb.AppendLine();
        }
        
        if (updated.Any())
        {
            sb.AppendLine("🔄 UPDATED:");
            foreach (var ns in updated.OrderBy(n => n.Name))
            {
                var stats = GetNamespaceStatsText(ns);
                sb.AppendLine($"   • {ns.Name}{stats}");
                foreach (var error in ns.Errors)
                {
                    sb.AppendLine($"     ↳ {error}");
                }
            }
            sb.AppendLine();
        }
        
        if (upToDate.Any())
        {
            sb.AppendLine("✅ UP-TO-DATE:");
            foreach (var ns in upToDate.OrderBy(n => n.Name))
            {
                var stats = GetNamespaceStatsText(ns);
                sb.AppendLine($"   • {ns.Name}{stats}");
            }
            sb.AppendLine();
        }
        
        if (failed.Any())
        {
            sb.AppendLine("❌ FAILED:");
            foreach (var ns in failed.OrderBy(n => n.Name))
            {
                var stats = GetNamespaceStatsText(ns);
                sb.AppendLine($"   • {ns.Name}{stats}");
                
                // Limit to first 3 errors per namespace and truncate long messages
                var errorsToShow = ns.Errors.Take(3).ToList();
                for (int i = 0; i < errorsToShow.Count; i++)
                {
                    var error = errorsToShow[i];
                    var truncatedError = error.Length > 100 ? error.Substring(0, 100) + "..." : error;
                    sb.AppendLine($"     ↳ Error {i + 1}: {truncatedError}");
                }
                
                if (ns.Errors.Count > 3)
                {
                    sb.AppendLine($"     ↳ ... and {ns.Errors.Count - 3} more error(s)");
                }
            }
            sb.AppendLine();
        }
        
        if (notFound.Any())
        {
            sb.AppendLine("⚠️  NOT FOUND:");
            foreach (var ns in notFound.OrderBy(n => n.Name))
            {
                var stats = GetNamespaceStatsText(ns);
                sb.AppendLine($"   • {ns.Name}{stats}");
                foreach (var error in ns.Errors)
                {
                    sb.AppendLine($"     ↳ {error}");
                }
            }
            sb.AppendLine();
        }
    }
    
    private static string GetNamespaceStatsText(NamespaceSummary ns)
    {
        var stats = new List<string>();
        if (ns.Created > 0) stats.Add($"C:{ns.Created}");
        if (ns.Updated > 0) stats.Add($"U:{ns.Updated}");
        if (ns.Skipped > 0) stats.Add($"S:{ns.Skipped}");
        if (ns.Failed > 0) stats.Add($"F:{ns.Failed}");
        
        return stats.Any() ? $" [{string.Join(", ", stats)}]" : "";
    }
    
    private static void AppendFooter(StringBuilder sb, SyncSummary summary)
    {
        if (summary == null) return;
        
        var statusIcon = summary.GetStatusIcon();
        var endTime = summary.EndTime.ToString("HH:mm:ss");
        
        sb.AppendLine("=============================================================");
        sb.AppendLine($"{statusIcon} Sync completed at {endTime} - Next sync in configured interval");
        sb.AppendLine("=============================================================");
    }
}
