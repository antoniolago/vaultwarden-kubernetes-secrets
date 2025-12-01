using Spectre.Console;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

/// <summary>
/// Formats sync summaries using Spectre.Console for responsive, beautiful terminal output
/// </summary>
public static class SpectreConsoleSummaryFormatter
{
    public static void RenderSummary(SyncSummary summary, bool isDryRun = false)
    {
        if (summary == null) return;

        var dryRunTag = isDryRun ? " [yellow][DRY RUN][/]" : "";
        var statusIcon = summary.GetStatusIcon();
        var statusText = summary.GetStatusText();
        
        // Determine status color based on actual status
        var statusColor = statusText switch
        {
            "SUCCESS" => "green",
            "UP-TO-DATE" => "blue",
            "PARTIAL" => "yellow",
            "FAILED" => "red",
            _ => "white"
        };

        // Detect console width (with reasonable min/max)
        int consoleWidth;
        if (int.TryParse(Environment.GetEnvironmentVariable("CONSOLE_WIDTH"), out var width))
        {
            // Use width from environment variable if set (for testing)
            consoleWidth = Math.Clamp(width, min: 60, max: 120);
        }
        else
        {
            // Otherwise use actual console width
            consoleWidth = Math.Clamp(
                Console.WindowWidth > 0 ? Console.WindowWidth : 80,
                min: 60,  // Absolute minimum
                max: 120  // Maximum width to use
            );
        }
        
        // Header
        AnsiConsole.WriteLine();
        var headerRule = new Rule("[bold cyan]🔄 VAULTWARDEN K8S SYNC SUMMARY 🔄[/]");
        headerRule.Style = Style.Parse("cyan");
        AnsiConsole.Write(headerRule);
        AnsiConsole.WriteLine();

        // Sync Info
        AnsiConsole.MarkupLine("[bold]📊 Sync Info:[/]");
        RenderSyncInfo(summary, dryRunTag, statusIcon, statusText, statusColor);
        AnsiConsole.WriteLine();

        // Quick Stats
        AnsiConsole.MarkupLine("[bold]📈 Quick Stats:[/]");
        RenderQuickStats(summary);
        AnsiConsole.WriteLine();

        // Issues & Cleanup
        AnsiConsole.MarkupLine("[bold]⚠️  Issues & Cleanup:[/]");
        RenderIssuesAndOrphans(summary, isDryRun);
        AnsiConsole.WriteLine();

        // Namespace Details Table
        if (summary.Namespaces?.Any() == true)
        {
            RenderNamespaceDetails(summary.Namespaces);
        }

        // Footer
        AnsiConsole.WriteLine();
        var nextSyncText = summary.SyncIntervalSeconds > 0 
            ? $"Next sync in {summary.SyncIntervalSeconds}s" 
            : "Next sync at configured interval";
        var footerRule = new Rule($"[bold]{statusIcon}  Sync completed at {summary.EndTime:HH:mm:ss}  •  {nextSyncText}[/]");
        footerRule.Style = Style.Parse(statusColor);
        AnsiConsole.Write(footerRule);
    }

    private static void RenderSyncInfo(SyncSummary summary, string dryRunTag, string statusIcon, string statusText, string statusColor)
    {
        AnsiConsole.MarkupLine($"  Sync #{summary.SyncNumber}{dryRunTag}");
        AnsiConsole.MarkupLine($"  ⏱️  Duration: [cyan]{summary.Duration.TotalSeconds:F1}s[/]");
        AnsiConsole.MarkupLine($"  🎯 Status: [{statusColor}]{statusIcon} {statusText}[/]");
        AnsiConsole.MarkupLine($"  📦 Items: [cyan]{summary.TotalItemsFromVaultwarden}[/]");
        AnsiConsole.MarkupLine($"  🌐 Namespaces: [cyan]{summary.TotalNamespaces}[/]");
        AnsiConsole.MarkupLine($"  🔄 Changes: [cyan]{(summary.HasChanges ? "Yes" : "No")}[/]");
    }

    private static void RenderQuickStats(SyncSummary summary)
    {
        AnsiConsole.MarkupLine($"  🆕 Created: [green]{summary.TotalSecretsCreated}[/]");
        AnsiConsole.MarkupLine($"  🔄 Updated: [yellow]{summary.TotalSecretsUpdated}[/]");
        AnsiConsole.MarkupLine($"  ✅ Up-To-Date: [blue]{summary.TotalSecretsSkipped}[/]");
        AnsiConsole.MarkupLine($"  ❌ Failed: [red]{summary.TotalSecretsFailed}[/]");
        AnsiConsole.MarkupLine($"  🧹 Orphans: [magenta]{summary.OrphanCleanup?.TotalOrphansDeleted ?? 0}[/]");
        AnsiConsole.MarkupLine($"  [bold]➤ Total: {summary.TotalSecretsProcessed}[/]");
    }

    private static void RenderIssuesAndOrphans(SyncSummary summary, bool isDryRun)
    {
        var hasContent = false;

        // Orphan cleanup
        if (summary.OrphanCleanup?.Enabled == true && summary.OrphanCleanup.TotalOrphansFound > 0)
        {
            var orphanIcon = summary.OrphanCleanup.GetStatusIcon();
            var dryRunText = isDryRun ? " (would delete)" : "";
            AnsiConsole.MarkupLine($"  [bold]🧹 Orphan Cleanup:[/]");
            AnsiConsole.MarkupLine($"  {orphanIcon} {summary.OrphanCleanup.TotalOrphansDeleted}/{summary.OrphanCleanup.TotalOrphansFound} deleted{dryRunText}");
            hasContent = true;
        }

        // Errors
        if (summary.Errors?.Any() == true)
        {
            if (hasContent) AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold red]Errors:[/]");
            foreach (var error in summary.Errors.Take(3))
            {
                AnsiConsole.MarkupLine($"    [red]❌ {Markup.Escape(error)}[/]");
            }
            if (summary.Errors.Count > 3)
            {
                AnsiConsole.MarkupLine($"    [dim]... and {summary.Errors.Count - 3} more[/]");
            }
            hasContent = true;
        }

