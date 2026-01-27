using Serilog;
using VaultwardenK8sSync.Application;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Infrastructure;

namespace VaultwardenK8sSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Configure Serilog early for bootstrap logging
        var loggingSettings = AppSettings.FromEnvironment().Logging;
        ConfigurationExtensions.ConfigureSerilog(loggingSettings);

        try
        {
            // Ensure only one sync service process runs at a time
            using var processLock = new ProcessLock();

            if (!processLock.TryAcquire())
            {
                Log.Error("Another sync service process is already running");
                Log.Warning("Only one instance of the sync service can run at a time");
                Log.Information("If this is unexpected, check for orphaned processes:");
                Log.Information("  ps aux | grep VaultwardenK8sSync");
                Log.Information("  kill <PID>");
                return 1;
            }

            Log.Information("Process lock acquired - this is the only running sync service instance");

            var host = new ApplicationHost();
            return await host.RunAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
