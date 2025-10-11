using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Infrastructure;
using VaultwardenK8sSync.Services;
using dotenv.net;

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
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Add configuration
        services.AddAppConfiguration();
        
        // Get logging settings for configuration
        var tempSettings = AppSettings.FromEnvironment();
        services.AddCustomLogging(tempSettings.Logging);
        
        // Add infrastructure services
        services.AddScoped<IProcessFactory, ProcessFactory>();
        services.AddScoped<IProcessRunner, ProcessRunner>();
        
        // Register application services
        services.AddSingleton<IMetricsService, MetricsService>();
        services.AddScoped<IVaultwardenService, VaultwardenService>();
        services.AddScoped<IKubernetesService, KubernetesService>();
        services.AddScoped<ISyncService, SyncService>();
        services.AddScoped<IWebhookService, WebhookService>();
        services.AddScoped<ICommandHandler, CommandHandler>();
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
            // Log full stack trace to aid debugging even if logger is not available
            Console.WriteLine($"Fatal error: {ex}");
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
        _logger.LogWarning("⚠️  WARNING: This application is not production-ready and may have significant CPU usage");
        _logger.LogWarning("⚠️  Monitor resource consumption and adjust sync intervals accordingly");

        _logger.LogDebug(
            "Log levels -> Default: {Default}, Microsoft: {Ms}, Microsoft.Hosting.Lifetime: {MsLifetime}",
            _appSettings.Logging.DefaultLevel,
            _appSettings.Logging.MicrosoftLevel,
            _appSettings.Logging.MicrosoftHostingLifetimeLevel);

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

    private async Task<bool> InitializeServicesAsync()
    {
        // Initialize Kubernetes client
        _logger.LogDebug("Initializing Kubernetes client...");
        var kubernetesService = _serviceProvider.GetRequiredService<IKubernetesService>();
        if (!await kubernetesService.InitializeAsync())
        {
            _logger.LogError("Failed to initialize Kubernetes client");
            return false;
        }

        // Authenticate with Vaultwarden with simple progress display
        using (var progress = new Services.StaticProgressDisplay())
        {
            progress.Start("Authenticating with Vaultwarden...");
            
            var vaultwardenService = _serviceProvider.GetRequiredService<IVaultwardenService>();
            var authSuccess = await vaultwardenService.AuthenticateAsync();
            
            if (!authSuccess)
            {
                progress.Complete("❌ Failed to authenticate with Vaultwarden");
                _logger.LogError("Failed to authenticate with Vaultwarden");
                return false;
            }
            
            progress.Complete("✅ Successfully authenticated with Vaultwarden");
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
                _metricsServer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping metrics server");
            }
        }
    }
}

