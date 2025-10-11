namespace VaultwardenK8sSync.Models;

/// <summary>
/// Represents a webhook event from Vaultwarden
/// </summary>
public class WebhookEvent
{
    public string EventType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string? CollectionId { get; set; }
    public string? UserId { get; set; }
    public WebhookEventData? Data { get; set; }
}

public class WebhookEventData
{
    public string? ItemName { get; set; }
    public string? ItemType { get; set; }
    public List<string>? AffectedNamespaces { get; set; }
}

/// <summary>
/// Webhook event types
/// </summary>
public static class WebhookEventTypes
{
    public const string ItemCreated = "item.created";
    public const string ItemUpdated = "item.updated";
    public const string ItemDeleted = "item.deleted";
    public const string ItemRestored = "item.restored";
    public const string ItemMoved = "item.moved";
    public const string ItemShared = "item.shared";
}

/// <summary>
/// Result of webhook processing
/// </summary>
public class WebhookProcessingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int ItemsProcessed { get; set; }
    public int SecretsAffected { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
}
