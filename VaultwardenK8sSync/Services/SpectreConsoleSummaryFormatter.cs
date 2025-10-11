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

        // Set console to use full width (with fallback for CI environments)
        var consoleWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 120;
        AnsiConsole.Profile.Width = consoleWidth;

        // Header
        AnsiConsole.WriteLine();
        var headerRule = new Rule($"[bold cyan]🔄 VAULTWARDEN K8S SYNC SUMMARY 🔄[/]");
        headerRule.Style = Style.Parse("cyan");
        AnsiConsole.Write(headerRule);
        AnsiConsole.WriteLine();

        // Create main layout with 3 columns
        var layout = new Layout("Root")
            .SplitColumns(
                new Layout("Left"),
                new Layout("Middle"),
                new Layout("Right")
            );

        // Left column: Sync Info
        var syncInfoPanel = new Panel(BuildSyncInfo(summary, dryRunTag, statusIcon, statusText, statusColor))
        {
            Header = new PanelHeader("[bold]📊 Sync Info[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("blue")
        };
        layout["Left"].Update(syncInfoPanel);

        // Middle column: Quick Stats
        var statsPanel = new Panel(BuildQuickStats(summary))
        {
            Header = new PanelHeader("[bold]📈 Quick Stats[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green")
        };
        layout["Middle"].Update(statsPanel);

        // Right column: Issues & Orphans
        var issuesPanel = new Panel(BuildIssuesAndOrphans(summary, isDryRun))
        {
            Header = new PanelHeader("[bold]⚠️  Issues & Cleanup[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("yellow")
        };
        layout["Right"].Update(issuesPanel);

        // Render the layout
        AnsiConsole.Write(layout);
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

    private static Markup BuildSyncInfo(SyncSummary summary, string dryRunTag, string statusIcon, string statusText, string statusColor)
    {
        var text = $"""
            Sync #{summary.SyncNumber}{dryRunTag}
            ⏱️  Duration: [cyan]{summary.Duration.TotalSeconds:F1}s[/]
            🎯 Status: [{statusColor}]{statusIcon} {statusText}[/]
            📦 Items: [cyan]{summary.TotalItemsFromVaultwarden}[/]
            🌐 Namespaces: [cyan]{summary.TotalNamespaces}[/]
            🔄 Changes: [cyan]{(summary.HasChanges ? "Yes" : "No")}[/]
            """;
        return new Markup(text);
    }

    private static Table BuildQuickStats(SyncSummary summary)
    {
        var table = new Table();
        table.Border(TableBorder.None);
        table.HideHeaders();
        table.AddColumn(new TableColumn("Label").LeftAligned());
        table.AddColumn(new TableColumn("Value").RightAligned());
        
        table.AddRow("🆕 Created", $"[green]{summary.TotalSecretsCreated}[/]");
        table.AddRow("🔄 Updated", $"[yellow]{summary.TotalSecretsUpdated}[/]");
        table.AddRow("✅ Up-To-Date", $"[blue]{summary.TotalSecretsSkipped}[/]");
        table.AddRow("❌ Failed", $"[red]{summary.TotalSecretsFailed}[/]");
        table.AddRow("🧹 Orphans", $"[magenta]{summary.OrphanCleanup?.TotalOrphansDeleted ?? 0}[/]");
        table.AddRow("[dim]───────────[/]", "[dim]────[/]");
        table.AddRow("[bold]➤  Total[/]", $"[bold]{summary.TotalSecretsProcessed}[/]");
        
        return table;
    }

    private static Markup BuildIssuesAndOrphans(SyncSummary summary, bool isDryRun)
    {
        var lines = new List<string>();

        // Orphan cleanup
        if (summary.OrphanCleanup?.Enabled == true && summary.OrphanCleanup.TotalOrphansFound > 0)
        {
            var orphanIcon = summary.OrphanCleanup.GetStatusIcon();
            var dryRunText = isDryRun ? " (would delete)" : "";
            lines.Add($"[bold]🧹 Orphan Cleanup[/]");
            lines.Add($"{orphanIcon} {summary.OrphanCleanup.TotalOrphansDeleted}/{summary.OrphanCleanup.TotalOrphansFound} deleted{dryRunText}");
            lines.Add("");
        }

        // Errors
        if (summary.Errors?.Any() == true)
        {
            lines.Add("[bold red]Errors:[/]");
            foreach (var error in summary.Errors.Take(3))
            {
                lines.Add($"[red]❌ {Markup.Escape(error)}[/]");
            }
            if (summary.Errors.Count > 3)
            {
                lines.Add($"[dim]... and {summary.Errors.Count - 3} more[/]");
            }
            lines.Add("");
        }

        // Warnings
        if (summary.Warnings?.Any() == true)
        {
            lines.Add("[bold yellow]Warnings:[/]");
            foreach (var warning in summary.Warnings.Take(3))
            {
                lines.Add($"[yellow]⚠️  {Markup.Escape(warning)}[/]");
            }
            if (summary.Warnings.Count > 3)
            {
                lines.Add($"[dim]... and {summary.Warnings.Count - 3} more[/]");
            }
        }

        if (!lines.Any())
        {
            lines.Add("[dim]No issues[/]");
        }

        return new Markup(string.Join("\n", lines));
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
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderColor(Color.Grey);
        table.ShowRowSeparators();
        
        var groups = new List<(string Header, List<NamespaceSummary> Namespaces)>();
        if (created.Any()) groups.Add(("🆕 Created", created));
        if (updated.Any()) groups.Add(("🔄 Updated", updated));
        if (upToDate.Any()) groups.Add(("✅ Up-To-Date", upToDate));
        
        // Add columns
        foreach (var (header, _) in groups)
        {
            table.AddColumn(new TableColumn($"[bold]{header}[/]").Centered());
        }

        // Find max rows needed
        int maxRows = groups.Max(g => g.Namespaces.Count);

        // Add rows
        for (int i = 0; i < maxRows; i++)
        {
            var rowCells = new List<string>();

            foreach (var (_, groupNamespaces) in groups)
            {
                if (i < groupNamespaces.Count)
                {
                    var ns = groupNamespaces[i];
                    var stats = new List<string>();
                    if (ns.Created > 0) stats.Add($"[green]{ns.Created} created[/]");
                    if (ns.Updated > 0) stats.Add($"[yellow]{ns.Updated} updated[/]");
                    if (ns.Skipped > 0) stats.Add($"[blue]{ns.Skipped} skipped[/]");
                    if (ns.Failed > 0) stats.Add($"[red]{ns.Failed} failed[/]");

                    var statsText = stats.Any() ? $"\n[dim]{string.Join(", ", stats)}[/]" : "";
                    
                    // Show secret names in small font
                    var secretNames = ns.Secrets?.Take(3).Select(s => Markup.Escape(s.Name)).ToList() ?? new List<string>();
                    var secretsText = secretNames.Any() 
                        ? $"\n[dim italic grey]{string.Join(", ", secretNames)}{(ns.Secrets?.Count > 3 ? "..." : "")}[/]" 
                        : "";
                    
                    var errorText = ns.Errors.Any() ? $"\n[dim italic red]{Markup.Escape(ns.Errors.First().Substring(0, Math.Min(35, ns.Errors.First().Length)))}...[/]" : "";

                    rowCells.Add($"[bold]{Markup.Escape(ns.Name)}[/]{statsText}{secretsText}{errorText}");
                }
                else
                {
                    rowCells.Add("");
                }
            }

            table.AddRow(rowCells.ToArray());
        }

        AnsiConsole.Write(table);
    }
    
    private static void RenderFailedTable(List<NamespaceSummary> failed)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderColor(Color.Red);
        table.ShowRowSeparators();
        table.Title("[bold red]❌ Failed Namespaces[/]");
        
        table.AddColumn(new TableColumn("[bold]Namespace[/]").LeftAligned().Width(25));
        table.AddColumn(new TableColumn("[bold]Stats[/]").LeftAligned().Width(12));
        table.AddColumn(new TableColumn("[bold]Errors[/]").LeftAligned());
        
        foreach (var ns in failed.OrderBy(n => n.Name))
        {
            // Build stats
            var stats = new List<string>();
            if (ns.Created > 0) stats.Add($"[green]{ns.Created} created[/]");
            if (ns.Updated > 0) stats.Add($"[yellow]{ns.Updated} updated[/]");
            if (ns.Skipped > 0) stats.Add($"[blue]{ns.Skipped} skipped[/]");
            if (ns.Failed > 0) stats.Add($"[red]{ns.Failed} failed[/]");
            var statsText = stats.Any() ? string.Join("\n", stats) : "[dim]none[/]";
            
            // Build errors (show more errors for failed namespaces)
            var errors = ns.Errors.Take(3).Select(e => $"[red]• {Markup.Escape(e)}[/]").ToList();
            if (ns.Errors.Count > 3)
            {
                errors.Add($"[dim]... and {ns.Errors.Count - 3} more errors[/]");
            }
            var errorsText = errors.Any() ? string.Join("\n", errors) : "[dim]no error details[/]";
            
            // Show secret names if any
            var secretNames = ns.Secrets?.Take(5).Select(s => Markup.Escape(s.Name)).ToList() ?? new List<string>();
            var secretsText = secretNames.Any() 
                ? $"\n[dim italic]{string.Join(", ", secretNames)}{(ns.Secrets?.Count > 5 ? "..." : "")}[/]" 
                : "";
            
            table.AddRow(
                $"[bold]{Markup.Escape(ns.Name)}[/]{secretsText}",
                statsText,
                errorsText
            );
        }
        
        AnsiConsole.Write(table);
    }
}

