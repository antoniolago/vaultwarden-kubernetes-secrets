using Microsoft.Extensions.Diagnostics.HealthChecks;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.HealthChecks;

public class KubernetesHealthCheck : IHealthCheck
{
    private readonly IKubernetesService _kubernetesService;

    public KubernetesHealthCheck(IKubernetesService kubernetesService)
    {
        _kubernetesService = kubernetesService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to list namespaces to verify connection
            var namespaces = await _kubernetesService.GetAllNamespacesAsync();
            
            return HealthCheckResult.Healthy(
                $"Kubernetes is accessible. {namespaces.Count} namespaces available.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to connect to Kubernetes",
                ex);
        }
    }
}
