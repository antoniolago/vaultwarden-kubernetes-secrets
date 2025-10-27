using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace VaultwardenK8sSync.Infrastructure;

/// <summary>
/// File-based lock to ensure only one sync service process runs at a time.
/// Prevents multiple instances from running concurrently.
/// </summary>
public class ProcessLock : IDisposable
{
    private readonly string _lockFilePath;
    private FileStream? _lockFileStream;
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;

    public ProcessLock(string lockFileName = "vaultwarden-sync.lock", Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        _logger = logger;
        var tempPath = Path.GetTempPath();
        _lockFilePath = Path.Combine(tempPath, lockFileName);
    }

    /// <summary>
    /// Attempts to acquire the process lock. Returns true if successful, false if another process holds the lock.
    /// </summary>
    public bool TryAcquire()
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
                FileOptions.DeleteOnClose); // Auto-cleanup when process exits

            // Write PID to lock file for debugging
            var processInfo = $"PID:{Environment.ProcessId}\nStarted:{DateTime.UtcNow:O}\n";
            using (var writer = new StreamWriter(_lockFileStream, leaveOpen: true))
            {
                writer.Write(processInfo);
                writer.Flush();
            }

            _logger?.LogInformation("✅ Acquired process lock: {LockFile}", _lockFilePath);
            return true;
        }
        catch (IOException)
        {
            // Lock file is held by another process
            _logger?.LogError("❌ Another sync service process is already running. Lock file: {LockFile}", _lockFilePath);
            
            // Try to read who holds the lock
            try
            {
                var lockInfo = File.ReadAllText(_lockFilePath);
                _logger?.LogError("Lock held by: {LockInfo}", lockInfo);
            }
            catch
            {
                // Ignore if we can't read
            }
            
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "Permission denied accessing lock file: {LockFile}", _lockFilePath);
            return false;
        }
    }

    public void Dispose()
    {
        if (_lockFileStream != null)
        {
            _lockFileStream.Dispose();
            _lockFileStream = null;
            _logger?.LogInformation("Released process lock: {LockFile}", _lockFilePath);
        }
    }
}
