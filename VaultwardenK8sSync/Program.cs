using VaultwardenK8sSync.Application;
using VaultwardenK8sSync.Infrastructure;

namespace VaultwardenK8sSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Ensure only one sync service process runs at a time
        using var processLock = new ProcessLock();
        
        if (!processLock.TryAcquire())
        {
            Console.Error.WriteLine("❌ ERROR: Another sync service process is already running!");
            Console.Error.WriteLine("Only one instance of the sync service can run at a time.");
            Console.Error.WriteLine("If this is unexpected, check for orphaned processes:");
            Console.Error.WriteLine("  ps aux | grep VaultwardenK8sSync");
            Console.Error.WriteLine("  kill <PID>");
            return 1; // Exit with error code
        }
        
        Console.WriteLine("✅ Process lock acquired - this is the only running sync service instance");
        
        var host = new ApplicationHost();
        return await host.RunAsync(args);
    }
}
