using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public class WebhookService : IWebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly IVaultwardenService _vaultwardenService;
    private readonly IKubernetesService _kubernetesService;
    private readonly ISyncService _syncService;
    private readonly IMetricsService _metricsService;
    private readonly WebhookSettings _webhookSettings;

    public WebhookService(
        ILogger<WebhookService> logger,
        IVaultwardenService vaultwardenService,
        IKubernetesService kubernetesService,
        ISyncService syncService,
        IMetricsService metricsService,
        WebhookSettings webhookSettings)
    {
        _logger = logger;
        _vaultwardenService = vaultwardenService;
        _kubernetesService = kubernetesService;
        _syncService = syncService;
        _metricsService = metricsService;
        _webhookSettings = webhookSettings;
    }

    public bool ValidateSignature(string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(_webhookSettings.Secret))
        {
            _logger.LogWarning("Webhook secret not configured - skipping signature validation");
            return true; // Allow if no secret configured (for testing)
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("No signature provided in webhook request");
            return false;
        }

        try
        {
            // Remove "sha256=" prefix if present
            var signatureValue = signature.StartsWith("sha256=") 
                ? signature.Substring(7) 
                : signature;

            // Compute HMAC-SHA256
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_webhookSettings.Secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

            var isValid = computedSignature.Equals(signatureValue, StringComparison.OrdinalIgnoreCase);
            
            if (!isValid)
            {
                _logger.LogWarning("Webhook signature validation failed");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook signature");
            return false;
        }
    }

    public async Task<WebhookProcessingResult> ProcessWebhookAsync(WebhookEvent webhookEvent)
    {
        var startTime = DateTime.UtcNow;
        var result = new WebhookProcessingResult();

        try
        {
            _logger.LogInformation(
                "Processing webhook event: {EventType} for item {ItemId}",
                webhookEvent.EventType,
                webhookEvent.ItemId);

            // Record webhook received metric
            _metricsService.RecordVaultwardenApiCall("webhook_received", true);

            SyncSummary? syncSummary = null;

            switch (webhookEvent.EventType)
            {
                case WebhookEventTypes.ItemCreated:
                case WebhookEventTypes.ItemUpdated:
                case WebhookEventTypes.ItemRestored:
                    // Sync the specific item
                    syncSummary = await SyncItemAsync(webhookEvent.ItemId);
                    break;

                case WebhookEventTypes.ItemDeleted:
                    // Trigger full sync to clean up orphaned secrets
                    _logger.LogInformation("Item deleted - triggering full sync to clean up orphans");
                    syncSummary = await _syncService.SyncAsync();
                    break;

                case WebhookEventTypes.ItemMoved:
                case WebhookEventTypes.ItemShared:
                    // These might affect multiple namespaces, do full sync
                    _logger.LogInformation("Item moved/shared - triggering full sync");
                    syncSummary = await _syncService.SyncAsync();
                    break;

                default:
                    _logger.LogWarning("Unknown webhook event type: {EventType}", webhookEvent.EventType);
                    result.Success = false;
                    result.ErrorMessage = $"Unknown event type: {webhookEvent.EventType}";
                    return result;
            }

            if (syncSummary != null)
            {
                result.Success = syncSummary.OverallSuccess;
                result.ItemsProcessed = syncSummary.TotalItemsFromVaultwarden;
                result.SecretsAffected = syncSummary.TotalSecretsProcessed;
                
                if (!syncSummary.OverallSuccess)
                {
                    result.ErrorMessage = string.Join("; ", syncSummary.Errors);
                }
            }

            result.ProcessingDuration = DateTime.UtcNow - startTime;

            _logger.LogInformation(
                "Webhook processing completed: Success={Success}, Duration={Duration}ms",
                result.Success,
                result.ProcessingDuration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook event");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ProcessingDuration = DateTime.UtcNow - startTime;
            
            _metricsService.RecordSyncError("webhook_processing_error");
            
            return result;
        }
    }

    public async Task<SyncSummary> SyncItemAsync(string itemId)
    {
        try
        {
            _logger.LogDebug("Performing selective sync for item {ItemId}", itemId);

            // Get the specific item
            var allItems = await _vaultwardenService.GetItemsAsync();
            var item = allItems.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                _logger.LogWarning("Item {ItemId} not found in Vaultwarden", itemId);
                
                // Item might have been deleted, trigger full sync
                return await _syncService.SyncAsync();
            }

            // Extract namespaces from the item
            var namespaces = item.ExtractNamespaces();
            
            if (!namespaces.Any())
            {
                _logger.LogDebug("Item {ItemId} has no namespace tags - skipping sync", itemId);
                return new SyncSummary
                {
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow,
                    OverallSuccess = true
                };
            }

            // Sync each affected namespace
            var summary = new SyncSummary
            {
                StartTime = DateTime.UtcNow,
                TotalItemsFromVaultwarden = 1
            };

            foreach (var namespaceName in namespaces)
            {
                var namespaceSuccess = await _syncService.SyncNamespaceAsync(namespaceName);
                if (!namespaceSuccess)
                {
                    summary.AddError($"Failed to sync namespace {namespaceName}");
                }
            }

            summary.EndTime = DateTime.UtcNow;
            
            _logger.LogDebug(
                "Selective sync completed for item {ItemId}: Success={Success}",
                itemId,
                summary.OverallSuccess);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing item {ItemId}", itemId);
            throw;
        }
    }

    public async Task<SyncSummary> SyncNamespaceAsync(string namespaceName)
    {
        try
        {
            _logger.LogDebug("Performing selective sync for namespace {Namespace}", namespaceName);

            var success = await _syncService.SyncNamespaceAsync(namespaceName);
            
            return new SyncSummary
            {
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                OverallSuccess = success
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing namespace {Namespace}", namespaceName);
            throw;
        }
    }
}
