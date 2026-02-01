using System.Text.Json;
using Spectre.Console;

namespace VaultwardenK8sSync.E2ETests.Infrastructure;

/// <summary>
/// Generates JSON test results for AI/CI parsing.
/// </summary>
public static class TestResultReporter
{
    public static void GenerateReport(E2ETestFixture fixture, string outputPath)
    {
        var results = fixture.TestResults;
        var passed = results.Count(r => r.Status == "passed");
        var failed = results.Count(r => r.Status == "failed");
        var verdict = failed == 0 ? "PASS" : "FAIL";
        
        var report = new
        {
            test_suite = "vaultwarden-kubernetes-secrets-e2e",
            timestamp = DateTime.UtcNow.ToString("o"),
            duration_seconds = results.Sum(r => r.DurationMs) / 1000.0,
            summary = new
            {
                total = results.Count,
                passed,
                failed,
                skipped = 0
            },
            tests = results.Select(r => new
            {
                name = r.Name,
                status = r.Status,
                duration_ms = r.DurationMs,
                message = r.Message
            }),
            verdict,
            failure_summary = failed > 0 
                ? $"{failed} test(s) failed: {string.Join(", ", results.Where(r => r.Status == "failed").Select(r => r.Name))}"
                : null
        };
        
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
        
        // Print summary
        AnsiConsole.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");
        
        table.AddRow("Total Tests", results.Count.ToString());
        table.AddRow("[green]Passed[/]", passed.ToString());
        table.AddRow("[red]Failed[/]", failed.ToString());
        table.AddRow("Duration", $"{report.duration_seconds:F2}s");
        table.AddRow("Verdict", verdict == "PASS" ? "[green]PASS[/]" : "[red]FAIL[/]");
        
        AnsiConsole.Write(table);
        
        // Output JSON for AI parsing
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[blue]=== JSON RESULTS (for AI/CI parsing) ===[/]");
        AnsiConsole.WriteLine(json);
        AnsiConsole.MarkupLine("[blue]=== END JSON RESULTS ===[/]");
    }
}