        // Warnings
        if (summary.Warnings?.Any() == true)
        {
            if (hasContent) AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [bold yellow]Warnings:[/]");
            foreach (var warning in summary.Warnings.Take(3))
            {
                AnsiConsole.MarkupLine($"    [yellow]⚠️  {Markup.Escape(warning)}[/]");
            }
            if (summary.Warnings.Count > 3)
            {
                AnsiConsole.MarkupLine($"    [dim]... and {summary.Warnings.Count - 3} more[/]");
            }
            hasContent = true;
        }

        if (!hasContent)
        {
            AnsiConsole.MarkupLine("  [dim]No issues[/]");
        }
    }

    private static void RenderNamespaceDetails(List<NamespaceSummary> namespaces)
    {
        var rule = new Rule("[bold]🌐 Namespace Details[/]");
        rule.Style = Style.Parse("cyan");
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Group namespaces by status
        var created = namespaces.Where(n => n.Created > 0 && n.Failed == 0).ToList();
        var updated = namespaces.Where(n => n.Updated > 0 && n.Created == 0 && n.Failed == 0).ToList();
        var upToDate = namespaces.Where(n => n.Skipped > 0 && n.Created == 0 && n.Updated == 0 && n.Failed == 0).ToList();
        var failed = namespaces.Where(n => n.Failed > 0 || !n.Success).ToList();

        // Render success categories in columns
        if (created.Any() || updated.Any() || upToDate.Any())
        {
            RenderSuccessTable(created, updated, upToDate);
        }
        
        // Render failed namespaces in full-width table (more space for errors)
        if (failed.Any())
        {
            AnsiConsole.WriteLine();
            RenderFailedTable(failed);
        }
    }
    
    private static void RenderSuccessTable(List<NamespaceSummary> created, List<NamespaceSummary> updated, List<NamespaceSummary> upToDate)
    {
        var groups = new List<(string Header, string Icon, List<NamespaceSummary> Namespaces)>();
        if (created.Any()) groups.Add(("Created", "🆕", created));
        if (updated.Any()) groups.Add(("Updated", "🔄", updated));
        if (upToDate.Any()) groups.Add(("Up-To-Date", "✅", upToDate));
        
        foreach (var (header, icon, namespaces) in groups)
        {
            AnsiConsole.MarkupLine($"[bold]{icon} {header}:[/]");
            
            foreach (var ns in namespaces)
            {
                // Build stats
                var stats = new List<string>();
                if (ns.Created > 0) stats.Add($"[green]{ns.Created} new[/]");
                if (ns.Updated > 0) stats.Add($"[yellow]{ns.Updated} upd[/]");
                if (ns.Skipped > 0) stats.Add($"[blue]{ns.Skipped} ok[/]");
                var statsText = stats.Any() ? $" ({string.Join(", ", stats)})" : "";
                
                // Show first 3 secret names
                var secretNames = ns.Secrets?
                    .Take(3)
                    .Select(s => Markup.Escape(s.Name))
                    .ToList() ?? new List<string>();
                
                var secretsText = secretNames.Any() 
                    ? $" - [dim]{string.Join(", ", secretNames)}{(ns.Secrets?.Count > 3 ? "..." : "")}[/]" 
                    : "";

                AnsiConsole.MarkupLine($"  • [bold]{Markup.Escape(ns.Name)}[/]{statsText}{secretsText}");
            }
            
            AnsiConsole.WriteLine();
        }
    }
    
    private static void RenderFailedTable(List<NamespaceSummary> failed)
    {
        AnsiConsole.MarkupLine("[bold red]❌ Failed Namespaces:[/]");
        
        foreach (var ns in failed.OrderBy(n => n.Name))
        {
            // Build stats
            var stats = new List<string>();
            if (ns.Created > 0) stats.Add($"[green]{ns.Created} new[/]");
            if (ns.Updated > 0) stats.Add($"[yellow]{ns.Updated} upd[/]");
            if (ns.Skipped > 0) stats.Add($"[blue]{ns.Skipped} ok[/]");
            if (ns.Failed > 0) stats.Add($"[red]{ns.Failed} failed[/]");
            var statsText = stats.Any() ? $" ({string.Join(", ", stats)})" : "";
            
            AnsiConsole.MarkupLine($"  • [bold]{Markup.Escape(ns.Name)}[/]{statsText}");
            
            // Show first 3 errors
            var errors = ns.Errors.Take(3).ToList();
            foreach (var error in errors)
            {
                var truncated = error.Length > 80 ? error.Substring(0, 77) + "..." : error;
                AnsiConsole.MarkupLine($"    [red]→ {Markup.Escape(truncated)}[/]");
            }
            
            if (ns.Errors.Count > 3)
            {
                AnsiConsole.MarkupLine($"    [dim]... and {ns.Errors.Count - 3} more error(s)[/]");
            }
            
            // Show secret names if any
            var secretNames = ns.Secrets?.Take(5).Select(s => s.Name).ToList() ?? new List<string>();
            if (secretNames.Any())
            {
                var secretsList = string.Join(", ", secretNames.Select(Markup.Escape));
                var more = ns.Secrets?.Count > 5 ? "..." : "";
                AnsiConsole.MarkupLine($"    [dim]Secrets: {secretsList}{more}[/]");
            }
            
            AnsiConsole.WriteLine();
        }
    }
}

