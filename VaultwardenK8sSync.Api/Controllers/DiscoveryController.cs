using Microsoft.AspNetCore.Mvc;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiscoveryController : ControllerBase
{
    private readonly ISecretStateRepository _secretStateRepository;
    private readonly IVaultwardenItemRepository _vaultwardenItemRepository;
    private readonly IVaultwardenService _vaultwardenService;
    private readonly ILogger<DiscoveryController> _logger;

    public DiscoveryController(
        ISecretStateRepository secretStateRepository,
        IVaultwardenItemRepository vaultwardenItemRepository,
        IVaultwardenService vaultwardenService,
        ILogger<DiscoveryController> logger)
    {
        _secretStateRepository = secretStateRepository;
        _vaultwardenItemRepository = vaultwardenItemRepository;
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
            var syncedSecrets = await _secretStateRepository.GetAllAsync();
            
            // Read cached Vaultwarden items from database (populated by sync service)
            var cachedItems = await _vaultwardenItemRepository.GetAllAsync();
            var lastFetch = await _vaultwardenItemRepository.GetLastFetchTimeAsync();
            
            var vaultwardenItems = cachedItems.Select(item => new VaultwardenItem
            {
                Id = item.ItemId,
                Name = item.Name,
                Folder = item.FolderId,
                OrganizationId = item.OrganizationId,
                OrganizationName = item.OrganizationName,
                Owner = item.Owner,
                Fields = item.FieldCount,
                Notes = item.Notes,
                HasNamespacesField = item.HasNamespacesField
            }).ToList();
            
            var response = new DiscoveryData
            {
                VaultwardenItems = vaultwardenItems,
                
                SyncedSecrets = syncedSecrets.Select(s => new SyncedSecret
                {
                    VaultwardenItemId = s.VaultwardenItemId,
                    VaultwardenItemName = s.VaultwardenItemName,
                    Namespace = s.Namespace,
                    SecretName = s.SecretName,
                    Status = s.Status,
                    DataKeysCount = s.DataKeysCount
                }).ToList(),
                
                LastScanTime = lastFetch ?? DateTime.UtcNow
            };
            
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching discovery data");
            return StatusCode(500, new { 
                error = "Error fetching discovery data", 
                message = ex.Message,
                details = ex.InnerException?.Message 
            });
        }
    }

    /// <summary>
    /// Get custom field names for a Vaultwarden item from cache
    /// </summary>
    [HttpGet("/api/vaultwarden/items/{itemId}/fields")]
    public async Task<ActionResult<List<string>>> GetItemFields(string itemId)
    {
        try
        {
            var cachedItem = await _vaultwardenItemRepository.GetByItemIdAsync(itemId);
            
            if (cachedItem == null)
            {
                return NotFound(new { error = "Item not found in cache", itemId });
            }
            
            if (string.IsNullOrEmpty(cachedItem.FieldNamesJson))
            {
                return Ok(new List<string>());
            }
            
            var fieldNames = System.Text.Json.JsonSerializer.Deserialize<List<string>>(cachedItem.FieldNamesJson) 
                ?? new List<string>();
            
            return Ok(fieldNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching fields for item {ItemId}", itemId);
            return StatusCode(500, new { 
                error = "Error fetching item fields", 
                message = ex.Message 
            });
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
    public string? OrganizationName { get; set; }
    public string? Owner { get; set; }
    public int Fields { get; set; }
    public string? Notes { get; set; }
    public bool HasNamespacesField { get; set; }
}

public class SyncedSecret
{
    public string VaultwardenItemId { get; set; } = string.Empty;
    public string VaultwardenItemName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string SecretName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DataKeysCount { get; set; }
}
