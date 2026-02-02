using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Templates;
using VaultwardenK8sSync.Database;
using VaultwardenK8sSync.Database.Repositories;

namespace VaultwardenK8sSync.Configuration;

public static class ConfigurationExtensions
{
    public static IServiceCollection AddAppConfiguration(this IServiceCollection services)
    {
        // Load configuration from environment variables
        var appSettings = AppSettings.FromEnvironment();

        // Register configuration sections
        services.AddSingleton(appSettings.Vaultwarden);
        services.AddSingleton(appSettings.Kubernetes);
        services.AddSingleton(appSettings.Sync);
        services.AddSingleton(appSettings.Logging);
        services.AddSingleton(appSettings.Metrics);
        services.AddSingleton(appSettings.Webhook);
        services.AddSingleton(appSettings);

        var dbPath = Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "./data/sync.db";
        var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";
        services.AddDbContext<SyncDbContext>(options =>
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            }));

        services.AddScoped<ISyncLogRepository, SyncLogRepository>();
        services.AddScoped<ISecretStateRepository, SecretStateRepository>();

        services.AddSingleton<Services.IDatabaseLoggerService, Services.DatabaseLoggerService>();

        return services;
    }

    /// <summary>
    /// Configures Serilog with environment-aware output formatting.
    /// Call this early in Program.cs before building the host.
    /// </summary>
    public static void ConfigureSerilog(LoggingSettings? settings = null)
    {
        settings ??= new LoggingSettings();
        var isProduction = IsProductionEnvironment();

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(GetLogLevel(settings.DefaultLevel, LogEventLevel.Information))
            .MinimumLevel.Override("Microsoft", GetLogLevel(settings.MicrosoftLevel, LogEventLevel.Warning))
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext();

        // Apply component-specific log levels
        ApplyComponentOverrides(loggerConfig, settings);

        if (isProduction)
        {
            // Compact JSON for log aggregators (Loki, ELK, etc.)
            loggerConfig.WriteTo.Console(new RenderedCompactJsonFormatter());
        }
        else
        {
            // Human-readable with colors for development
            // Format: [HH:mm:ss LEVEL] SourceContext [Namespace/SecretName]
            //           Message
            //           Exception
            loggerConfig.WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3}] {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}" +
                "{#if Namespace is not null} [{Namespace}{#if SecretName is not null}/{SecretName}{#end}]{#end}\n" +
                "  {@m}\n" +
                "{#if @x is not null}  {@x}\n{#end}",
                theme: Serilog.Templates.Themes.TemplateTheme.Code));
        }

        Log.Logger = loggerConfig.CreateLogger();
    }

    /// <summary>
    /// Adds Serilog to the service collection using the pre-configured Log.Logger.
    /// </summary>
    public static IServiceCollection AddSerilogLogging(this IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        return services;
    }

    private static void ApplyComponentOverrides(LoggerConfiguration loggerConfig, LoggingSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.SyncLevel))
        {
            loggerConfig.MinimumLevel.Override(
                "VaultwardenK8sSync.Services.SyncService",
                GetLogLevel(settings.SyncLevel, LogEventLevel.Information));
        }

        if (!string.IsNullOrEmpty(settings.KubernetesLevel))
        {
            loggerConfig.MinimumLevel.Override(
                "VaultwardenK8sSync.Services.KubernetesService",
                GetLogLevel(settings.KubernetesLevel, LogEventLevel.Information));
        }

        if (!string.IsNullOrEmpty(settings.VaultwardenLevel))
        {
            loggerConfig.MinimumLevel.Override(
                "VaultwardenK8sSync.Services.VaultwardenService",
                GetLogLevel(settings.VaultwardenLevel, LogEventLevel.Information));
        }

        if (!string.IsNullOrEmpty(settings.DatabaseLevel))
        {
            loggerConfig.MinimumLevel.Override(
                "VaultwardenK8sSync.Services.DatabaseLoggerService",
                GetLogLevel(settings.DatabaseLevel, LogEventLevel.Information));
            loggerConfig.MinimumLevel.Override(
                "VaultwardenK8sSync.Database.Repositories",
                GetLogLevel(settings.DatabaseLevel, LogEventLevel.Information));
        }

        if (!string.IsNullOrEmpty(settings.WebhookLevel))
        {
            loggerConfig.MinimumLevel.Override(
                "VaultwardenK8sSync.Services.WebhookService",
                GetLogLevel(settings.WebhookLevel, LogEventLevel.Information));
        }

        if (!string.IsNullOrEmpty(settings.MetricsLevel))
        {
            loggerConfig.MinimumLevel.Override(
                "VaultwardenK8sSync.Infrastructure.MetricsServer",
                GetLogLevel(settings.MetricsLevel, LogEventLevel.Information));
            loggerConfig.MinimumLevel.Override(
                "VaultwardenK8sSync.Services.MetricsService",
                GetLogLevel(settings.MetricsLevel, LogEventLevel.Information));
        }
    }

    private static bool IsProductionEnvironment()
    {
        return Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Production"
            || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production"
            || Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") != null;
    }

    private static LogEventLevel GetLogLevel(string? configuredLevel, LogEventLevel defaultLevel)
    {
        if (string.IsNullOrWhiteSpace(configuredLevel))
        {
            return defaultLevel;
        }

        return configuredLevel.ToLowerInvariant() switch
        {
            "verbose" or "trace" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" or "info" => LogEventLevel.Information,
            "warning" or "warn" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" or "critical" => LogEventLevel.Fatal,
            _ => Enum.TryParse<LogEventLevel>(configuredLevel, ignoreCase: true, out var level)
                ? level
                : defaultLevel
        };
    }
}

