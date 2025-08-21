using VaultwardenK8sSync.Application;

namespace VaultwardenK8sSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var host = new ApplicationHost();
        return await host.RunAsync(args);
    }
}
