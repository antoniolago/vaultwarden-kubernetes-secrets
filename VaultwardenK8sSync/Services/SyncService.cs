using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public class SyncService : ISyncService
{
    private readonly ILogger<SyncService> _logger;
    private readonly IVaultwardenService _vaultwardenService;
    private readonly IKubernetesService _kubernetesService;
    private readonly SyncSettings _syncConfig;

    public SyncService(
        ILogger<SyncService> logger,
        IVaultwardenService vaultwardenService,
        IKubernetesService kubernetesService,
        SyncSettings syncConfig)
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
            _logger.LogInformation("Starting reconciliation");

            // Get all items from Vaultwarden
            var items = await _vaultwardenService.GetItemsAsync();
            if (!items.Any())
            {
                _logger.LogWarning("No items found in Vaultwarden vault");
                return true;
            }

            // Group items by namespace (supporting multiple namespaces per item)
            var itemsByNamespace = new Dictionary<string, List<Models.VaultwardenItem>>();
            
            foreach (var item in items)
            {
                var namespaces = item.ExtractNamespaces();
                foreach (var namespaceName in namespaces)
                {
                    if (!itemsByNamespace.ContainsKey(namespaceName))
                    {
                        itemsByNamespace[namespaceName] = new List<Models.VaultwardenItem>();
                    }
                    itemsByNamespace[namespaceName].Add(item);
                }
            }

            _logger.LogDebug("Found {Count} items with namespace tags across {NamespaceCount} namespaces", 
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

            _logger.LogInformation("Reconciliation completed: success={Success}", success);
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
            .Where(item => item.ExtractNamespaces().Contains(namespaceName))
            .ToList();

        return await SyncNamespaceAsync(namespaceName, namespaceItems);
    }

    private async Task<bool> SyncNamespaceAsync(string namespaceName, List<Models.VaultwardenItem> items)
    {
        try
        {
            _logger.LogInformation("Reconciling namespace {Namespace} with {Count} source items", namespaceName, items.Count);

            // Group items by secret name to handle multiple items pointing to the same secret
            var itemsBySecretName = GroupItemsBySecretName(items);
            
            var success = true;
            int created = 0, updated = 0, skipped = 0, failed = 0;
            foreach (var (secretName, secretItems) in itemsBySecretName)
            {
                try
                {
                    var outcome = await SyncSecretAsync(namespaceName, secretName, secretItems);
                    switch (outcome)
                    {
                        case ReconcileOutcome.Created:
                            created++; break;
                        case ReconcileOutcome.Updated:
                            updated++; break;
                        case ReconcileOutcome.Skipped:
                            skipped++; break;
                        case ReconcileOutcome.Failed:
                            failed++; success = false; break;
                    }
                    if (outcome == ReconcileOutcome.Failed)
                    {
                        success = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
                    failed++; success = false;
                }
            }

            _logger.LogInformation("Namespace {Namespace} reconciliation result: created={Created}, updated={Updated}, skipped={Skipped}, failed={Failed}",
                namespaceName, created, updated, skipped, failed);

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync namespace {Namespace}", namespaceName);
            return false;
        }
    }

    private async Task<bool> SyncItemAsync(string namespaceName, Models.VaultwardenItem item)
    {
        try
        {
            // Use extracted secret name if available, otherwise use item name
            var extractedSecretName = item.ExtractSecretName();
            var secretName = !string.IsNullOrEmpty(extractedSecretName) 
                ? extractedSecretName 
                : SanitizeSecretName(item.Name);
            
            var secretData = await ExtractSecretDataAsync(item);

            if (_syncConfig.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Would sync item {ItemName} as secret {SecretName} in namespace {Namespace}", 
                    item.Name, secretName, namespaceName);
                return true;
            }

            // Check if there's an existing secret with the old name (based on item name)
            var oldSecretName = SanitizeSecretName(item.Name);
            var oldSecretExists = await _kubernetesService.SecretExistsAsync(namespaceName, oldSecretName);
            var newSecretExists = await _kubernetesService.SecretExistsAsync(namespaceName, secretName);

            bool success = true;

            // If the secret name changed and old secret exists, delete it
            if (oldSecretName != secretName && oldSecretExists)
            {
                if (_syncConfig.DryRun)
                {
                    _logger.LogInformation("[DRY RUN] Would delete old secret {OldSecretName} in namespace {Namespace} due to name change", 
                        oldSecretName, namespaceName);
                }
                else
                {
                    var deleteSuccess = await _kubernetesService.DeleteSecretAsync(namespaceName, oldSecretName);
                    if (deleteSuccess)
                    {
                        _logger.LogInformation("Deleted old secret {OldSecretName} in namespace {Namespace} due to name change", 
                            oldSecretName, namespaceName);
                    }
                    else
                    {
                        success = false;
                    }
                }
            }

            // Create or update the new secret
            if (newSecretExists)
            {
                // Check if the secret data has actually changed
                var existingData = await _kubernetesService.GetSecretDataAsync(namespaceName, secretName);
                if (existingData != null && !HasSecretDataChanged(existingData, secretData))
                {
                    _logger.LogDebug("Secret {SecretName} in namespace {Namespace} is up to date, skipping update", secretName, namespaceName);
                    return true;
                }

                success = await _kubernetesService.UpdateSecretAsync(namespaceName, secretName, secretData);
                if (success)
                {
                    _logger.LogInformation("Updated secret {SecretName} in namespace {Namespace} due to content changes", secretName, namespaceName);
                }
            }
            else
            {
                success = await _kubernetesService.CreateSecretAsync(namespaceName, secretName, secretData);
                if (success)
                {
                    _logger.LogInformation("Created secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
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

    private Task<Dictionary<string, string>> ExtractSecretDataAsync(Models.VaultwardenItem item)
    {
        var data = new Dictionary<string, string>();

        // Get username if available
        var username = GetUsername(item);
        if (!string.IsNullOrEmpty(username))
        {
            // Use custom username key if specified, otherwise use sanitized item name with _username suffix
            var usernameKey = item.ExtractSecretKeyUsername();
            if (string.IsNullOrEmpty(usernameKey))
            {
                usernameKey = $"{SanitizeFieldName(item.Name)}_username";
            }
            data[usernameKey] = FormatMultilineValue(username);
        }

        // Get the password/credential value
        var password = GetLoginPasswordOrSshKey(item);

        // Determine the key to use for the primary value (password/content)
        var passwordKeyResolved = item.ExtractSecretKeyPassword();
        if (string.IsNullOrEmpty(passwordKeyResolved))
        {
            // Fall back to the old secret-key tag for backward compatibility
            passwordKeyResolved = item.ExtractSecretKey();
            if (string.IsNullOrEmpty(passwordKeyResolved))
            {
                passwordKeyResolved = SanitizeFieldName(item.Name);
            }
        }

        if (!string.IsNullOrEmpty(password))
        {
            var formattedPassword = FormatPasswordPossiblyPrivateKey(password);
            data[passwordKeyResolved] = formattedPassword;
        }
        else
        {
            // If no password found, store the item's note content (excluding metadata tags) when present,
            // otherwise fall back to the item name as a placeholder.
            var noteBody = ExtractPureNoteBody(item.Notes);
            data[passwordKeyResolved] = string.IsNullOrWhiteSpace(noteBody) ? item.Name : noteBody;
        }

        // Include custom fields from the item as additional secret keys
        if (item.Fields?.Any() == true)
        {
            foreach (var field in item.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                    continue;
                if (string.IsNullOrEmpty(field.Value))
                    continue;

                var fieldKey = SanitizeFieldName(field.Name);
                // Avoid overwriting previously added keys unless explicit
                if (!data.ContainsKey(fieldKey))
                {
                    data[fieldKey] = FormatMultilineValue(field.Value);
                }
            }
        }

        // Include extra key/values extracted from notes (supports multiline blocks)
        var extraFromNotes = item.ExtractSecretDataFromNotes();
        if (extraFromNotes.Count > 0)
        {
            foreach (var kvp in extraFromNotes)
            {
                var noteKey = SanitizeFieldName(kvp.Key);
                data[noteKey] = FormatMultilineValue(kvp.Value);
            }
        }

        return Task.FromResult(data);
    }

    private static string ExtractPureNoteBody(string notes)
    {
        if (string.IsNullOrEmpty(notes))
            return string.Empty;

        var normalized = notes.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');

        var output = new List<string>();
        bool inSecretBlock = false;
        foreach (var raw in lines)
        {
            var line = raw;
            var trimmed = line.Trim();

            // Track fenced secret blocks so we don't include them by default in the body
            if (trimmed.StartsWith("```secret:", StringComparison.OrdinalIgnoreCase))
            {
                inSecretBlock = true;
                continue;
            }
            if (inSecretBlock && trimmed.StartsWith("```"))
            {
                inSecretBlock = false;
                continue;
            }
            if (inSecretBlock)
            {
                // skip lines inside secret block for default note body
                continue;
            }

            // Skip metadata and kv lines
            if (trimmed.StartsWith($"#{Models.FieldNameConfig.NamespacesFieldName}:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith($"#{Models.FieldNameConfig.SecretNameFieldName}:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith($"#{Models.FieldNameConfig.LegacySecretKeyFieldName}:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith($"#{Models.FieldNameConfig.SecretKeyPasswordFieldName}:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith($"#{Models.FieldNameConfig.SecretKeyUsernameFieldName}:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith($"#{Models.FieldNameConfig.InlineKvTagPrefix}:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            output.Add(line);
        }

        // Trim leading/trailing blank lines
        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[0])) output.RemoveAt(0);
        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[^1])) output.RemoveAt(output.Count - 1);

        return string.Join("\n", output);
    }

    private static string GetPasswordOrSshKey(Models.VaultwardenItem item)
    {
        // First check for regular password
        if (!string.IsNullOrEmpty(item.Password))
            return item.Password;

        // Check for SSH key in custom fields
        if (item.Fields?.Any() == true)
        {
            var sshKeyField = item.Fields.FirstOrDefault(f => 
                f.Name.Equals("ssh_key", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("private_key", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("ssh_private_key", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("key", StringComparison.OrdinalIgnoreCase));
            
            if (sshKeyField != null && !string.IsNullOrEmpty(sshKeyField.Value))
                return sshKeyField.Value;
        }

        // Check for SSH key in notes (common pattern)
        if (!string.IsNullOrEmpty(item.Notes))
        {
            var lines = item.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            bool inSshKeyBlock = false;
            var sshKeyLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Check for SSH key markers
                if (trimmedLine.StartsWith("-----BEGIN") && trimmedLine.Contains("PRIVATE KEY"))
                {
                    inSshKeyBlock = true;
                    sshKeyLines.Add(line);
                }
                else if (trimmedLine.StartsWith("-----END") && trimmedLine.Contains("PRIVATE KEY"))
                {
                    inSshKeyBlock = false;
                    sshKeyLines.Add(line);
                    break;
                }
                else if (inSshKeyBlock)
                {
                    sshKeyLines.Add(line);
                }
            }

            if (sshKeyLines.Any())
            {
                return string.Join("\n", sshKeyLines);
            }
        }

        return string.Empty;
    }

    private static string GetUsername(Models.VaultwardenItem item)
    {
        // First check for login username
        if (item.Login != null && !string.IsNullOrEmpty(item.Login.Username))
            return item.Login.Username;

        // Check for username in custom fields
        if (item.Fields?.Any() == true)
        {
            var usernameField = item.Fields.FirstOrDefault(f => 
                f.Name.Equals("username", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("login", StringComparison.OrdinalIgnoreCase));
            
            if (usernameField != null && !string.IsNullOrEmpty(usernameField.Value))
                return usernameField.Value;
        }

        return string.Empty;
    }

    private static string GetLoginPasswordOrSshKey(Models.VaultwardenItem item)
    {
        // First check for login password
        if (item.Login != null && !string.IsNullOrEmpty(item.Login.Password))
            return item.Login.Password;

        // If no login password, fall back to the general password/SSH key logic
        return GetPasswordOrSshKey(item);
    }

    public async Task<bool> CleanupOrphanedSecretsAsync()
    {
        try
        {
            _logger.LogInformation("Starting cleanup of orphaned secrets...");

            // Get all items from Vaultwarden
            var items = await _vaultwardenService.GetItemsAsync();
            
            // Group items by namespace (supporting multiple namespaces per item)
            var itemsByNamespace = new Dictionary<string, List<Models.VaultwardenItem>>();
            
            foreach (var item in items)
            {
                var namespaces = item.ExtractNamespaces();
                foreach (var namespaceName in namespaces)
                {
                    if (!itemsByNamespace.ContainsKey(namespaceName))
                    {
                        itemsByNamespace[namespaceName] = new List<Models.VaultwardenItem>();
                    }
                    itemsByNamespace[namespaceName].Add(item);
                }
            }

            // Get all namespaces that have secrets (including those no longer in use)
            var allNamespacesWithSecrets = await GetAllNamespacesWithSecretsAsync();
            
            var success = true;
            foreach (var namespaceName in allNamespacesWithSecrets)
            {
                try
                {
                    // Get items for this namespace (empty list if no items currently sync to this namespace)
                    var namespaceItems = itemsByNamespace.GetValueOrDefault(namespaceName, new List<Models.VaultwardenItem>());
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

    private async Task<HashSet<string>> GetAllNamespacesWithSecretsAsync()
    {
        var namespacesWithSecrets = new HashSet<string>();
        
        // Get all namespaces from Kubernetes
        var allNamespaces = await _kubernetesService.GetAllNamespacesAsync();
        
        foreach (var namespaceName in allNamespaces)
        {
            try
            {
                // Only check for secrets that have our management labels
                var managedSecrets = await _kubernetesService.GetManagedSecretNamesAsync(namespaceName);
                
                if (managedSecrets.Any())
                {
                    namespacesWithSecrets.Add(namespaceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check secrets in namespace {Namespace}", namespaceName);
            }
        }
        
        return namespacesWithSecrets;
    }

    private async Task<bool> CleanupOrphanedSecretsInNamespaceAsync(string namespaceName, List<Models.VaultwardenItem> items)
    {
        try
        {
            // Only get secrets that have our management labels
            var managedSecrets = await _kubernetesService.GetManagedSecretNamesAsync(namespaceName);
            
            if (!managedSecrets.Any())
            {
                _logger.LogInformation("No managed secrets found in namespace {Namespace}", namespaceName);
                return true;
            }

            // Calculate expected secret names
            var expectedSecrets = items.Select(item => {
                var extractedSecretName = item.ExtractSecretName();
                return !string.IsNullOrEmpty(extractedSecretName) 
                    ? extractedSecretName 
                    : SanitizeSecretName(item.Name);
            }).ToHashSet();

            // Find orphaned secrets
            var orphanedSecrets = managedSecrets.Where(s => !expectedSecrets.Contains(s)).ToList();

            if (!orphanedSecrets.Any())
            {
                _logger.LogInformation("No orphaned secrets found in namespace {Namespace}", namespaceName);
                return true;
            }

            _logger.LogInformation("Namespace {Namespace} orphan cleanup: {Count} orphaned secret(s)", namespaceName, orphanedSecrets.Count);

            var success = true;
            foreach (var orphanedSecret in orphanedSecrets)
            {
                try
                {
                    if (_syncConfig.DryRun)
                    {
                        _logger.LogInformation("[DRY RUN] Namespace {Namespace}: would delete orphaned secret {SecretName}", 
                            namespaceName, orphanedSecret);
                    }
                    else
                    {
                        var deleteSuccess = await _kubernetesService.DeleteSecretAsync(namespaceName, orphanedSecret);
                        if (deleteSuccess)
                        {
                            _logger.LogInformation("Namespace {Namespace}: deleted orphaned secret {SecretName}", 
                                namespaceName, orphanedSecret);
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

    private static bool HasSecretDataChanged(Dictionary<string, string> existingData, Dictionary<string, string> newData)
    {
        // Check if the number of keys is different
        if (existingData.Count != newData.Count)
            return true;

        // Check if any keys are missing or have different values
        foreach (var kvp in newData)
        {
            if (!existingData.TryGetValue(kvp.Key, out var existingValue) || existingValue != kvp.Value)
                return true;
        }

        // Check if any existing keys are missing in new data
        foreach (var kvp in existingData)
        {
            if (!newData.ContainsKey(kvp.Key))
                return true;
        }

        return false;
    }

    private Dictionary<string, List<Models.VaultwardenItem>> GroupItemsBySecretName(List<Models.VaultwardenItem> items)
    {
        var itemsBySecretName = new Dictionary<string, List<Models.VaultwardenItem>>();

        foreach (var item in items)
        {
            var extractedSecretName = item.ExtractSecretName();
            var secretName = !string.IsNullOrEmpty(extractedSecretName) 
                ? extractedSecretName 
                : SanitizeSecretName(item.Name);

            if (!itemsBySecretName.ContainsKey(secretName))
            {
                itemsBySecretName[secretName] = new List<Models.VaultwardenItem>();
            }
            itemsBySecretName[secretName].Add(item);
        }

        return itemsBySecretName;
    }

    private async Task<ReconcileOutcome> SyncSecretAsync(string namespaceName, string secretName, List<Models.VaultwardenItem> items)
    {
        try
        {
            // Combine all items' data into a single secret
            var combinedSecretData = new Dictionary<string, string>();
            var itemHashes = new List<string>();

            foreach (var item in items)
            {
                var itemData = await ExtractSecretDataAsync(item);
                foreach (var kvp in itemData)
                {
                    // If multiple items have the same key, the last one wins
                    combinedSecretData[kvp.Key] = kvp.Value;
                }
                
                // Calculate hash for this item
                var itemHash = CalculateItemHash(item);
                itemHashes.Add(itemHash);
                
                // Log hash calculation data for debugging
                LogHashCalculationData(item, _logger);
            }

            // Create a combined hash for all items
            var combinedHash = string.Join("|", itemHashes.OrderBy(h => h));
            var hashKey = "vaultwarden-sync-hash";

            if (_syncConfig.DryRun)
            {
                _logger.LogInformation("[DRY RUN] Secret {SecretName} in {Namespace}: ensure up-to-date from {Count} item(s)", 
                    secretName, namespaceName, items.Count);
                return ReconcileOutcome.Skipped;
            }

            // Check if there's an existing secret with the old name (based on item name)
            var oldSecretName = SanitizeSecretName(items.First().Name);
            var oldSecretExists = await _kubernetesService.SecretExistsAsync(namespaceName, oldSecretName);
            var newSecretExists = await _kubernetesService.SecretExistsAsync(namespaceName, secretName);

            bool success = true;
            bool didCreate = false;
            bool didUpdate = false;

            // If the secret name changed and old secret exists, delete it
            if (oldSecretName != secretName && oldSecretExists)
            {
                if (_syncConfig.DryRun)
                {
                    _logger.LogInformation("[DRY RUN] Would delete old secret {OldSecretName} in namespace {Namespace} due to name change", 
                        oldSecretName, namespaceName);
                }
                else
                {
                    var deleteSuccess = await _kubernetesService.DeleteSecretAsync(namespaceName, oldSecretName);
                    if (deleteSuccess)
                    {
                        _logger.LogInformation("Deleted old secret {OldSecretName} in namespace {Namespace} due to name change", 
                            oldSecretName, namespaceName);
                    }
                    else
                    {
                        success = false;
                    }
                }
            }

            // Create or update the new secret
            if (newSecretExists)
            {
                // Check if the secret data or item hashes have changed
                var existingData = await _kubernetesService.GetSecretDataAsync(namespaceName, secretName);
                var hasDataChanged = existingData == null || HasSecretDataChanged(existingData, combinedSecretData);

                bool hasHashChanged;
                if (existingData != null && existingData.TryGetValue(hashKey, out var oldHashValue))
                {
                    hasHashChanged = oldHashValue != combinedHash;
                }
                else
                {
                    hasHashChanged = true;
                }

                // Log detailed information about what changed
                if (hasHashChanged)
                {
                    var oldHash = existingData?.GetValueOrDefault(hashKey, "none");
                    _logger.LogInformation("Hash changed for secret {SecretName} in namespace {Namespace}: old={OldHash}, new={NewHash}", 
                        secretName, namespaceName, oldHash, combinedHash);
                }

                if (!hasDataChanged && !hasHashChanged)
                {
                    _logger.LogInformation("Reconciled secret {SecretName} in namespace {Namespace}: Skipped (UpToDate)", secretName, namespaceName);
                    return ReconcileOutcome.Skipped;
                }

                // Add the hash to the secret data for future comparisons
                combinedSecretData[hashKey] = combinedHash;

                success = await _kubernetesService.UpdateSecretAsync(namespaceName, secretName, combinedSecretData);
                if (success)
                {
                    didUpdate = true;
                    var changeReason = hasDataChanged && hasHashChanged ? "content+metadata" :
                                      hasDataChanged ? "content" : "metadata";
                    _logger.LogInformation("Reconciled secret {SecretName} in namespace {Namespace}: Updated ({Reason})", 
                        secretName, namespaceName, changeReason);
                }
            }
            else
            {
                // Add the hash to the secret data for future comparisons
                combinedSecretData[hashKey] = combinedHash;

                success = await _kubernetesService.CreateSecretAsync(namespaceName, secretName, combinedSecretData);
                if (success)
                {
                    didCreate = true;
                    _logger.LogInformation("Reconciled secret {SecretName} in namespace {Namespace}: Created", secretName, namespaceName);
                }
            }

            if (!success) return ReconcileOutcome.Failed;
            if (didCreate) return ReconcileOutcome.Created;
            if (didUpdate) return ReconcileOutcome.Updated;
            return ReconcileOutcome.Skipped;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
            return ReconcileOutcome.Failed;
        }
    }

    private static string CalculateItemHash(Models.VaultwardenItem item)
    {
        // Create a hash based on all relevant fields that could affect the secret
        var hashData = new List<string>
        {
            item.Name ?? "",
            ExtractPureNoteBody(item.Notes ?? string.Empty),
            item.Password ?? "",
            item.ExtractSecretName() ?? "",
            item.ExtractSecretKey() ?? "",
            item.ExtractSecretKeyPassword() ?? "",
            item.ExtractSecretKeyUsername() ?? "",
            string.Join(",", item.ExtractNamespaces().OrderBy(ns => ns))
        };

        // Add login information if available
        if (item.Login != null)
        {
            hashData.Add(item.Login.Username ?? "");
            hashData.Add(item.Login.Password ?? "");
            if (item.Login.Uris?.Any() == true)
            {
                var uris = string.Join(",", item.Login.Uris.Select(u => u.Uri).OrderBy(u => u));
                hashData.Add(uris);
            }
        }

        // Add custom fields if available (including tags)
        if (item.Fields?.Any() == true)
        {
            var sortedFields = item.Fields
                .OrderBy(f => f.Name)
                .Select(f => $"{f.Name}:{f.Value}")
                .ToList();
            hashData.AddRange(sortedFields);
        }

        // Add revision date to catch any changes to the item
        hashData.Add(item.RevisionDate.ToString("O"));

        // Create a hash of all the data
        var combinedData = string.Join("|", hashData);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combinedData));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Debug method to log the hash calculation data for troubleshooting
    /// </summary>
    private static void LogHashCalculationData(Models.VaultwardenItem item, ILogger logger)
    {
        var hashData = new List<string>
        {
            $"Name: {item.Name}",
            $"NoteBody: {ExtractPureNoteBody(item.Notes ?? string.Empty)}",
            $"Password: {(string.IsNullOrEmpty(item.Password) ? "<empty>" : "<set>")}",
            $"ExtractSecretName: {item.ExtractSecretName()}",
            $"ExtractSecretKey: {item.ExtractSecretKey()}",
            $"ExtractSecretKeyPassword: {item.ExtractSecretKeyPassword()}",
            $"ExtractSecretKeyUsername: {item.ExtractSecretKeyUsername()}",
            $"Namespaces: {string.Join(",", item.ExtractNamespaces().OrderBy(ns => ns))}",
            $"RevisionDate: {item.RevisionDate:O}"
        };

        if (item.Login != null)
        {
            hashData.Add($"Login.Username: {item.Login.Username}");
            hashData.Add($"Login.Password: {item.Login.Password}");
            if (item.Login.Uris?.Any() == true)
            {
                var uris = string.Join(",", item.Login.Uris.Select(u => u.Uri).OrderBy(u => u));
                hashData.Add($"Login.Uris: {uris}");
            }
        }

        if (item.Fields?.Any() == true)
        {
            var sortedFields = item.Fields
                .OrderBy(f => f.Name)
                .Select(f => $"Field.{f.Name}: {f.Value}")
                .ToList();
            hashData.AddRange(sortedFields);
        }

        logger.LogTrace("Hash base data for item {ItemName}: {HashData}", 
            item.Name, string.Join(" | ", hashData));
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

    /// <summary>
    /// Formats a value to ensure proper handling of multiline content.
    /// This method ensures that multiline values (like SSH keys, certificates, etc.)
    /// are preserved correctly when stored in Kubernetes secrets.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <returns>The formatted value with proper multiline handling</returns>
    private static string FormatMultilineValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Normalize line endings to ensure consistent handling across platforms
        var normalizedValue = value.Replace("\r\n", "\n").Replace("\r", "\n");
        
        // If the value contains newlines, log it for debugging purposes
        if (normalizedValue.Contains('\n'))
        {
            // This is a multiline value - ensure it's properly formatted
            // The current implementation already handles this correctly by preserving all characters
            // including newlines when converting to bytes for Kubernetes storage
            var lineCount = normalizedValue.Split('\n').Length;
            // Note: We can't use _logger here since this is a static method
            // The multiline handling is working correctly - this is just for reference
            return normalizedValue;
        }
        
        return normalizedValue;
    }

    /// <summary>
    /// If the provided value appears to be a PEM private key, normalize it to canonical PEM format:
    /// - Ensure header/footer are on separate lines
    /// - Remove extraneous whitespace inside the base64 body
    /// - Wrap the base64 body at 64 characters per line
    /// Otherwise, return the value with normalized line endings.
    /// </summary>
    private static string FormatPasswordPossiblyPrivateKey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalized = value.Replace("\r\n", "\n").Replace("\r", "\n");

        // Detect PEM private key markers
        var headerMatch = Regex.Match(normalized, "-+BEGIN ([A-Z ]*PRIVATE KEY)-+", RegexOptions.CultureInvariant);
        var footerMatch = Regex.Match(normalized, "-+END ([A-Z ]*PRIVATE KEY)-+", RegexOptions.CultureInvariant);

        if (!headerMatch.Success || !footerMatch.Success)
        {
            // Not a PEM-looking value; just normalize newlines
            return FormatMultilineValue(normalized);
        }

        var keyTypeHeader = headerMatch.Groups[1].Value.Trim();

        // If header/footer types differ, fall back to normalized value
        var keyTypeFooter = footerMatch.Groups[1].Value.Trim();
        if (!string.Equals(keyTypeHeader, keyTypeFooter, StringComparison.Ordinal))
        {
            return FormatMultilineValue(normalized);
        }

        var pemHeader = $"-----BEGIN {keyTypeHeader}-----";
        var pemFooter = $"-----END {keyTypeHeader}-----";

        // Extract the raw base64 body between header and footer
        var startIdx = headerMatch.Index + headerMatch.Length;
        var endIdx = footerMatch.Index;
        if (endIdx <= startIdx)
        {
            return FormatMultilineValue(normalized);
        }

        var bodyRaw = normalized.Substring(startIdx, endIdx - startIdx);
        // Convert escaped newlines when users paste with literal \n
        bodyRaw = bodyRaw.Replace("\\n", "\n");
        // Remove all whitespace to re-wrap cleanly
        var base64Body = Regex.Replace(bodyRaw, "\\s+", string.Empty, RegexOptions.CultureInvariant);

        // Wrap to 64 characters per line
        var wrapped = WrapAtColumn(base64Body, 64);

        var rebuilt = string.Join("\n", new[] { pemHeader, wrapped, pemFooter });
        return rebuilt;
    }

    private static string WrapAtColumn(string text, int column)
    {
        if (string.IsNullOrEmpty(text) || column <= 0)
            return text ?? string.Empty;

        var chunks = new List<string>(capacity: Math.Max(1, text.Length / Math.Max(1, column)));
        for (int i = 0; i < text.Length; i += column)
        {
            var len = Math.Min(column, text.Length - i);
            chunks.Add(text.Substring(i, len));
        }
        return string.Join("\n", chunks);
    }
} 

internal enum ReconcileOutcome
{
    Created,
    Updated,
    Skipped,
    Failed
}