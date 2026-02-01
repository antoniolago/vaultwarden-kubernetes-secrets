using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Configuration;

namespace VaultwardenK8sSync.Infrastructure;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(Process process, int timeoutSeconds = Constants.Timeouts.DefaultCommandTimeoutSeconds, string? input = null);
}

public class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(Process process, int timeoutSeconds = Constants.Timeouts.DefaultCommandTimeoutSeconds, string? input = null)
    {
        try
        {
            // For interactive commands like 'bw unlock', we need to write stdin immediately
            // before the process starts blocking on input
            if (!string.IsNullOrEmpty(input))
            {
                // Set up stdin writer before starting process
                process.StartInfo.Environment["BW_NOINTERACTION"] = "true";
            }
            
            process.Start();

            // Write input if provided - do this immediately after start
            if (!string.IsNullOrEmpty(input))
            {
                try
                {
                    // Write immediately without delay - the process buffers stdin
                    await process.StandardInput.WriteLineAsync(input);
                    await process.StandardInput.FlushAsync();
                    process.StandardInput.Close();
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning("Could not write input to process stdin: {Message}", ex.Message);
                }
            }

            // Read output and error streams asynchronously with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();

            // Wait for process to exit with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("Process '{FileName} {Arguments}' timed out after {Timeout} seconds",
                    process.StartInfo.FileName, process.StartInfo.Arguments, timeoutSeconds);
                
                try { process.Kill(); } catch { }
                
                return new ProcessResult
                {
                    ExitCode = -1,
                    Output = string.Empty,
                    Error = $"Process timed out after {timeoutSeconds} seconds",
                    Success = false
                };
            }

            await exitTask;
            var output = await outputTask;
            var error = await errorTask;

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                Output = output ?? string.Empty,
                Error = error ?? string.Empty,
                Success = process.ExitCode == 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run process '{FileName} {Arguments}'",
                process.StartInfo.FileName, process.StartInfo.Arguments);
            
            return new ProcessResult
            {
                ExitCode = -1,
                Output = string.Empty,
                Error = ex.Message,
                Success = false
            };
        }
    }
}

public class ProcessResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool Success { get; set; }
}

