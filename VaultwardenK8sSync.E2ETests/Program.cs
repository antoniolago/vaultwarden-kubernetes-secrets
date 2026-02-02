using Spectre.Console;
using VaultwardenK8sSync.E2ETests.Infrastructure;

// This file enables running e2e tests as a standalone console app
// for easier debugging and CI integration.
// 
// Usage: dotnet run --project VaultwardenK8sSync.E2ETests
// 
// For xUnit test runner: dotnet test VaultwardenK8sSync.E2ETests

AnsiConsole.Write(new Rule("[blue]Vaultwarden Kubernetes Secrets - E2E Tests[/]").RuleStyle("blue"));
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("Run tests using: [yellow]dotnet test VaultwardenK8sSync.E2ETests[/]");
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("Or run with verbose output:");
AnsiConsole.MarkupLine("  [yellow]dotnet test VaultwardenK8sSync.E2ETests --logger \"console;verbosity=detailed\"[/]");
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("Environment variables:");
AnsiConsole.MarkupLine("  [cyan]E2E_KEEP_CLUSTER[/]=true  - Keep cluster after tests for debugging");
AnsiConsole.WriteLine();

return 0;
