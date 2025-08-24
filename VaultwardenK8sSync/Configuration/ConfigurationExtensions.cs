using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.AddSingleton(appSettings);
        
        return services;
    }
    
    public static IServiceCollection AddCustomLogging(this IServiceCollection services, LoggingSettings loggingSettings)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole(options =>
            {
                options.IncludeScopes = false;
                options.TimestampFormat = "HH:mm:ss ";
                options.DisableColors = false;
            });
            builder.SetMinimumLevel(LogLevelParser.Parse(loggingSettings.DefaultLevel));
            builder.AddFilter("Microsoft", LogLevelParser.Parse(loggingSettings.MicrosoftLevel));
            builder.AddFilter("Microsoft.Hosting.Lifetime", LogLevelParser.Parse(loggingSettings.MicrosoftHostingLifetimeLevel));
        });
        
        return services;
    }
}

