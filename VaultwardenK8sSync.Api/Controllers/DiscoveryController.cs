using Microsoft.AspNetCore.Mvc;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiscoveryController : ControllerBase
{
    private readonly ISecretStateRepository _repository;
    private readonly IVaultwardenService _vaultwardenService;
    private readonly ILogger<DiscoveryController> _logger;

    public DiscoveryController(
        ISecretStateRepository repository, 
        IVaultwardenService vaultwardenService,
        ILogger<DiscoveryController> logger)
    {
        _repository = repository;
        _vaultwardenService = vaultwardenService;
        _logger = logger;
    }

    /// <summary>
    /// Get discovery data showing Vaultwarden items vs synced secrets
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DiscoveryData>> GetDiscoveryData()
    {
        try
        {
            var syncedSecrets = await _repository.GetAllAsync();
            
            // Fetch Vaultwarden items using existing service
            var vaultwardenItems = await GetVaultwardenItemsAsync();
            
            var response = new DiscoveryData
            {
                VaultwardenItems = vaultwardenItems,
                
                SyncedSecrets = syncedSecrets.Select(s => new SyncedSecret
                {
                    VaultwardenItemId = s.VaultwardenItemId,
                    VaultwardenItemName = s.VaultwardenItemName,
                    Namespace = s.Namespace,
                    SecretName = s.SecretName,
                    Status = s.Status
                }).ToList(),
                
                LastScanTime = DateTime.UtcNow
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching discovery data");
            return StatusCode(500, "Error fetching discovery data");
        }
    }

    private async Task<List<VaultwardenItem>> GetVaultwardenItemsAsync()
    {
        try
        {
            // Try to authenticate if not already authenticated
            try
            {
                await _vaultwardenService.AuthenticateAsync();
            }
            catch (Exception authEx)
            {
                _logger.LogWarning(authEx, "Failed to authenticate with Vaultwarden");
                return new List<VaultwardenItem>();
            }
            
            // Use the existing Vaultwarden service
            var items = await _vaultwardenService.GetItemsAsync();
            
            // Convert to API model
            return items.Select(item => new VaultwardenItem
            {
                Id = item.Id,
                Name = item.Name,
                Folder = item.FolderId,
                OrganizationId = item.OrganizationId,
                Fields = item.Fields?.Count ?? 0,
                Notes = item.Notes
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to fetch Vaultwarden items");
            return new List<VaultwardenItem>();
        }
    }
}

public class DiscoveryData
{
    public List<VaultwardenItem> VaultwardenItems { get; set; } = new();
    public List<SyncedSecret> SyncedSecrets { get; set; } = new();
    public DateTime LastScanTime { get; set; }
}

public class VaultwardenItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Folder { get; set; }
    public string? OrganizationId { get; set; }
    public int Fields { get; set; }
    public string? Notes { get; set; }
}

public class SyncedSecret
{
    public string VaultwardenItemId { get; set; } = string.Empty;
    public string VaultwardenItemName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string SecretName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
