using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
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
    
    public static IServiceCollection AddCustomLogging(this IServiceCollection services, LoggingSettings loggingSettings)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = false;
                options.TimestampFormat = "HH:mm:ss ";
                options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
            });
            builder.SetMinimumLevel(LogLevelParser.Parse(loggingSettings.DefaultLevel));
            builder.AddFilter("Microsoft", LogLevelParser.Parse(loggingSettings.MicrosoftLevel));
            builder.AddFilter("Microsoft.Hosting.Lifetime", LogLevelParser.Parse(loggingSettings.MicrosoftHostingLifetimeLevel));
        });
        
        return services;
    }
}

