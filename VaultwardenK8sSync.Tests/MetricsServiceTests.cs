using FluentAssertions;
using VaultwardenK8sSync.Services;
using Xunit;

namespace VaultwardenK8sSync.Tests;

public class MetricsServiceTests
{
    private readonly MetricsService _metricsService;

    public MetricsServiceTests()
    {
        _metricsService = new MetricsService();
    }

    [Fact]
    public void RecordSyncDuration_WithSuccess_RecordsMetric()
    {
        _metricsService.RecordSyncDuration(1.5, true);
        _metricsService.RecordSyncDuration(2.0, true);
    }

    [Fact]
    public void RecordSyncDuration_WithFailure_RecordsMetric()
    {
        _metricsService.RecordSyncDuration(0.5, false);
    }

    [Fact]
    public void RecordSecretsSynced_WithOperation_RecordsCounter()
    {
        _metricsService.RecordSecretsSynced(5, "created");
        _metricsService.RecordSecretsSynced(3, "updated");
        _metricsService.RecordSecretsSynced(1, "deleted");
    }

    [Fact]
    public void RecordSyncError_WithErrorType_RecordsCounter()
    {
        _metricsService.RecordSyncError("KubernetesApiError");
        _metricsService.RecordSyncError("VaultwardenApiError");
        _metricsService.RecordSyncError("TimeoutError");
    }

    [Fact]
    public void RecordItemsWatched_SetsGauge()
    {
        _metricsService.RecordItemsWatched(100);
        _metricsService.RecordItemsWatched(50);
    }

    [Fact]
    public void RecordVaultwardenApiCall_WithSuccess_RecordsCounter()
    {
        _metricsService.RecordVaultwardenApiCall("get_items", true);
        _metricsService.RecordVaultwardenApiCall("get_item", true);
    }

    [Fact]
    public void RecordVaultwardenApiCall_WithFailure_RecordsCounter()
    {
        _metricsService.RecordVaultwardenApiCall("get_items", false);
    }

    [Fact]
    public void RecordKubernetesApiCall_WithSuccess_RecordsCounter()
    {
        _metricsService.RecordKubernetesApiCall("create_secret", true);
        _metricsService.RecordKubernetesApiCall("update_secret", true);
    }

    [Fact]
    public void RecordKubernetesApiCall_WithFailure_RecordsCounter()
    {
        _metricsService.RecordKubernetesApiCall("delete_secret", false);
    }

    [Fact]
    public void SetLastSuccessfulSync_SetsTimestamp()
    {
        var before = DateTime.UtcNow;
        _metricsService.SetLastSuccessfulSync();
        var after = DateTime.UtcNow;

        var lastSync = _metricsService.GetLastSuccessfulSync();
        lastSync.Should().NotBeNull();
        lastSync!.Value.Should().BeAfter(before.AddSeconds(-1));
        lastSync.Value.Should().BeBefore(after.AddSeconds(1));
    }

    [Fact]
    public void GetLastSuccessfulSync_BeforeSet_ReturnsNull()
    {
        var service = new MetricsService();
        var result = service.GetLastSuccessfulSync();
        result.Should().BeNull();
    }

    [Fact]
    public void SetLastSuccessfulSync_CalledTwice_UpdatesTimestamp()
    {
        var service = new MetricsService();
        var first = DateTime.UtcNow;
        service.SetLastSuccessfulSync();
        
        Thread.Sleep(10);
        
        var second = DateTime.UtcNow;
        service.SetLastSuccessfulSync();
        
        var result = service.GetLastSuccessfulSync();
        result.Should().NotBeNull();
        result!.Value.Should().BeAfter(first);
        result.Value.Should().BeBefore(second.AddSeconds(1));
    }
}