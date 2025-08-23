using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.Application;

public interface ICommandHandler
{
    Task<bool> HandleCommandAsync(string[] args);
}

public class CommandHandler : ICommandHandler
{
    private readonly ILogger<CommandHandler> _logger;
    private readonly ISyncService _syncService;
    private readonly IVaultwardenService _vaultwardenService;
    private readonly IKubernetesService _kubernetesService;
    private readonly AppSettings _appSettings;

    public CommandHandler(
        ILogger<CommandHandler> logger,
        ISyncService syncService,
        IVaultwardenService vaultwardenService,
        IKubernetesService kubernetesService,
        AppSettings appSettings)
    {
        _logger = logger;
        _syncService = syncService;
        _vaultwardenService = vaultwardenService;
        _kubernetesService = kubernetesService;
        _appSettings = appSettings;
    }

    public async Task<bool> HandleCommandAsync(string[] args)
    {
        // Parse command line arguments
        var command = args.FirstOrDefault()?.ToLowerInvariant();
        var namespaceArg = args.Skip(1).FirstOrDefault();

        return command switch
        {
            "sync" or null => await HandleSyncCommandAsync(),
            "sync-namespace" => await HandleSyncNamespaceCommandAsync(namespaceArg),
            "cleanup" => await HandleCleanupCommandAsync(),
            "list" => await HandleListCommandAsync(),
            "export" => await HandleExportCommandAsync(args),
            "config" => await HandleConfigCommandAsync(),
            "help" => HandleHelpCommand(),
            _ => HandleUnknownCommand(command)
        };
    }

    private async Task<bool> HandleSyncCommandAsync()
    {
        if (_appSettings.Sync.ContinuousSync)
        {
            _logger.LogInformation("Starting continuous sync with interval {Interval} seconds...", 
                _appSettings.Sync.SyncIntervalSeconds);
            return await RunContinuousSyncAsync();
        }
        else
        {
            _logger.LogInformation("Starting full sync...");
            return await _syncService.SyncAsync();
        }
    }

    private async Task<bool> HandleSyncNamespaceCommandAsync(string? namespaceArg)
    {
        if (string.IsNullOrEmpty(namespaceArg))
        {
            _logger.LogError("Namespace name is required for sync-namespace command");
            return false;
        }
        
        _logger.LogInformation("Starting sync for namespace: {Namespace}", namespaceArg);
        return await _syncService.SyncNamespaceAsync(namespaceArg);
    }

    private async Task<bool> HandleCleanupCommandAsync()
    {
        _logger.LogInformation("Starting cleanup of orphaned secrets...");
        return await _syncService.CleanupOrphanedSecretsAsync();
    }

    private async Task<bool> HandleListCommandAsync()
    {
        _logger.LogInformation("Listing Vaultwarden items...");
        var items = await _vaultwardenService.GetItemsAsync();
        var itemsWithNamespace = items.Where(item => item.ExtractNamespaces().Any()).ToList();
        
        _logger.LogInformation("Found {Count} items with namespace tags:", itemsWithNamespace.Count);
        foreach (var item in itemsWithNamespace)
        {
            var namespaces = item.ExtractNamespaces();
            var namespaceList = string.Join(", ", namespaces);
            _logger.LogInformation("  - {Name} -> {Namespaces}", item.Name, namespaceList);
        }
        
        return true;
    }

    private async Task<bool> HandleExportCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            _logger.LogError("Secret name is required for export command. Usage: export <secret-name> [namespace]");
            return false;
        }
        
        var secretName = args[1];
        var exportNamespace = args.Length > 2 ? args[2] : _appSettings.Kubernetes.DefaultNamespace;
        
        _logger.LogInformation("Exporting secret {SecretName} from namespace {Namespace}...", 
            secretName, exportNamespace);
        
        var yamlOutput = await _kubernetesService.ExportSecretAsYamlAsync(exportNamespace, secretName);
        
        if (yamlOutput != null)
        {
            Console.WriteLine(yamlOutput);
            return true;
        }
        else
        {
            _logger.LogError("Secret {SecretName} not found in namespace {Namespace}", 
                secretName, exportNamespace);
            return false;
        }
    }

    private async Task<bool> HandleConfigCommandAsync()
    {
        _logger.LogInformation("Validating configuration...");
        
        if (_appSettings.Validate(out var configValidationResults))
        {
            _logger.LogInformation("✓ All required configuration keys are present");
            _logger.LogInformation("✓ Vaultwarden Server: {Server}", _appSettings.Vaultwarden.ServerUrl);
            _logger.LogInformation("✓ Kubernetes Default Namespace: {Namespace}", 
                _appSettings.Kubernetes.DefaultNamespace);
            _logger.LogInformation("✓ Dry Run Mode: {DryRun}", _appSettings.Sync.DryRun);
            _logger.LogInformation("✓ Auth Mode: API Key (default)");
        }
        else
        {
            _logger.LogWarning("✗ Configuration validation failed:");
            foreach (var result in configValidationResults)
            {
                _logger.LogWarning("  - {Error}", result.ErrorMessage);
            }
            _logger.LogInformation("Please check your .env file or environment variables");
        }
        
        return true;
    }

    private bool HandleHelpCommand()
    {
        ShowHelp();
        return true;
    }

    private bool HandleUnknownCommand(string? command)
    {
        _logger.LogError("Unknown command: {Command}", command);
        ShowHelp();
        return false;
    }

    private async Task<bool> RunContinuousSyncAsync()
    {
        var cancellationTokenSource = new CancellationTokenSource();
        
        // Handle graceful shutdown
        Console.CancelKeyPress += (sender, e) =>
        {
            _logger.LogInformation("Received shutdown signal, stopping continuous sync...");
            e.Cancel = true; // Prevent immediate termination
            cancellationTokenSource.Cancel();
        };

        try
        {
            var runCount = 0;
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                runCount++;
                _logger.LogInformation("Starting sync run #{RunCount}...", runCount);
                
                try
                {
                    var syncSuccess = await _syncService.SyncAsync();
                    if (syncSuccess)
                    {
                        _logger.LogInformation("Sync run #{RunCount} completed successfully", runCount);
                    }
                    else
                    {
                        _logger.LogWarning("Sync run #{RunCount} completed with errors", runCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sync run #{RunCount} failed with exception", runCount);
                }

                if (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    _logger.LogInformation("Waiting {Interval} seconds before next sync...", 
                        _appSettings.Sync.SyncIntervalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(_appSettings.Sync.SyncIntervalSeconds), 
                        cancellationTokenSource.Token);
                }
            }

            _logger.LogInformation("Continuous sync stopped gracefully");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Continuous sync was cancelled");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Continuous sync failed with exception");
            return false;
        }
    }

    private static void ShowHelp()
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

Namespace Configuration:
  To sync a Vaultwarden item to a Kubernetes namespace, add the following custom field:
  Field name: namespaces
  Field value: your-namespace-name

Field Control:
  To exclude specific fields from being synced to Kubernetes secrets, add a custom field named 
  'ignore-field' containing a comma-separated list of field names to ignore:
  ignore-field: password,sensitive_key,temp_data
  
  The ignore-field itself will never be synced to prevent sensitive configuration exposure.

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

