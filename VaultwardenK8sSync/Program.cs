using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using dotenv.net;

namespace VaultwardenK8sSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Load .env file
            DotEnv.Load(options: new DotEnvOptions(probeForEnv: true));
            
            // Load configuration from environment variables
            var appSettings = AppSettings.FromEnvironment();

            // Build service collection
            var services = new ServiceCollection();

            // Configure logging (levels from env via LoggingSettings)
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddConsole();
                builder.AddDebug();
                builder.SetMinimumLevel(ParseLogLevel(appSettings.Logging.DefaultLevel));
                builder.AddFilter("Microsoft", ParseLogLevel(appSettings.Logging.MicrosoftLevel));
                builder.AddFilter("Microsoft.Hosting.Lifetime", ParseLogLevel(appSettings.Logging.MicrosoftHostingLifetimeLevel));
            });

            // Register configuration
            services.AddSingleton(appSettings.Vaultwarden);
            services.AddSingleton(appSettings.Kubernetes);
            services.AddSingleton(appSettings.Sync);

            // Register services
            services.AddScoped<IVaultwardenService, VaultwardenService>();
            services.AddScoped<IKubernetesService, KubernetesService>();
            services.AddScoped<ISyncService, SyncService>();
            
            // Build service provider
            using var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // Global exception logging
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null)
                {
                    logger.LogCritical(ex, "Unhandled exception");
                }
                else
                {
                    logger.LogCritical("Unhandled exception: {Info}", e.ExceptionObject);
                }
            };
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                logger.LogCritical(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            // Validate configuration
            if (!appSettings.Validate(out var validationResults))
            {
                logger.LogWarning("Configuration validation failed:");
                foreach (var result in validationResults)
                {
                    logger.LogWarning("  - {Error}", result.ErrorMessage);
                }
                logger.LogInformation("Please check your .env file or environment variables");
            }

            // API key is the only supported mode; AppSettings validation already enforces ClientId/ClientSecret

            logger.LogInformation("Vaultwarden Kubernetes Secrets Sync Tool");
            logger.LogInformation("========================================");
            logger.LogInformation(
                "Log levels -> Default: {Default}, Microsoft: {Ms}, Microsoft.Hosting.Lifetime: {MsLifetime}",
                appSettings.Logging.DefaultLevel,
                appSettings.Logging.MicrosoftLevel,
                appSettings.Logging.MicrosoftHostingLifetimeLevel);

            // Parse command line arguments
            var command = args.FirstOrDefault()?.ToLowerInvariant();
            var namespaceArg = args.Skip(1).FirstOrDefault();

            // Initialize services
            var vaultwardenService = serviceProvider.GetRequiredService<IVaultwardenService>();
            var kubernetesService = serviceProvider.GetRequiredService<IKubernetesService>();
            var syncService = serviceProvider.GetRequiredService<ISyncService>();
            

            // Initialize Kubernetes client
            logger.LogInformation("Initializing Kubernetes client...");
            if (!await kubernetesService.InitializeAsync())
            {
                logger.LogError("Failed to initialize Kubernetes client");
                return 1;
            }

            // Authenticate with Vaultwarden
            logger.LogInformation("Authenticating with Vaultwarden...");
            if (!await vaultwardenService.AuthenticateAsync())
            {
                logger.LogError("Failed to authenticate with Vaultwarden");
                return 1;
            }

            // Execute command
            bool success = false;
            switch (command)
            {
                case "sync":
                case null:
                    if (appSettings.Sync.ContinuousSync)
                    {
                        logger.LogInformation("Starting continuous sync with interval {Interval} seconds...", appSettings.Sync.SyncIntervalSeconds);
                        success = await RunContinuousSyncAsync(syncService, vaultwardenService, logger, appSettings.Sync);
                    }
                    else
                    {
                        logger.LogInformation("Starting full sync...");
                        success = await syncService.SyncAsync();
                    }
                    break;

                case "sync-namespace":
                    if (string.IsNullOrEmpty(namespaceArg))
                    {
                        logger.LogError("Namespace name is required for sync-namespace command");
                        return 1;
                    }
                    logger.LogInformation("Starting sync for namespace: {Namespace} (V1 - bw CLI)", namespaceArg);
                    success = await syncService.SyncNamespaceAsync(namespaceArg);
                    break;

                case "cleanup":
                    logger.LogInformation("Starting cleanup of orphaned secrets (V1 - bw CLI)...");
                    success = await syncService.CleanupOrphanedSecretsAsync();
                    break;

                case "list":
                    logger.LogInformation("Listing Vaultwarden items...");
                    var items = await vaultwardenService.GetItemsAsync();
                    var itemsWithNamespace = items.Where(item => item.ExtractNamespaces().Any()).ToList();
                    
                    logger.LogInformation("Found {Count} items with namespace tags:", itemsWithNamespace.Count);
                    foreach (var item in itemsWithNamespace)
                    {
                        var namespaces = item.ExtractNamespaces();
                        var namespaceList = string.Join(", ", namespaces);
                        logger.LogInformation("  - {Name} -> {Namespaces}", item.Name, namespaceList);
                    }
                    success = true;
                    break;

                case "export":
                    if (string.IsNullOrEmpty(namespaceArg))
                    {
                        logger.LogError("Secret name is required for export command. Usage: export <secret-name> [namespace]");
                        return 1;
                    }
                    
                    var secretName = namespaceArg;
                    var exportNamespace = args.Skip(2).FirstOrDefault() ?? appSettings.Kubernetes.DefaultNamespace;
                    
                    logger.LogInformation("Exporting secret {SecretName} from namespace {Namespace}...", secretName, exportNamespace);
                    var yamlOutput = await kubernetesService.ExportSecretAsYamlAsync(exportNamespace, secretName);
                    
                    if (yamlOutput != null)
                    {
                        Console.WriteLine(yamlOutput);
                        success = true;
                    }
                    else
                    {
                        logger.LogError("Secret {SecretName} not found in namespace {Namespace}", secretName, exportNamespace);
                        success = false;
                    }
                    break;

                case "config":
                    logger.LogInformation("Validating configuration...");
                    
                    if (appSettings.Validate(out var configValidationResults))
                    {
                        logger.LogInformation("✓ All required configuration keys are present");
                        logger.LogInformation("✓ Vaultwarden Server: {Server}", appSettings.Vaultwarden.ServerUrl);
                         logger.LogInformation("✓ Kubernetes Default Namespace: {Namespace}", appSettings.Kubernetes.DefaultNamespace);
                         // Prefix removed; secret name defaults to sanitized item name unless #secret-name is set
                        logger.LogInformation("✓ Dry Run Mode: {DryRun}", appSettings.Sync.DryRun);
                         logger.LogInformation("✓ Auth Mode: API Key (default)");
                    }
                    else
                    {
                        logger.LogWarning("✗ Configuration validation failed:");
                        foreach (var result in configValidationResults)
                        {
                            logger.LogWarning("  - {Error}", result.ErrorMessage);
                        }
                        logger.LogInformation("Please check your .env file or environment variables");
                    }
                    success = true;
                    break;

                case "help":
                    ShowHelp();
                    success = true;
                    break;

                default:
                    logger.LogError("Unknown command: {Command}", command);
                    ShowHelp();
                    return 1;
            }

            // Logout from Vaultwarden
            await vaultwardenService.LogoutAsync();

            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            // Log full stack trace to aid debugging even if logger is not available
            Console.WriteLine($"Fatal error: {ex}");
            return 1;
        }
    }

    private static LogLevel ParseLogLevel(string? configuredLevel)
    {
        if (string.IsNullOrWhiteSpace(configuredLevel))
        {
            return LogLevel.Information;
        }
        if (Enum.TryParse<LogLevel>(configuredLevel, ignoreCase: true, out var level))
        {
            return level;
        }
        return LogLevel.Information;
    }

    private static async Task<bool> RunContinuousSyncAsync(ISyncService syncService, IVaultwardenService vaultwardenService, ILogger logger, SyncSettings syncConfig)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        
        // Handle graceful shutdown
        Console.CancelKeyPress += (sender, e) =>
        {
            logger.LogInformation("Received shutdown signal, stopping continuous sync...");
            e.Cancel = true; // Prevent immediate termination
            cancellationTokenSource.Cancel();
        };

        try
        {
            var runCount = 0;
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                runCount++;
                logger.LogInformation("Starting sync run #{RunCount}...", runCount);
                
                try
                {
                    var syncSuccess = await syncService.SyncAsync();
                    if (syncSuccess)
                    {
                        logger.LogInformation("Sync run #{RunCount} completed successfully", runCount);
                    }
                    else
                    {
                        logger.LogWarning("Sync run #{RunCount} completed with errors", runCount);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Sync run #{RunCount} failed with exception", runCount);
                }

                if (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    logger.LogInformation("Waiting {Interval} seconds before next sync...", syncConfig.SyncIntervalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(syncConfig.SyncIntervalSeconds), cancellationTokenSource.Token);
                }
            }

            logger.LogInformation("Continuous sync stopped gracefully");
            return true;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Continuous sync was cancelled");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Continuous sync failed with exception");
            return false;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine(@"
Vaultwarden Kubernetes Secrets Sync Tool

Usage:
  VaultwardenK8sSync [command] [options]

Commands:
  sync                    Perform full sync of all Vaultwarden items to Kubernetes secrets
                          (Use SYNC__CONTINUOUSSYNC=true for continuous operation)
  sync-namespace <name>   Sync only items for a specific namespace
  cleanup                 Clean up orphaned secrets (secrets that no longer exist in Vaultwarden)
  list                    List all Vaultwarden items with namespace tags
  export <secret> [ns]    Export a secret as YAML with proper multiline formatting



General Commands:
  config                  Validate and display current configuration
  help                    Show this help message

Configuration:
  The tool reads configuration from environment variables:
  - .env file (recommended for sensitive data)
  - System environment variables
  - Launch configuration environment variables

  Create a .env file from env.example for secure configuration.

Namespace Tagging:
  To sync a Vaultwarden item to a Kubernetes namespace, add the following to the item's description:
  #namespaces:your-namespace-name

Examples:
  VaultwardenK8sSync config                  # Validate configuration
  VaultwardenK8sSync sync                    # Perform full sync
  VaultwardenK8sSync sync-namespace production
  VaultwardenK8sSync cleanup
  VaultwardenK8sSync list
  VaultwardenK8sSync export my-secret        # Export secret with proper YAML formatting
  VaultwardenK8sSync export my-secret prod   # Export secret from specific namespace
");
    }
}
