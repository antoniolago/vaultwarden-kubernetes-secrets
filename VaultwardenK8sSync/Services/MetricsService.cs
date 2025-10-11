using Prometheus;

namespace VaultwardenK8sSync.Services;

public class MetricsService : IMetricsService
{
    // Sync metrics
    private static readonly Histogram SyncDuration = Metrics.CreateHistogram(
        "vaultwarden_sync_duration_seconds",
        "Duration of sync operations in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.1, 2, 10),
            LabelNames = new[] { "success" }
        });

    private static readonly Counter SyncTotal = Metrics.CreateCounter(
        "vaultwarden_sync_total",
        "Total number of sync operations",
        new CounterConfiguration
        {
            LabelNames = new[] { "success" }
        });

    private static readonly Counter SecretsTotal = Metrics.CreateCounter(
        "vaultwarden_secrets_synced_total",
        "Total number of secrets synced",
        new CounterConfiguration
        {
            LabelNames = new[] { "operation" }
        });

    private static readonly Counter SyncErrors = Metrics.CreateCounter(
        "vaultwarden_sync_errors_total",
        "Total number of sync errors",
        new CounterConfiguration
        {
            LabelNames = new[] { "error_type" }
        });

    // Vaultwarden metrics
    private static readonly Gauge ItemsWatched = Metrics.CreateGauge(
        "vaultwarden_items_watched",
        "Number of items currently watched from Vaultwarden");

    private static readonly Counter VaultwardenApiCalls = Metrics.CreateCounter(
        "vaultwarden_api_calls_total",
        "Total number of Vaultwarden API calls",
        new CounterConfiguration
        {
            LabelNames = new[] { "operation", "success" }
        });

    // Kubernetes metrics
    private static readonly Counter KubernetesApiCalls = Metrics.CreateCounter(
        "vaultwarden_kubernetes_api_calls_total",
        "Total number of Kubernetes API calls",
        new CounterConfiguration
        {
            LabelNames = new[] { "operation", "success" }
        });

    // Health metrics
    private static readonly Gauge LastSuccessfulSyncTimestamp = Metrics.CreateGauge(
        "vaultwarden_last_successful_sync_timestamp",
        "Unix timestamp of the last successful sync");

    private DateTime? _lastSuccessfulSync;

    public void RecordSyncDuration(double durationSeconds, bool success)
    {
        SyncDuration.WithLabels(success.ToString().ToLower()).Observe(durationSeconds);
        SyncTotal.WithLabels(success.ToString().ToLower()).Inc();
    }

    public void RecordSecretsSynced(int count, string operation)
    {
        SecretsTotal.WithLabels(operation).Inc(count);
    }

    public void RecordSyncError(string errorType)
    {
        SyncErrors.WithLabels(errorType).Inc();
    }

    public void RecordItemsWatched(int count)
    {
        ItemsWatched.Set(count);
    }

    public void RecordVaultwardenApiCall(string operation, bool success)
    {
        VaultwardenApiCalls.WithLabels(operation, success.ToString().ToLower()).Inc();
    }

    public void RecordKubernetesApiCall(string operation, bool success)
    {
        KubernetesApiCalls.WithLabels(operation, success.ToString().ToLower()).Inc();
    }

    public void SetLastSuccessfulSync()
    {
        _lastSuccessfulSync = DateTime.UtcNow;
        LastSuccessfulSyncTimestamp.Set(new DateTimeOffset(_lastSuccessfulSync.Value).ToUnixTimeSeconds());
    }

    public DateTime? GetLastSuccessfulSync()
    {
        return _lastSuccessfulSync;
    }
}
