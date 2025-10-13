using Spectre.Console;
using Spectre.Console.Testing;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Stress test with many namespaces and secrets to see how the display scales
/// </summary>
public class SpectreConsoleStressTest
{
    [Theory]
    [InlineData(false)]  // Test normal width
    [InlineData(true)]   // Test narrow width
    public void DisplayWithManyNamespacesAndSecrets(bool useNarrowWidth)
    {
        // Use test console to avoid duplication
        var testConsole = new TestConsole();
        AnsiConsole.Console = testConsole;
        
        try
        {
            // Create test data
            var summary = new SyncSummary
            {
                StartTime = DateTime.UtcNow.AddMinutes(-5),
                EndTime = DateTime.UtcNow,
                OverallSuccess = false,
                HasChanges = true,
                TotalItemsFromVaultwarden = 150,
                SyncNumber = 99
            };
            
            // Create 10 namespaces with created secrets (lots of secrets)
            for (int i = 1; i <= 10; i++)
            {
                var ns = new NamespaceSummary
                {
                    Name = $"prod-app-{i:D2}",
                    Created = 12,
                    Success = true,
                    SourceItems = 12
                };
                
                // Add many secret names
                for (int j = 1; j <= 12; j++)
                {
                    ns.AddSecret(new SecretSummary 
                    { 
                        Name = $"secret-{i:D2}-{j:D2}", 
                        Outcome = ReconcileOutcome.Created 
                    });
                }
                
                summary.AddNamespace(ns);
            }
            
            // Create 8 namespaces with updated secrets
            for (int i = 1; i <= 8; i++)
            {
                var ns = new NamespaceSummary
                {
                    Name = $"staging-svc-{i:D2}",
                    Updated = 8,
                    Skipped = 4,
                    Success = true,
                    SourceItems = 12
                };
                
                for (int j = 1; j <= 8; j++)
                {
                    ns.AddSecret(new SecretSummary 
                    { 
                        Name = $"config-{i:D2}-{j:D2}", 
                        Outcome = ReconcileOutcome.Updated 
                    });
                }
                
                summary.AddNamespace(ns);
            }
            
            // Create 7 namespaces with up-to-date secrets
            for (int i = 1; i <= 7; i++)
            {
                var ns = new NamespaceSummary
                {
                    Name = $"dev-team-{i:D2}",
                    Skipped = 15,
                    Success = true,
                    SourceItems = 15
                };
                
                for (int j = 1; j <= 15; j++)
                {
                    ns.AddSecret(new SecretSummary 
                    { 
                        Name = $"cred-{i:D2}-{j:D2}", 
                        Outcome = ReconcileOutcome.Skipped 
                    });
                }
                
                summary.AddNamespace(ns);
            }
            
            // Create 5 namespaces with failures (verbose errors)
            for (int i = 1; i <= 5; i++)
            {
                var ns = new NamespaceSummary
                {
                    Name = $"qa-env-{i:D2}",
                    Created = 3,
                    Failed = 5,
                    Success = false,
                    SourceItems = 8
                };
                
                ns.Errors.Add($"Failed to create secret 'database-credentials-{i:D2}': insufficient permissions to access namespace, RBAC policy denies access");
                ns.Errors.Add($"Failed to update secret 'api-keys-{i:D2}': resource version conflict, another process modified the secret");
                ns.Errors.Add($"Failed to create secret 'tls-certificates-{i:D2}': validation error, certificate format is invalid");
                
                for (int j = 1; j <= 3; j++)
                {
                    ns.AddSecret(new SecretSummary 
                    { 
                        Name = $"working-secret-{i:D2}-{j:D2}", 
                        Outcome = ReconcileOutcome.Created 
                    });
                }
                
                summary.AddNamespace(ns);
            }
            
            // Add orphan cleanup
            summary.OrphanCleanup = new OrphanCleanupSummary
            {
                Enabled = true,
                Success = true,
                TotalOrphansFound = 15,
                TotalOrphansDeleted = 15
            };
            
            // Add warnings
            summary.AddWarning("Some items have no namespace configured and were skipped");
            summary.AddWarning("Rate limiting detected, sync may take longer than usual");
            
            // Set a temporary environment variable to simulate narrow width
            var originalWidth = Environment.GetEnvironmentVariable("CONSOLE_WIDTH");
            try
            {
                Environment.SetEnvironmentVariable("CONSOLE_WIDTH", useNarrowWidth ? "80" : "120");
                SpectreConsoleSummaryFormatter.RenderSummary(summary);
            }
            finally
            {
                // Restore original environment
                Environment.SetEnvironmentVariable("CONSOLE_WIDTH", originalWidth);
            }
        }
        finally
        {
            // TestConsole captures output - no need to print again
            // The test framework will show the output automatically
        }
    }
}
