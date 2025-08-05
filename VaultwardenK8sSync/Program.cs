using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Configuration;
using dotenv.net;

namespace VaultwardenK8sSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Load .env file
            DotEnv.Load(options: new DotEnvOptions(probeForEnv: true, probeLevelsToRoot: 2));
            
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            // Build service collection
            var services = new ServiceCollection();

            // Configure logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Configure services
            var appConfig = new AppConfiguration();
            configuration.Bind(appConfig);

            // Validate required configuration
            var requiredKeys = new[] { "Vaultwarden:ServerUrl", "Vaultwarden:Email", "Vaultwarden:MasterPassword" };
            var missingKeys = configuration.GetMissingConfigurationKeys(requiredKeys);
            
            if (missingKeys.Any())
            {
                logger.LogWarning("Missing required configuration keys: {MissingKeys}", string.Join(", ", missingKeys));
                logger.LogInformation("Please check your .env file or appsettings.json");
            }

            // Register configuration
            services.AddSingleton(appConfig.Vaultwarden);
            services.AddSingleton(appConfig.Kubernetes);
            services.AddSingleton(appConfig.Sync);

            // Register services
            services.AddScoped<IVaultwardenService, VaultwardenService>();
            services.AddScoped<IKubernetesService, KubernetesService>();
            services.AddScoped<ISyncService, SyncService>();
            
            // Register V2 services (VwConnector-based)
            services.AddScoped<IVaultwardenServiceV2, VaultwardenServiceV2>();
            services.AddScoped<ISyncServiceV2, SyncServiceV2>();

            // Build service provider
            using var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Vaultwarden Kubernetes Secrets Sync Tool");
            logger.LogInformation("========================================");

            // Parse command line arguments
            var command = args.FirstOrDefault()?.ToLowerInvariant();
            var namespaceArg = args.Skip(1).FirstOrDefault();

            // Initialize services
            var vaultwardenService = serviceProvider.GetRequiredService<IVaultwardenService>();
            var kubernetesService = serviceProvider.GetRequiredService<IKubernetesService>();
            var syncService = serviceProvider.GetRequiredService<ISyncService>();
            
            // Initialize V2 services
            var vaultwardenServiceV2 = serviceProvider.GetRequiredService<IVaultwardenServiceV2>();
            var syncServiceV2 = serviceProvider.GetRequiredService<ISyncServiceV2>();

            // Initialize Kubernetes client
            logger.LogInformation("Initializing Kubernetes client...");
            if (!await kubernetesService.InitializeAsync())
            {
                logger.LogError("Failed to initialize Kubernetes client");
                return 1;
            }

            // Authenticate with Vaultwarden (determine which version to use based on command)
            bool useV2 = command?.StartsWith("v2") == true;
            IVaultwardenService currentVaultwardenService = useV2 ? vaultwardenServiceV2 : vaultwardenService;
            
            logger.LogInformation("Authenticating with Vaultwarden using {Version}...", useV2 ? "VwConnector (V2)" : "bw CLI (V1)");
            if (!await currentVaultwardenService.AuthenticateAsync())
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
                    logger.LogInformation("Starting full sync (V1 - bw CLI)...");
                    success = await syncService.SyncAsync();
                    break;

                case "v2-sync":
                    logger.LogInformation("Starting full sync (V2 - VwConnector)...");
                    success = await syncServiceV2.SyncAsync();
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

                case "v2-sync-namespace":
                    if (string.IsNullOrEmpty(namespaceArg))
                    {
                        logger.LogError("Namespace name is required for v2-sync-namespace command");
                        return 1;
                    }
                    logger.LogInformation("Starting sync for namespace: {Namespace} (V2 - VwConnector)", namespaceArg);
                    success = await syncServiceV2.SyncNamespaceAsync(namespaceArg);
                    break;

                case "cleanup":
                    logger.LogInformation("Starting cleanup of orphaned secrets (V1 - bw CLI)...");
                    success = await syncService.CleanupOrphanedSecretsAsync();
                    break;

                case "v2-cleanup":
                    logger.LogInformation("Starting cleanup of orphaned secrets (V2 - VwConnector)...");
                    success = await syncServiceV2.CleanupOrphanedSecretsAsync();
                    break;

                case "list":
                    logger.LogInformation("Listing Vaultwarden items (V1 - bw CLI)...");
                    var items = await vaultwardenService.GetItemsAsync();
                    var itemsWithNamespace = items.Where(item => !string.IsNullOrEmpty(item.ExtractNamespace())).ToList();
                    
                    logger.LogInformation("Found {Count} items with namespace tags:", itemsWithNamespace.Count);
                    foreach (var item in itemsWithNamespace)
                    {
                        logger.LogInformation("  - {Name} -> {Namespace}", item.Name, item.ExtractNamespace());
                    }
                    success = true;
                    break;

                case "v2-list":
                    logger.LogInformation("Listing Vaultwarden items (V2 - VwConnector)...");
                    var itemsV2 = await vaultwardenServiceV2.GetItemsAsync();
                    var itemsWithNamespaceV2 = itemsV2.Where(item => !item.Deleted && !string.IsNullOrEmpty(item.ExtractNamespace())).ToList();
                    
                    logger.LogInformation("Found {Count} active items with namespace tags:", itemsWithNamespaceV2.Count);
                    foreach (var item in itemsWithNamespaceV2)
                    {
                        logger.LogInformation("  - {Name} -> {Namespace}", item.Name, item.ExtractNamespace());
                    }
                    success = true;
                    break;

                case "config":
                    logger.LogInformation("Validating configuration...");
                    var configKeys = new[] { "Vaultwarden:ServerUrl", "Vaultwarden:Email", "Vaultwarden:MasterPassword" };
                    var missingConfigKeys = configuration.GetMissingConfigurationKeys(configKeys);
                    
                    if (!missingConfigKeys.Any())
                    {
                        logger.LogInformation("✓ All required configuration keys are present");
                        logger.LogInformation("✓ Vaultwarden Server: {Server}", configuration.GetValue("Vaultwarden:ServerUrl"));
                        logger.LogInformation("✓ Vaultwarden Email: {Email}", configuration.GetValue("Vaultwarden:Email"));
                        logger.LogInformation("✓ Kubernetes Default Namespace: {Namespace}", configuration.GetValue("Kubernetes:DefaultNamespace", "default"));
                        logger.LogInformation("✓ Sync Secret Prefix: {Prefix}", configuration.GetValue("Sync:SecretPrefix", "vaultwarden-"));
                    }
                    else
                    {
                        logger.LogWarning("✗ Missing required configuration keys: {MissingKeys}", string.Join(", ", missingConfigKeys));
                        logger.LogInformation("Please check your .env file or appsettings.json");
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
            await currentVaultwardenService.LogoutAsync();

            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine(@"
Vaultwarden Kubernetes Secrets Sync Tool

Usage:
  VaultwardenK8sSync [command] [options]

Commands (V1 - bw CLI):
  sync                    Perform full sync of all Vaultwarden items to Kubernetes secrets
  sync-namespace <name>   Sync only items for a specific namespace
  cleanup                 Clean up orphaned secrets (secrets that no longer exist in Vaultwarden)
  list                    List all Vaultwarden items with namespace tags

Commands (V2 - VwConnector):
  v2-sync                 Perform full sync using VwConnector library (no bw CLI required)
  v2-sync-namespace <name> Sync only items for a specific namespace using VwConnector
  v2-cleanup              Clean up orphaned secrets using VwConnector
  v2-list                 List all Vaultwarden items with namespace tags using VwConnector

General Commands:
  config                  Validate and display current configuration
  help                    Show this help message

Configuration:
  The tool reads configuration from:
  - .env file (recommended for sensitive data)
  - appsettings.json
  - appsettings.Development.json
  - Environment variables
  - Command line arguments

  Create a .env file from env.example for secure configuration.

Namespace Tagging:
  To sync a Vaultwarden item to a Kubernetes namespace, add the following to the item's description:
  #namespace:your-namespace-name

Examples:
  VaultwardenK8sSync config                  # Validate configuration
  VaultwardenK8sSync sync                    # Using bw CLI
  VaultwardenK8sSync v2-sync                 # Using VwConnector
  VaultwardenK8sSync sync-namespace production
  VaultwardenK8sSync v2-sync-namespace production
  VaultwardenK8sSync cleanup
  VaultwardenK8sSync v2-cleanup
  VaultwardenK8sSync list
  VaultwardenK8sSync v2-list
");
    }
}
