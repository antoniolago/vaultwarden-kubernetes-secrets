using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Infrastructure;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Database;
using dotenv.net;
using System.IO;

namespace VaultwardenK8sSync.Application;

public class ApplicationHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplicationHost> _logger;
    private readonly AppSettings _appSettings;
    private MetricsServer? _metricsServer;

    public ApplicationHost()
    {
        // Load .env file
        DotEnv.Load(options: new DotEnvOptions(probeForEnv: true));
        
        // Build service collection
        var services = new ServiceCollection();
        ConfigureServices(services);
        
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<ApplicationHost>>();
        _appSettings = _serviceProvider.GetRequiredService<AppSettings>();
        
        // Initialize database
        InitializeDatabase();
    }
    
    private void InitializeDatabase()
    {
        try
        {
            // Ensure data directory exists
            var dbPath = Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "./data/sync.db";
            var dbDirectory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
                _logger.LogInformation("Created database directory: {Directory}", dbDirectory);
            }
            
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
            db.Database.EnsureCreated();
            
            // Run migrations: Add sync configuration columns to SyncLogs table
            try
            {
                var connection = db.Database.GetDbConnection();
                connection.Open();
                
                using var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "PRAGMA table_info(SyncLogs);";
                var reader = checkCmd.ExecuteReader();
                var columns = new List<string>();
                while (reader.Read())
                {
                    columns.Add(reader.GetString(1)); // Column name at index 1
                }
                reader.Close();
                
                if (!columns.Contains("SyncIntervalSeconds"))
                {
                    _logger.LogInformation("Adding SyncIntervalSeconds column to SyncLogs");
                    using var addCmd = connection.CreateCommand();
                    addCmd.CommandText = "ALTER TABLE SyncLogs ADD COLUMN SyncIntervalSeconds INTEGER NOT NULL DEFAULT 0;";
                    addCmd.ExecuteNonQuery();
                    _logger.LogInformation("SyncIntervalSeconds column added");
                }
                
                if (!columns.Contains("ContinuousSync"))
                {
                    _logger.LogInformation("Adding ContinuousSync column to SyncLogs");
                    using var addCmd = connection.CreateCommand();
                    addCmd.CommandText = "ALTER TABLE SyncLogs ADD COLUMN ContinuousSync INTEGER NOT NULL DEFAULT 0;";
                    addCmd.ExecuteNonQuery();
                    _logger.LogInformation("ContinuousSync column added");
                }
                
                connection.Close();
            }
            catch (Exception migEx)
            {
                _logger.LogWarning(migEx, "Could not run database migrations");
            }
            
            _logger.LogInformation("Database initialized successfully at {Path}", dbPath);
            
            // Clean up orphaned InProgress sync logs from crashed/killed previous runs
            CleanupOrphanedSyncLogs();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize database - database logging will be disabled");
        }
    }
    
    private void CleanupOrphanedSyncLogs()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
            
            // Find all InProgress sync logs (likely orphaned from crashes/kills)
            var orphanedLogs = db.SyncLogs
                .Where(s => s.Status == "InProgress")
                .ToList();
            
            if (orphanedLogs.Any())
            {
                _logger.LogWarning("Found {Count} orphaned InProgress sync logs from previous runs - marking as Failed", orphanedLogs.Count);
                
                foreach (var log in orphanedLogs)
                {
                    log.Status = "Failed";
                    log.EndTime = DateTime.UtcNow;
                    log.ErrorMessage = "Sync was interrupted (service stopped/crashed before completion)";
                    log.DurationSeconds = log.EndTime.HasValue 
                        ? (log.EndTime.Value - log.StartTime).TotalSeconds 
                        : 0;
                }
                
                db.SaveChanges();
                _logger.LogInformation("Cleaned up {Count} orphaned sync logs", orphanedLogs.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up orphaned sync logs");
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Add configuration
        services.AddAppConfiguration();

        // Use pre-configured Serilog (configured in Program.cs)
        services.AddSerilogLogging();

        // Add infrastructure services
        services.AddScoped<IProcessFactory, ProcessFactory>();
        services.AddScoped<IProcessRunner, ProcessRunner>();
        
        // Register application services
        services.AddSingleton<IMetricsService, MetricsService>();
        services.AddSingleton<IValkeySyncOutputPublisher, ValkeySyncOutputPublisher>();
        // VaultwardenService must be Singleton to maintain authentication state across continuous sync runs
        services.AddSingleton<IVaultwardenService, VaultwardenService>();
        services.AddSingleton<IKubernetesService, KubernetesService>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<IWebhookService, WebhookService>();
        services.AddSingleton<ICommandHandler, CommandHandler>();
    }

    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            SetupGlobalExceptionHandlers();
            LogStartupInformation();
            
            if (!ValidateConfiguration())
            {
                return 1;
            }

            if (!await InitializeServicesAsync())
            {
                return 1;
            }

            // Start metrics server if enabled
            await StartMetricsServerAsync();

            var commandHandler = _serviceProvider.GetRequiredService<ICommandHandler>();
            var success = await commandHandler.HandleCommandAsync(args);
            
            // Logout from Vaultwarden
            var vaultwardenService = _serviceProvider.GetRequiredService<IVaultwardenService>();
            await vaultwardenService.LogoutAsync();

            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error during application execution");
            return 1;
        }
        finally
        {
            await StopMetricsServerAsync();
            
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private void SetupGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                _logger.LogCritical(ex, "Unhandled exception");
            }
            else
            {
                _logger.LogCritical("Unhandled exception: {Info}", e.ExceptionObject);
            }
        };
        
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            _logger.LogCritical(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    private void LogStartupInformation()
    {
        // Production readiness warning
        _logger.LogWarning("WARNING: This application is not production-ready and may have significant CPU usage");
        _logger.LogWarning("Monitor resource consumption and adjust sync intervals accordingly");

        _logger.LogDebug(
            "Log levels -> Default: {Default}, Microsoft: {Ms}, Sync: {Sync}, Kubernetes: {K8s}, Vaultwarden: {Vw}",
            _appSettings.Logging.DefaultLevel,
            _appSettings.Logging.MicrosoftLevel,
            _appSettings.Logging.SyncLevel ?? "inherit",
            _appSettings.Logging.KubernetesLevel ?? "inherit",
            _appSettings.Logging.VaultwardenLevel ?? "inherit");

        // Log presence (not values) of critical env vars
        _logger.LogDebug(
            "Config: ServerUrl set={ServerSet}, InCluster={InCluster}, DefaultNamespace={Ns}, BW_CLIENTID set={HasId}, BW_CLIENTSECRET set={HasSecret}, MASTERPASSWORD set={HasPw}",
            !string.IsNullOrWhiteSpace(_appSettings.Vaultwarden.ServerUrl),
            _appSettings.Kubernetes.InCluster,
            _appSettings.Kubernetes.DefaultNamespace,
            !string.IsNullOrWhiteSpace(_appSettings.Vaultwarden.ClientId),
            !string.IsNullOrWhiteSpace(_appSettings.Vaultwarden.ClientSecret),
            !string.IsNullOrWhiteSpace(_appSettings.Vaultwarden.MasterPassword));
    }

    private bool ValidateConfiguration()
    {
        if (!_appSettings.Validate(out var validationResults))
        {
            _logger.LogWarning("Configuration validation failed:");
            foreach (var result in validationResults)
            {
                _logger.LogWarning("  - {Error}", result.ErrorMessage);
            }
            _logger.LogInformation("Please check your .env file or environment variables");
            return false;
        }
        
        return true;
    }

    private async Task InitializeAuthTokenAsync(IKubernetesService kubernetesService)
    {
        _logger.LogDebug("Starting auth token initialization");
        
        // Skip if loginless mode is enabled
        var loginlessMode = Environment.GetEnvironmentVariable("LOGINLESS_MODE")?.ToLower() == "true";
        _logger.LogDebug("LOGINLESS_MODE = {Mode}", loginlessMode);
        
        if (loginlessMode)
        {
            _logger.LogInformation("Loginless mode enabled - skipping auth token initialization");
            return;
        }

        // Check if AUTH_TOKEN is already set in environment
        var existingToken = Environment.GetEnvironmentVariable("AUTH_TOKEN");
        if (!string.IsNullOrEmpty(existingToken))
        {
            _logger.LogInformation("Auth token found in environment variables");
            return;
        }

        // Try to read token from Kubernetes secret
        // Use APP_NAMESPACE for the auth token secret, defaulting to 'vaultwarden-kubernetes-secrets'
        var secretNamespace = Environment.GetEnvironmentVariable("APP_NAMESPACE") 
            ?? "vaultwarden-kubernetes-secrets";
        var secretName = "vaultwarden-kubernetes-secrets-token";
        
        _logger.LogDebug("Checking for auth token secret in namespace: {Namespace}", secretNamespace);
        
        try
        {
            var secretExists = await kubernetesService.SecretExistsAsync(secretNamespace, secretName);
            _logger.LogDebug("Secret exists check result: {Exists}", secretExists);
            
            if (secretExists)
            {
                _logger.LogInformation("Found existing auth token secret in Kubernetes");
                var secretData = await kubernetesService.GetSecretDataAsync(secretNamespace, secretName);
                
                if (secretData != null && secretData.ContainsKey("token"))
                {
                    var token = secretData["token"];
                    Environment.SetEnvironmentVariable("AUTH_TOKEN", token);
                    _logger.LogInformation("Loaded auth token from Kubernetes secret");
                    return;
                }
                else
                {
                    _logger.LogWarning("Secret exists but token key not found");
                }
            }

            // Generate new token and create secret
            _logger.LogInformation("Generating new auth token and creating Kubernetes secret");
            _logger.LogDebug("Target namespace: {Namespace}, Secret name: {SecretName}", secretNamespace, secretName);
            
            var newToken = GenerateSecureToken();
            
            var data = new Dictionary<string, string>
            {
                { "token", newToken }
            };
            
            var annotations = new Dictionary<string, string>
            {
                { "managed-by", "vaultwarden-kubernetes-secrets" },
                { "description", "Authentication token for Vaultwarden K8s Sync API" }
            };
            
            _logger.LogDebug("Attempting to create secret");
            var result = await kubernetesService.CreateSecretAsync(secretNamespace, secretName, data, annotations);
            _logger.LogDebug("Create secret result: Success={Success}, Error={Error}", result.Success, result.ErrorMessage);
            
            if (result.Success)
            {
                Environment.SetEnvironmentVariable("AUTH_TOKEN", newToken);
                _logger.LogInformation("Created new auth token secret in Kubernetes namespace '{Namespace}'", secretNamespace);
                _logger.LogWarning("IMPORTANT: Auth token has been generated. Retrieve it with: kubectl get secret {SecretName} -n {Namespace} -o jsonpath='{{.data.token}}' | base64 -d", secretName, secretNamespace);
            }
            else
            {
                _logger.LogWarning("Failed to create auth token secret: {Error}", result.ErrorMessage);
                _logger.LogWarning("API will run without authentication");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing auth token from Kubernetes");
            _logger.LogWarning("API will run without authentication");
        }
    }

    private string GenerateSecureToken()
    {
        // Generate a cryptographically secure random token
        var tokenBytes = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        return Convert.ToBase64String(tokenBytes);
    }

    private async Task<bool> InitializeServicesAsync()
    {
        // Authenticate with Vaultwarden FIRST (before Kubernetes)
        // This allows the app to work even if K8s is unavailable (e.g., for API/dashboard only)
        var valkeyPublisher = _serviceProvider.GetRequiredService<IValkeySyncOutputPublisher>();
        using (var progress = new Services.StaticProgressDisplay(valkeyPublisher))
        {
            progress.Start("Authenticating with Vaultwarden...");
            
            var vaultwardenService = _serviceProvider.GetRequiredService<IVaultwardenService>();
            var authSuccess = await vaultwardenService.AuthenticateAsync();
            
            if (!authSuccess)
            {
                progress.Complete("Failed to authenticate with Vaultwarden");
                _logger.LogError("Failed to authenticate with Vaultwarden");
                return false;
            }
            
            progress.Complete("Successfully authenticated with Vaultwarden");
        }

        // Initialize Kubernetes client
        _logger.LogDebug("Initializing Kubernetes client...");
        var kubernetesService = _serviceProvider.GetRequiredService<IKubernetesService>();
        if (!await kubernetesService.InitializeAsync())
        {
            _logger.LogWarning("Failed to initialize Kubernetes client - sync to K8s will not work");
            _logger.LogWarning("API and dashboard will still be available for viewing Vaultwarden items");
            // Don't return false - allow app to continue for API/dashboard functionality
        }
        else
        {
            // Initialize auth token from Kubernetes secret if not in loginless mode
            await InitializeAuthTokenAsync(kubernetesService);
        }

        return true;
    }

    private async Task StartMetricsServerAsync()
    {
        if (!_appSettings.Metrics.Enabled)
        {
            _logger.LogInformation("Metrics server is disabled");
            return;
        }

        try
        {
            var vaultwardenService = _serviceProvider.GetRequiredService<IVaultwardenService>();
            var kubernetesService = _serviceProvider.GetRequiredService<IKubernetesService>();
            var metricsService = _serviceProvider.GetRequiredService<IMetricsService>();
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            var metricsLogger = loggerFactory.CreateLogger<MetricsServer>();

            // Get webhook service if enabled
            IWebhookService? webhookService = null;
            if (_appSettings.Webhook.Enabled)
            {
                webhookService = _serviceProvider.GetRequiredService<IWebhookService>();
                _logger.LogInformation("Webhook support enabled");
            }

            _metricsServer = new MetricsServer(
                metricsLogger,
                vaultwardenService,
                kubernetesService,
                metricsService,
                webhookService,
                _appSettings.Webhook,
                _appSettings.Metrics.Port);

            await _metricsServer.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start metrics server - continuing without metrics");
        }
    }

    private async Task StopMetricsServerAsync()
    {
        if (_metricsServer != null)
        {
            try
            {
                await _metricsServer.StopAsync();
                await _metricsServer.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping metrics server");
            }
        }
    }
}

