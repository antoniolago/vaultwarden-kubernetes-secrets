using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VaultwardenK8sSync.Infrastructure;

/// <summary>
/// Global file-based lock to ensure only ONE sync operation runs at a time across ALL processes.
/// This prevents:
/// - Multiple sync service processes
/// - Manual "dotnet run sync" while continuous sync is running  
/// - API-triggered syncs while continuous sync is running
/// </summary>
public class GlobalSyncLock : IDisposable
{
    private readonly string _lockFilePath;
    private FileStream? _lockFileStream;
    private readonly ILogger? _logger;
    private readonly int _timeoutMs;

    public GlobalSyncLock(ILogger? logger = null, int timeoutMs = 100)
    {
        _logger = logger;
        _timeoutMs = timeoutMs;
        var tempPath = Path.GetTempPath();
        _lockFilePath = Path.Combine(tempPath, "vaultwarden-sync-operation.lock");
    }

    /// <summary>
    /// Attempts to acquire the sync lock. Returns true if successful, false if another sync is in progress.
    /// Waits up to timeoutMs for the lock to become available.
    /// </summary>
    public async Task<bool> TryAcquireAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(_timeoutMs);
        
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // Try to create/open the lock file with exclusive access
                _lockFileStream = new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None, // No sharing - exclusive lock
                    bufferSize: 1,
                    FileOptions.DeleteOnClose); // Auto-cleanup when released

                // Write sync info to lock file for debugging
                var lockInfo = $"PID:{Environment.ProcessId}\nStarted:{DateTime.UtcNow:O}\nType:SyncOperation\n";
                using (var writer = new StreamWriter(_lockFileStream, leaveOpen: true))
                {
                    writer.Write(lockInfo);
                    writer.Flush();
                }

                _logger?.LogDebug("🔒 Acquired sync operation lock: {LockFile}", _lockFilePath);
                return true;
            }
            catch (IOException)
            {
                // Lock file is held by another process/sync - wait a bit
                await Task.Delay(10);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogError(ex, "Permission denied accessing sync lock file: {LockFile}", _lockFilePath);
                return false;
            }
        }
        
        // Timeout - another sync is in progress
        try
        {
            var lockInfo = File.ReadAllText(_lockFilePath);
            _logger?.LogWarning("⏳ Sync lock timeout. Another sync in progress: {LockInfo}", lockInfo);
        }
        catch
        {
            _logger?.LogWarning("⏳ Sync lock timeout. Another sync is in progress");
        }
        
        return false;
    }

    public void Dispose()
    {
        if (_lockFileStream != null)
        {
            _lockFileStream.Dispose();
            _lockFileStream = null;
            _logger?.LogDebug("🔓 Released sync operation lock: {LockFile}", _lockFilePath);
        }
    }
}
