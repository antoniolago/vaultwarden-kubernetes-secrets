using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Infrastructure;
using Xunit;

namespace VaultwardenK8sSync.Tests.Infrastructure;

[Collection("GlobalSyncLock Sequential")]
public class GlobalSyncLockTests : IDisposable
{
    private const string LockFileName = "vaultwarden-sync-operation-test.lock";
    private static string LockFilePath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), LockFileName);

    public void Dispose()
    {
        try
        {
            if (File.Exists(LockFilePath))
                File.Delete(LockFilePath);
        }
        catch { }
    }

    [Fact]
    public async Task TryAcquireAsync_WhenLockAvailable_ReturnsTrue()
    {
        var loggerMock = new Mock<ILogger<GlobalSyncLock>>();
        using var @lock = new GlobalSyncLock(loggerMock.Object, timeoutMs: 1000, lockFileName: LockFileName);

        var result = await @lock.TryAcquireAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_SecondInstance_WhenFirstHoldsLock_ReturnsFalse()
    {
        var loggerMock = new Mock<ILogger<GlobalSyncLock>>();
        using var lock1 = new GlobalSyncLock(loggerMock.Object, timeoutMs: 1000, lockFileName: LockFileName);
        using var lock2 = new GlobalSyncLock(loggerMock.Object, timeoutMs: 1000, lockFileName: LockFileName);

        var result1 = await lock1.TryAcquireAsync();
        result1.Should().BeTrue();

        var result2 = await lock2.TryAcquireAsync();
        result2.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_AfterFirstReleased_ReturnsTrue()
    {
        var loggerMock = new Mock<ILogger<GlobalSyncLock>>();
        using var lock1 = new GlobalSyncLock(loggerMock.Object, timeoutMs: 1000, lockFileName: LockFileName);
        using var lock2 = new GlobalSyncLock(loggerMock.Object, timeoutMs: 1000, lockFileName: LockFileName);

        var result1 = await lock1.TryAcquireAsync();
        result1.Should().BeTrue();

        await lock1.DisposeAsync();

        var result2 = await lock2.TryAcquireAsync();
        result2.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_QuickTimeout_ReturnsFalse()
    {
        var loggerMock = new Mock<ILogger<GlobalSyncLock>>();
        using var lock1 = new GlobalSyncLock(loggerMock.Object, timeoutMs: 50, lockFileName: LockFileName);
        using var lock2 = new GlobalSyncLock(loggerMock.Object, timeoutMs: 50, lockFileName: LockFileName);

        await lock1.TryAcquireAsync();
        var result2 = await lock2.TryAcquireAsync();

        result2.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_AfterAcquire_ReleasesLock()
    {
        var loggerMock = new Mock<ILogger<GlobalSyncLock>>();
        using var lock1 = new GlobalSyncLock(loggerMock.Object, lockFileName: LockFileName);

        await lock1.TryAcquireAsync();
        lock1.Dispose();

        using var lock2 = new GlobalSyncLock(loggerMock.Object, lockFileName: LockFileName);
        var result = await lock2.TryAcquireAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_AfterAcquire_ReleasesLock()
    {
        var loggerMock = new Mock<ILogger<GlobalSyncLock>>();
        var lock1 = new GlobalSyncLock(loggerMock.Object, lockFileName: LockFileName);

        await lock1.TryAcquireAsync();
        await lock1.DisposeAsync();

        await Task.Delay(50);

        var lock2 = new GlobalSyncLock(loggerMock.Object, lockFileName: LockFileName);
        var result = await lock2.TryAcquireAsync();
        result.Should().BeTrue();
        await lock2.DisposeAsync();
    }
}