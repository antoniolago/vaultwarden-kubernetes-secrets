using Microsoft.Extensions.Diagnostics.HealthChecks;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.HealthChecks;

public class SyncHealthCheck : IHealthCheck
{
    private readonly IMetricsService _metricsService;
    private readonly TimeSpan _maxTimeSinceLastSync = TimeSpan.FromMinutes(10);

    public SyncHealthCheck(IMetricsService metricsService)
    {
        _metricsService = metricsService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var lastSync = _metricsService.GetLastSuccessfulSync();
        
        if (lastSync == null)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "No successful sync has been completed yet"));
        }

        var timeSinceLastSync = DateTime.UtcNow - lastSync.Value;
        
        if (timeSinceLastSync > _maxTimeSinceLastSync)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Last successful sync was {timeSinceLastSync.TotalMinutes:F1} minutes ago"));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Last successful sync was {timeSinceLastSync.TotalSeconds:F0} seconds ago"));
    }
}
