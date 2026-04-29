using FluentAssertions;
using Prometheus;
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

    private static T GetMetricField<T>(string fieldName) where T : class
    {
        var field = typeof(MetricsService).GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (T)field!.GetValue(null)!;
    }

    [Fact]
    public void RecordSyncDuration_WithSuccess_RecordsMetric()
    {
        var syncTotal = GetMetricField<Counter>("SyncTotal");
        var before = syncTotal.WithLabels("true").Value;

        _metricsService.RecordSyncDuration(1.5, true);
        _metricsService.RecordSyncDuration(2.0, true);

        syncTotal.WithLabels("true").Value.Should().Be(before + 2);
    }

    [Fact]
    public void RecordSyncDuration_WithFailure_RecordsMetric()
    {
        var syncTotal = GetMetricField<Counter>("SyncTotal");
        var before = syncTotal.WithLabels("false").Value;

        _metricsService.RecordSyncDuration(0.5, false);

        syncTotal.WithLabels("false").Value.Should().Be(before + 1);
    }

    [Fact]
    public void RecordSecretsSynced_WithOperation_RecordsCounter()
    {
        var secretsTotal = GetMetricField<Counter>("SecretsTotal");

        _metricsService.RecordSecretsSynced(5, "created");
        _metricsService.RecordSecretsSynced(3, "updated");
        _metricsService.RecordSecretsSynced(1, "deleted");

        secretsTotal.WithLabels("created").Value.Should().Be(5);
        secretsTotal.WithLabels("updated").Value.Should().Be(3);
        secretsTotal.WithLabels("deleted").Value.Should().Be(1);
    }

    [Fact]
    public void RecordSyncError_WithErrorType_RecordsCounter()
    {
        var syncErrors = GetMetricField<Counter>("SyncErrors");

        _metricsService.RecordSyncError("KubernetesApiError");
        _metricsService.RecordSyncError("VaultwardenApiError");
        _metricsService.RecordSyncError("TimeoutError");

        syncErrors.WithLabels("KubernetesApiError").Value.Should().Be(1);
        syncErrors.WithLabels("VaultwardenApiError").Value.Should().Be(1);
        syncErrors.WithLabels("TimeoutError").Value.Should().Be(1);
    }

    [Fact]
    public void RecordItemsWatched_SetsGauge()
    {
        var itemsWatched = GetMetricField<Gauge>("ItemsWatched");

        _metricsService.RecordItemsWatched(100);
        _metricsService.RecordItemsWatched(50);

        itemsWatched.Value.Should().Be(50);
    }

    [Fact]
    public void RecordVaultwardenApiCall_WithSuccess_RecordsCounter()
    {
        var vwApiCalls = GetMetricField<Counter>("VaultwardenApiCalls");

        _metricsService.RecordVaultwardenApiCall("get_items", true);
        _metricsService.RecordVaultwardenApiCall("get_item", true);

        vwApiCalls.WithLabels("get_items", "true").Value.Should().Be(1);
        vwApiCalls.WithLabels("get_item", "true").Value.Should().Be(1);
    }

    [Fact]
    public void RecordVaultwardenApiCall_WithFailure_RecordsCounter()
    {
        var vwApiCalls = GetMetricField<Counter>("VaultwardenApiCalls");

        _metricsService.RecordVaultwardenApiCall("get_items", false);

        vwApiCalls.WithLabels("get_items", "false").Value.Should().Be(1);
    }

    [Fact]
    public void RecordKubernetesApiCall_WithSuccess_RecordsCounter()
    {
        var k8sApiCalls = GetMetricField<Counter>("KubernetesApiCalls");

        _metricsService.RecordKubernetesApiCall("create_secret", true);
        _metricsService.RecordKubernetesApiCall("update_secret", true);

        k8sApiCalls.WithLabels("create_secret", "true").Value.Should().Be(1);
        k8sApiCalls.WithLabels("update_secret", "true").Value.Should().Be(1);
    }

    [Fact]
    public void RecordKubernetesApiCall_WithFailure_RecordsCounter()
    {
        var k8sApiCalls = GetMetricField<Counter>("KubernetesApiCalls");

        _metricsService.RecordKubernetesApiCall("delete_secret", false);

        k8sApiCalls.WithLabels("delete_secret", "false").Value.Should().Be(1);
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
    public async Task SetLastSuccessfulSync_CalledTwice_UpdatesTimestamp()
    {
        var service = new MetricsService();
        var first = DateTime.UtcNow;
        service.SetLastSuccessfulSync();

        // Poll until clock advances or timeout
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow <= first && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1);
        }

        var second = DateTime.UtcNow;
        service.SetLastSuccessfulSync();

        var result = service.GetLastSuccessfulSync();
        result.Should().NotBeNull();
        result!.Value.Should().BeAfter(first);
        result.Value.Should().BeBefore(second.AddSeconds(1));
    }
}