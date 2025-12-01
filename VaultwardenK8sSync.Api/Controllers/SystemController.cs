using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VaultwardenK8sSync.Database;

namespace VaultwardenK8sSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly SyncDbContext _context;
    private readonly ILogger<SystemController> _logger;
    private static DateTime _lastCpuCheck = DateTime.MinValue;
    private static TimeSpan _lastCpuTime = TimeSpan.Zero;
    private static double _lastCpuUsage = 0;

    public SystemController(SyncDbContext context, ILogger<SystemController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("resources")]
    public ActionResult<object> GetResources()
    {
        var process = Process.GetCurrentProcess();
        
        return Ok(new
        {
            cpu = new
            {
                usagePercent = GetCpuUsage(process),
                cores = Environment.ProcessorCount,
                totalProcessorTime = process.TotalProcessorTime.TotalSeconds
            },
            memory = new
            {
                workingSetMB = process.WorkingSet64 / 1024.0 / 1024.0,
                privateMemoryMB = process.PrivateMemorySize64 / 1024.0 / 1024.0,
                gcTotalMemoryMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0
            },
            threads = new
            {
                count = process.Threads.Count,
                threadPoolAvailable = GetThreadPoolInfo()
            },
            runtime = new
            {
                uptimeSeconds = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
                dotnetVersion = RuntimeInformation.FrameworkDescription,
                osDescription = RuntimeInformation.OSDescription
            },
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("sync-service-resources")]
    public ActionResult<object> GetSyncServiceResources()
    {
        try
        {
            // Find sync service process by name
            var syncProcesses = Process.GetProcessesByName("dotnet")
                .Where(p => p.MainModule?.FileName?.Contains("VaultwardenK8sSync") ?? false)
                .ToList();

            if (!syncProcesses.Any())
            {
                return NotFound(new { error = "Sync service process not found" });
            }

            var syncProcess = syncProcesses.First();
            
            return Ok(new
            {
                cpu = new
                {
                    usagePercent = GetCpuUsage(syncProcess),
                    cores = Environment.ProcessorCount
                },
                memory = new
                {
                    workingSetMB = syncProcess.WorkingSet64 / 1024.0 / 1024.0,
                    privateMemoryMB = syncProcess.PrivateMemorySize64 / 1024.0 / 1024.0
                },
                threads = new
                {
                    count = syncProcess.Threads.Count
                },
                processId = syncProcess.Id,
                processName = syncProcess.ProcessName,
                startTime = syncProcess.StartTime,
                uptimeSeconds = (DateTime.UtcNow - syncProcess.StartTime.ToUniversalTime()).TotalSeconds,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static double GetCpuUsage(Process process)
    {
        try
        {
            var currentTime = DateTime.UtcNow;
            var currentCpuTime = process.TotalProcessorTime;

            // First call or after 1 second, recalculate
            if ((currentTime - _lastCpuCheck).TotalMilliseconds < 100)
            {
                return _lastCpuUsage;
            }

            if (_lastCpuCheck != DateTime.MinValue)
            {
                var timeDiff = (currentTime - _lastCpuCheck).TotalMilliseconds;
                var cpuDiff = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
                _lastCpuUsage = (cpuDiff / (Environment.ProcessorCount * timeDiff)) * 100;
            }

            _lastCpuCheck = currentTime;
            _lastCpuTime = currentCpuTime;

            return _lastCpuUsage;
        }
        catch
        {
            return 0;
        }
    }

    private static object GetThreadPoolInfo()
    {
        ThreadPool.GetAvailableThreads(out int workerThreads, out int ioThreads);
        ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxIoThreads);
        
        return new
        {
            availableWorkerThreads = workerThreads,
            availableIoThreads = ioThreads,
            maxWorkerThreads = maxWorkerThreads,
            maxIoThreads = maxIoThreads
        };
    }

    /// <summary>
    /// Reset the database - deletes all sync logs, secret states, and cached items
    /// </summary>
    [HttpPost("reset-database")]
    public async Task<ActionResult> ResetDatabase()
    {
        try
        {
            _logger.LogWarning("Database reset requested - deleting all data");

            // Delete all data from tables
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM SyncLogs");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM SecretStates");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM VaultwardenItems");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM SyncItems");
            
            _logger.LogInformation("Database reset completed successfully");
            
            return Ok(new { message = "Database reset successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset database");
            return StatusCode(500, "Failed to reset database: " + ex.Message);
        }
    }
}
