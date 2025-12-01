using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public interface IWebhookService
{
    /// <summary>
    /// Validates webhook signature
    /// </summary>
    bool ValidateSignature(string payload, string signature);
    
    /// <summary>
    /// Processes a webhook event
    /// </summary>
    Task<WebhookProcessingResult> ProcessWebhookAsync(WebhookEvent webhookEvent);
    
    /// <summary>
    /// Performs selective sync for specific item
    /// </summary>
    Task<SyncSummary> SyncItemAsync(string itemId);
    
    /// <summary>
    /// Performs selective sync for specific namespace
    /// </summary>
    Task<SyncSummary> SyncNamespaceAsync(string namespaceName);
}
