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
            // Try to get items to verify connection
            var items = await _vaultwardenService.GetItemsAsync();
            
            return HealthCheckResult.Healthy(
                $"Vaultwarden is accessible. {items.Count} items available.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Failed to connect to Vaultwarden",
                ex);
        }
    }
}
