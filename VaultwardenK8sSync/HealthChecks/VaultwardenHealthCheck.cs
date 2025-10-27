using Microsoft.Extensions.Diagnostics.HealthChecks;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.HealthChecks;

public class VaultwardenHealthCheck : IHealthCheck
{
    private readonly IVaultwardenService _vaultwardenService;

    public VaultwardenHealthCheck(IVaultwardenService vaultwardenService)
    {
        _vaultwardenService = vaultwardenService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check if authenticated - don't trigger re-authentication
            // to avoid interfering with sync operations
            var isAuthenticated = await _vaultwardenService.IsAuthenticatedAsync();
            
            if (isAuthenticated)
            {
                return HealthCheckResult.Healthy("Vaultwarden is authenticated");
            }
            else
            {
                return HealthCheckResult.Degraded("Vaultwarden is not authenticated");
            }
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to check Vaultwarden status",
                ex);
        }
    }
}
