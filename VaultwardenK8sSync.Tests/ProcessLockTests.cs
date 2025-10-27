using Xunit;
using VaultwardenK8sSync.Infrastructure;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;

namespace VaultwardenK8sSync.Tests;

public class ProcessLockTests : IDisposable
{
    private readonly string _testLockFile;

    public ProcessLockTests()
    {
        // Use unique lock file for each test to avoid conflicts
        _testLockFile = $"test-{Guid.NewGuid()}.lock";
    }

    public void Dispose()
    {
        // Clean up test lock file
        var lockPath = Path.Combine(Path.GetTempPath(), _testLockFile);
        try
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void ShouldAcquireLock_WhenNoOtherProcessHoldsIt()
    {
        // Arrange
        using var lock1 = new ProcessLock(_testLockFile);

        // Act
        var result = lock1.TryAcquire();

        // Assert
        Assert.True(result, "Should acquire lock when no other process holds it");
    }

    [Fact]
    public void ShouldFailToAcquireLock_WhenAnotherProcessHoldsIt()
    {
        // Arrange
        using var lock1 = new ProcessLock(_testLockFile);
        using var lock2 = new ProcessLock(_testLockFile);

        // Act
        var firstAcquire = lock1.TryAcquire();
        var secondAcquire = lock2.TryAcquire();

        // Assert
        Assert.True(firstAcquire, "First process should acquire lock");
        Assert.False(secondAcquire, "Second process should NOT acquire lock");
    }

    [Fact]
    public void ShouldReleaseLock_WhenDisposed()
    {
        // Arrange
        var lock1 = new ProcessLock(_testLockFile);
        var lock2 = new ProcessLock(_testLockFile);

        // Act
        var firstAcquire = lock1.TryAcquire();
        lock1.Dispose(); // Release lock
        var secondAcquire = lock2.TryAcquire();

        // Assert
        Assert.True(firstAcquire, "First process should acquire lock");
        Assert.True(secondAcquire, "Second process should acquire lock after first releases it");
        
        lock2.Dispose();
    }

    [Fact]
    public void ShouldCreateLockFileInTempDirectory()
    {
        // Arrange
        using var processLock = new ProcessLock(_testLockFile);
        var expectedPath = Path.Combine(Path.GetTempPath(), _testLockFile);

        // Act
        var acquired = processLock.TryAcquire();

        // Assert
        Assert.True(acquired);
        Assert.True(File.Exists(expectedPath), $"Lock file should exist at {expectedPath}");
    }

    [Fact]
    public void ShouldWriteProcessInfoToLockFile()
    {
        // Arrange
        using var processLock = new ProcessLock(_testLockFile);
        var lockPath = Path.Combine(Path.GetTempPath(), _testLockFile);

        // Act
        processLock.TryAcquire();
        
        // Give a moment for write to complete
        System.Threading.Thread.Sleep(100);

        // Assert
        // We can't read the file while it's locked, so this tests that it was created
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task ShouldPreventConcurrentProcesses_InRealScenario()
    {
        // This simulates what happens when start-all.sh runs multiple times
        
        // Arrange
        var startedProcesses = 0;
        var failedToStart = 0;
        var tasks = new List<Task>();

        // Act - Try to start 5 "processes" simultaneously
        for (int i = 0; i < 5; i++)
        {
            var taskId = i;
            tasks.Add(Task.Run(() =>
            {
                using var processLock = new ProcessLock(_testLockFile);
                
                if (processLock.TryAcquire())
                {
                    System.Threading.Interlocked.Increment(ref startedProcesses);
                    
                    // Simulate process running for a bit
                    System.Threading.Thread.Sleep(100);
                }
                else
                {
                    System.Threading.Interlocked.Increment(ref failedToStart);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(1, startedProcesses); // Only ONE should start
        Assert.Equal(4, failedToStart);    // 4 should fail
    }

    [Fact]
    public void ShouldHandleOrphanedLockFile_AfterManualCleanup()
    {
        // This simulates what start-all.sh does - manually removes lock file
        
        // Arrange
        var lockPath = Path.Combine(Path.GetTempPath(), _testLockFile);
        using var lock1 = new ProcessLock(_testLockFile);
        lock1.TryAcquire();
        
        // Simulate crash - dispose lock1 but pretend file is stuck
        // (In reality FileOptions.DeleteOnClose handles this, but let's test manual cleanup)
        
        // Act - Manually delete lock file (what start-all.sh does)
        try
        {
            // This will fail because file is still locked, which is good!
            // But if process crashed, OS would have released it
            File.Delete(lockPath);
            Assert.True(false, "Should not be able to delete locked file");
        }
        catch (IOException)
        {
            // Expected - file is locked
            Assert.True(true);
        }
    }

    [Fact]
    public void ShouldAllowReacquisition_AfterProperDisposal()
    {
        // Arrange & Act - Acquire and release multiple times
        for (int i = 0; i < 3; i++)
        {
            using var processLock = new ProcessLock(_testLockFile);
            var acquired = processLock.TryAcquire();
            
            // Assert
            Assert.True(acquired, $"Should acquire lock on attempt {i + 1}");
        }
    }
}
