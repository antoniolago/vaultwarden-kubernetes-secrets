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
        
        // Header with ASCII art
        sb.AppendLine();
        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║              🔄 VAULTWARDEN K8S SYNC SUMMARY 🔄               ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        
        // Compact overview (all on fewer lines)
        AppendCompactOverview(sb, summary, isDryRun);
        
        // Namespace details (more compact)
        if (summary.Namespaces?.Any() == true)
        {
            AppendCompactNamespaceDetails(sb, summary.Namespaces, isDryRun);
        }
        
        // Footer
        AppendFooter(sb, summary);
        
        return sb.ToString();
    }
    
    private static void AppendCompactOverview(StringBuilder sb, SyncSummary summary, bool isDryRun)
    {
        // Build all sections side by side in columns
        RenderAllSections(sb, summary, isDryRun);
        sb.AppendLine();
    }
    
    private static void RenderAllSections(StringBuilder sb, SyncSummary summary, bool isDryRun)
    {
        const int col1Width = 45;  // Sync Info
        const int col2Width = 45;  // Quick Stats
        
        var dryRunTag = isDryRun ? " [DRY RUN]" : "";
        var statusIcon = summary.GetStatusIcon();
        var statusText = summary.GetStatusText();
        
        // Build column 1 (Sync Info)
        var col1Lines = new List<string>();
        col1Lines.Add($"📊 Sync #{summary.SyncNumber}{dryRunTag}");
        col1Lines.Add($"⏱️  Duration: {summary.Duration.TotalSeconds:F1}s");
        col1Lines.Add($"🎯 Status: {statusIcon} {statusText}");
        col1Lines.Add($"📦 Items from Vaultwarden: {summary.TotalItemsFromVaultwarden}");
        col1Lines.Add($"🌐 Namespaces processed: {summary.TotalNamespaces}");
        col1Lines.Add($"🔄 Changes detected: {(summary.HasChanges ? "Yes" : "No")}");
        
        // Build column 2 (Quick Stats)
        var col2Lines = new List<string>();
        col2Lines.Add("📈 QUICK STATS");
        col2Lines.Add("├─────────────────────────────────────────");
        col2Lines.Add($"│ 🆕 Created: {summary.TotalSecretsCreated,4}");
        col2Lines.Add($"│ 🔄 Updated: {summary.TotalSecretsUpdated,4}");
        col2Lines.Add($"│ ✅ Up-To-Date: {summary.TotalSecretsSkipped,4}");
        col2Lines.Add($"│ ❌ Failed:  {summary.TotalSecretsFailed,4}");
        col2Lines.Add($"│ 🧹 Orphans: {summary.OrphanCleanup?.TotalOrphansDeleted ?? 0,4}");
        col2Lines.Add($"│ ➤  Total:   {summary.TotalSecretsProcessed,4}");
        col2Lines.Add("└─────────────────────────────────────────");
        
        // Build column 3 (Orphan Cleanup + Issues)
        var col3Lines = new List<string>();
        
        // Orphan cleanup section
        if (summary.OrphanCleanup?.Enabled == true && summary.OrphanCleanup.TotalOrphansFound > 0)
        {
            var orphanStatusIcon = summary.OrphanCleanup.GetStatusIcon();
            var dryRunText = isDryRun ? " (would delete)" : "";
            col3Lines.Add("🧹 ORPHAN CLEANUP");
            col3Lines.Add($"  {orphanStatusIcon} {summary.OrphanCleanup.TotalOrphansDeleted}/{summary.OrphanCleanup.TotalOrphansFound} deleted{dryRunText}");
            col3Lines.Add("");
        }
        
        // Issues section
        if (summary?.Errors?.Any() == true || summary?.Warnings?.Any() == true)
        {
            col3Lines.Add("⚠️  ISSUES");
            col3Lines.Add("├────────────────────────────────────────────────────────────────────────────");
            
            if (summary.Errors?.Any() == true)
            {
                foreach (var error in summary.Errors)
                {
                    var shortError = error.Length > 70 ? error.Substring(0, 67) + "..." : error;
                    col3Lines.Add($"│ ❌ {shortError}");
                }
            }
            
            if (summary.Warnings?.Any() == true)
            {
                foreach (var warning in summary.Warnings)
                {
                    var shortWarning = warning.Length > 70 ? warning.Substring(0, 67) + "..." : warning;
                    col3Lines.Add($"│ ⚠️  {shortWarning}");
                }
            }
            
            col3Lines.Add("└────────────────────────────────────────────────────────────────────────────");
        }
        
        // Render all columns side by side
        int maxLines = Math.Max(col1Lines.Count, Math.Max(col2Lines.Count, col3Lines.Count));
        
        for (int i = 0; i < maxLines; i++)
        {
            var line1 = i < col1Lines.Count ? col1Lines[i] : "";
            var line2 = i < col2Lines.Count ? col2Lines[i] : "";
            var line3 = i < col3Lines.Count ? col3Lines[i] : "";
            
            // Pad columns
            var paddedLine1 = line1.PadRight(col1Width);
            var paddedLine2 = line2.PadRight(col2Width);
            
            sb.Append(paddedLine1);
            sb.Append("  ");
            sb.Append(paddedLine2);
            sb.Append("  ");
            sb.AppendLine(line3);
        }
    }
    
    
    private static void AppendCompactNamespaceDetails(StringBuilder sb, List<NamespaceSummary> namespaces, bool isDryRun)
    {
        if (namespaces == null || !namespaces.Any()) return;
        
        sb.AppendLine("🌐 NAMESPACE DETAILS");
        sb.AppendLine("├─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────");
        
        // Group namespaces by status
        var created = namespaces.Where(n => n.Created > 0 && n.Failed == 0).ToList();
        var updated = namespaces.Where(n => n.Updated > 0 && n.Created == 0 && n.Failed == 0).ToList();
        var upToDate = namespaces.Where(n => n.Skipped > 0 && n.Created == 0 && n.Updated == 0 && n.Failed == 0).ToList();
        var failed = namespaces.Where(n => n.Failed > 0 || !n.Success).ToList();
        var notFound = namespaces.Where(n => n.Errors.Any(e => e.Contains("not found") || e.Contains("does not exist"))).ToList();
        
        // Build column data
        var columns = new List<ColumnData>();
        if (created.Any()) columns.Add(new ColumnData { Header = "🆕 CREATED", Namespaces = created });
        if (updated.Any()) columns.Add(new ColumnData { Header = "🔄 UPDATED", Namespaces = updated });
        if (upToDate.Any()) columns.Add(new ColumnData { Header = "✅ UP-TO-DATE", Namespaces = upToDate });
        if (failed.Any()) columns.Add(new ColumnData { Header = "❌ FAILED", Namespaces = failed });
        if (notFound.Any()) columns.Add(new ColumnData { Header = "⚠️  NOT FOUND", Namespaces = notFound });
        
        if (!columns.Any()) return;
        
        // Render columns side by side
        RenderColumnsHorizontally(sb, columns);
        
        sb.AppendLine("└─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine();
    }
    
    private static void RenderColumnsHorizontally(StringBuilder sb, List<ColumnData> columns)
    {
        const int columnWidth = 40;
        
        // Prepare all column lines
        var columnLines = new List<List<string>>();
        int maxLines = 0;
        
        foreach (var column in columns)
        {
            var lines = new List<string>();
            lines.Add(column.Header);
            
            foreach (var ns in column.Namespaces.OrderBy(n => n.Name))
            {
                if (ns == null) continue;
                
                // Build stats string
                var stats = new List<string>();
                if (ns.Created > 0) stats.Add($"C:{ns.Created}");
                if (ns.Updated > 0) stats.Add($"U:{ns.Updated}");
                if (ns.Skipped > 0) stats.Add($"S:{ns.Skipped}");
                if (ns.Failed > 0) stats.Add($"F:{ns.Failed}");
                
                var statsText = stats.Any() ? $" [{string.Join(", ", stats)}]" : "";
                var namespaceLine = $"  • {ns.Name}{statsText}";
                lines.Add(namespaceLine);
                
                // Show errors if any
                if (ns.Errors.Any())
                {
                    foreach (var error in ns.Errors.Take(1)) // Limit to 1 error per namespace for space
                    {
                        var shortError = error.Length > 32 ? error.Substring(0, 29) + "..." : error;
                        lines.Add($"    ↳ {shortError}");
                    }
                }
            }
            
            columnLines.Add(lines);
            maxLines = Math.Max(maxLines, lines.Count);
        }
        
        // Render lines horizontally
        for (int lineIndex = 0; lineIndex < maxLines; lineIndex++)
        {
            sb.Append("│ ");
            
            for (int colIndex = 0; colIndex < columns.Count; colIndex++)
            {
                var lines = columnLines[colIndex];
                var line = lineIndex < lines.Count ? lines[lineIndex] : "";
                
                // Pad to column width
                var paddedLine = line.PadRight(columnWidth);
                if (paddedLine.Length > columnWidth)
                {
                    paddedLine = paddedLine.Substring(0, columnWidth - 3) + "...";
                }
                
                sb.Append(paddedLine);
                
                // Add separator between columns (except last)
                if (colIndex < columns.Count - 1)
                {
                    sb.Append(" │ ");
                }
            }
            
            sb.AppendLine();
        }
    }
    
    private class ColumnData
    {
        public string Header { get; set; } = string.Empty;
        public List<NamespaceSummary> Namespaces { get; set; } = new();
    }
    
    private static void AppendFooter(StringBuilder sb, SyncSummary summary)
    {
        if (summary == null) return;
        
        var statusIcon = summary.GetStatusIcon();
        var endTime = summary.EndTime.ToString("HH:mm:ss");
        
        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine($"║ {statusIcon} Sync completed at {endTime} - Next sync in configured interval ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
    }
}
