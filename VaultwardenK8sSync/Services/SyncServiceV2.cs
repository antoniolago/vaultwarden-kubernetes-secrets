using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public class SyncServiceV2 : ISyncServiceV2
{
    private readonly ILogger<SyncServiceV2> _logger;
    private readonly IVaultwardenServiceV2 _vaultwardenService;
    private readonly IKubernetesService _kubernetesService;
    private readonly SyncConfig _syncConfig;

    public SyncServiceV2(
        ILogger<SyncServiceV2> logger,
        IVaultwardenServiceV2 vaultwardenService,
        IKubernetesService kubernetesService,
        SyncConfig syncConfig)
    {
        _logger = logger;
        _vaultwardenService = vaultwardenService;
        _kubernetesService = kubernetesService;
        _syncConfig = syncConfig;
    }

    public async Task<bool> SyncAsync()
    {
        try
        {
            _logger.LogInformation("Starting Vaultwarden to Kubernetes sync (V2 - VwConnector)...");

            // Get all items from Vaultwarden
            var items = await _vaultwardenService.GetItemsAsync();
            if (!items.Any())
            {
                _logger.LogWarning("No items found in Vaultwarden vault");
                return true;
            }

            // Filter out deleted items
            var activeItems = items.Where(item => !item.Deleted).ToList();

            // Group items by namespace
            var itemsByNamespace = activeItems
                .Where(item => !string.IsNullOrEmpty(item.ExtractNamespace()))
                .GroupBy(item => item.ExtractNamespace()!)
                .ToDictionary(g => g.Key, g => g.ToList());

            _logger.LogInformation("Found {Count} active items with namespace tags across {NamespaceCount} namespaces", 
                itemsByNamespace.Values.Sum(x => x.Count), itemsByNamespace.Count);

            // Sync each namespace
            var success = true;
            foreach (var (namespaceName, namespaceItems) in itemsByNamespace)
            {
                try
                {
                    var namespaceSuccess = await SyncNamespaceAsync(namespaceName, namespaceItems);
                    if (!namespaceSuccess)
                    {
                        success = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync namespace {Namespace}", namespaceName);
                    success = false;
                }
            }

            // Cleanup orphaned secrets if enabled
            if (_syncConfig.DeleteOrphans && success)
            {
                await CleanupOrphanedSecretsAsync();
            }

            _logger.LogInformation("Sync completed with status: {Success}", success);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform sync");
            return false;
        }
    }

    public async Task<bool> SyncNamespaceAsync(string namespaceName)
    {
        var items = await _vaultwardenService.GetItemsAsync();
        var namespaceItems = items
            .Where(item => !item.Deleted && item.ExtractNamespace() == namespaceName)
            .ToList();

        return await SyncNamespaceAsync(namespaceName, namespaceItems);
    }

    private async Task<bool> SyncNamespaceAsync(string namespaceName, List<Models.VaultwardenItemV2> items)
    {
        try
        {
            _logger.LogInformation("Syncing {Count} items to namespace {Namespace} (V2)", items.Count, namespaceName);

            var success = true;
            foreach (var item in items)
            {
                try
                {
                    var itemSuccess = await SyncItemAsync(namespaceName, item);
                    if (!itemSuccess)
                    {
                        success = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync item {ItemName} to namespace {Namespace}", item.Name, namespaceName);
                    success = false;
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync namespace {Namespace}", namespaceName);
            return false;
        }
    }

    private async Task<bool> SyncItemAsync(string namespaceName, Models.VaultwardenItemV2 item)
    {
        try
        {
            var secretName = $"{_syncConfig.SecretPrefix}{SanitizeSecretName(item.Name)}";
            var secretData = await ExtractSecretDataAsync(item);

            if (_syncConfig.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would sync item {ItemName} as secret {SecretName} in namespace {Namespace} (V2)", 
                    item.Name, secretName, namespaceName);
                return true;
            }

            var exists = await _kubernetesService.SecretExistsAsync(namespaceName, secretName);
            bool success;

            if (exists)
            {
                success = await _kubernetesService.UpdateSecretAsync(namespaceName, secretName, secretData);
                if (success)
                {
                    _logger.LogInformation("Updated secret {SecretName} in namespace {Namespace} (V2)", secretName, namespaceName);
                }
            }
            else
            {
                success = await _kubernetesService.CreateSecretAsync(namespaceName, secretName, secretData);
                if (success)
                {
                    _logger.LogInformation("Created secret {SecretName} in namespace {Namespace} (V2)", secretName, namespaceName);
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync item {ItemName} to namespace {Namespace}", item.Name, namespaceName);
            return false;
        }
    }

    private Task<Dictionary<string, string>> ExtractSecretDataAsync(Models.VaultwardenItemV2 item)
    {
        var data = new Dictionary<string, string>();

        // Add basic fields
        if (!string.IsNullOrEmpty(item.Username))
            data["username"] = item.Username;

        if (!string.IsNullOrEmpty(item.Password))
            data["password"] = item.Password;

        if (!string.IsNullOrEmpty(item.Url))
            data["url"] = item.Url;

        if (!string.IsNullOrEmpty(item.Notes))
            data["notes"] = item.Notes;

        // Add login-specific fields
        if (item.Login != null)
        {
            if (!string.IsNullOrEmpty(item.Login.Username))
                data["login_username"] = item.Login.Username;

            if (!string.IsNullOrEmpty(item.Login.Password))
                data["login_password"] = item.Login.Password;

            if (!string.IsNullOrEmpty(item.Login.Totp))
                data["totp"] = item.Login.Totp;

            if (item.Login.Uris?.Any() == true)
            {
                for (int i = 0; i < item.Login.Uris.Count; i++)
                {
                    data[$"uri_{i}"] = item.Login.Uris[i].Uri;
                }
            }
        }

        // Add custom fields
        if (item.Fields?.Any() == true)
        {
            foreach (var field in item.Fields)
            {
                var fieldName = SanitizeFieldName(field.Name);
                data[fieldName] = field.Value;
            }
        }

        // Add card information
        if (item.Card != null)
        {
            if (!string.IsNullOrEmpty(item.Card.CardholderName))
                data["cardholder_name"] = item.Card.CardholderName;

            if (!string.IsNullOrEmpty(item.Card.Brand))
                data["card_brand"] = item.Card.Brand;

            if (!string.IsNullOrEmpty(item.Card.Number))
                data["card_number"] = item.Card.Number;

            if (!string.IsNullOrEmpty(item.Card.ExpMonth))
                data["card_exp_month"] = item.Card.ExpMonth;

            if (!string.IsNullOrEmpty(item.Card.ExpYear))
                data["card_exp_year"] = item.Card.ExpYear;

            if (!string.IsNullOrEmpty(item.Card.Code))
                data["card_code"] = item.Card.Code;
        }

        // Add identity information
        if (item.Identity != null)
        {
            var identityFields = new[]
            {
                ("title", item.Identity.Title),
                ("first_name", item.Identity.FirstName),
                ("middle_name", item.Identity.MiddleName),
                ("last_name", item.Identity.LastName),
                ("email", item.Identity.Email),
                ("phone", item.Identity.Phone),
                ("company", item.Identity.Company),
                ("address1", item.Identity.Address1),
                ("address2", item.Identity.Address2),
                ("city", item.Identity.City),
                ("state", item.Identity.State),
                ("postal_code", item.Identity.PostalCode),
                ("country", item.Identity.Country),
                ("ssn", item.Identity.Ssn),
                ("passport_number", item.Identity.PassportNumber),
                ("license_number", item.Identity.LicenseNumber)
            };

            foreach (var (fieldName, value) in identityFields)
            {
                if (!string.IsNullOrEmpty(value))
                    data[fieldName] = value;
            }
        }

        return Task.FromResult(data);
    }

    public async Task<bool> CleanupOrphanedSecretsAsync()
    {
        try
        {
            _logger.LogInformation("Starting cleanup of orphaned secrets (V2)...");

            // Get all items from Vaultwarden
            var items = await _vaultwardenService.GetItemsAsync();
            var activeItems = items.Where(item => !item.Deleted).ToList();
            var itemsByNamespace = activeItems
                .Where(item => !string.IsNullOrEmpty(item.ExtractNamespace()))
                .GroupBy(item => item.ExtractNamespace()!)
                .ToDictionary(g => g.Key, g => g.ToList());

            var success = true;
            foreach (var (namespaceName, namespaceItems) in itemsByNamespace)
            {
                try
                {
                    var namespaceSuccess = await CleanupOrphanedSecretsInNamespaceAsync(namespaceName, namespaceItems);
                    if (!namespaceSuccess)
                    {
                        success = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cleanup orphaned secrets in namespace {Namespace}", namespaceName);
                    success = false;
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup orphaned secrets");
            return false;
        }
    }

    private async Task<bool> CleanupOrphanedSecretsInNamespaceAsync(string namespaceName, List<Models.VaultwardenItemV2> items)
    {
        try
        {
            // Get existing secrets in the namespace
            var existingSecrets = await _kubernetesService.GetExistingSecretNamesAsync(namespaceName);
            var managedSecrets = existingSecrets.Where(s => s.StartsWith(_syncConfig.SecretPrefix)).ToList();

            // Calculate expected secret names
            var expectedSecrets = items.Select(item => $"{_syncConfig.SecretPrefix}{SanitizeSecretName(item.Name)}").ToHashSet();

            // Find orphaned secrets
            var orphanedSecrets = managedSecrets.Where(s => !expectedSecrets.Contains(s)).ToList();

            if (!orphanedSecrets.Any())
            {
                _logger.LogInformation("No orphaned secrets found in namespace {Namespace} (V2)", namespaceName);
                return true;
            }

            _logger.LogInformation("Found {Count} orphaned secrets in namespace {Namespace} (V2)", orphanedSecrets.Count, namespaceName);

            var success = true;
            foreach (var orphanedSecret in orphanedSecrets)
            {
                try
                {
                    if (_syncConfig.DryRun)
                    {
                        _logger.LogInformation("[DRY RUN] Would delete orphaned secret {SecretName} in namespace {Namespace} (V2)", 
                            orphanedSecret, namespaceName);
                    }
                    else
                    {
                        var deleteSuccess = await _kubernetesService.DeleteSecretAsync(namespaceName, orphanedSecret);
                        if (deleteSuccess)
                        {
                            _logger.LogInformation("Deleted orphaned secret {SecretName} in namespace {Namespace} (V2)", 
                                orphanedSecret, namespaceName);
                        }
                        else
                        {
                            success = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete orphaned secret {SecretName} in namespace {Namespace}", 
                        orphanedSecret, namespaceName);
                    success = false;
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup orphaned secrets in namespace {Namespace}", namespaceName);
            return false;
        }
    }

    private static string SanitizeSecretName(string name)
    {
        // Kubernetes secret names must be lowercase, contain only alphanumeric characters, '-', '_', or '.'
        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-")
            .Replace(".", "-")
            .Replace("/", "-")
            .Replace("\\", "-")
            .Replace(":", "-")
            .Replace(";", "-")
            .Replace(",", "-")
            .Replace("(", "-")
            .Replace(")", "-")
            .Replace("[", "-")
            .Replace("]", "-")
            .Replace("{", "-")
            .Replace("}", "-")
            .Replace("'", "-")
            .Replace("\"", "-")
            .Replace("`", "-")
            .Replace("~", "-")
            .Replace("!", "-")
            .Replace("@", "-")
            .Replace("#", "-")
            .Replace("$", "-")
            .Replace("%", "-")
            .Replace("^", "-")
            .Replace("&", "-")
            .Replace("*", "-")
            .Replace("+", "-")
            .Replace("=", "-")
            .Replace("|", "-")
            .Replace("\\", "-")
            .Replace("<", "-")
            .Replace(">", "-")
            .Replace("?", "-")
            .Replace("--", "-")
            .Trim('-');
    }

    private static string SanitizeFieldName(string fieldName)
    {
        // Kubernetes secret keys must be valid environment variable names
        return fieldName.ToLowerInvariant()
            .Replace(" ", "_")
            .Replace("-", "_")
            .Replace(".", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace(":", "_")
            .Replace(";", "_")
            .Replace(",", "_")
            .Replace("(", "_")
            .Replace(")", "_")
            .Replace("[", "_")
            .Replace("]", "_")
            .Replace("{", "_")
            .Replace("}", "_")
            .Replace("'", "_")
            .Replace("\"", "_")
            .Replace("`", "_")
            .Replace("~", "_")
            .Replace("!", "_")
            .Replace("@", "_")
            .Replace("#", "_")
            .Replace("$", "_")
            .Replace("%", "_")
            .Replace("^", "_")
            .Replace("&", "_")
            .Replace("*", "_")
            .Replace("+", "_")
            .Replace("=", "_")
            .Replace("|", "_")
            .Replace("\\", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace("?", "_")
            .Replace("__", "_")
            .Trim('_');
    }
} 