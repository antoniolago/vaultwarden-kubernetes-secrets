using System.Diagnostics;

namespace VaultwardenK8sSync.Infrastructure;

public interface IProcessFactory
{
    Process CreateBwProcess(string arguments);
}

public class ProcessFactory : IProcessFactory
{
    public Process CreateBwProcess(string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bw",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }
}

