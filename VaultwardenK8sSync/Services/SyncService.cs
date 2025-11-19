using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Infrastructure;

namespace VaultwardenK8sSync.Services;

public class SyncService : ISyncService
{
    private readonly ILogger<SyncService> _logger;
    private readonly IVaultwardenService _vaultwardenService;
    private readonly IKubernetesService _kubernetesService;
    private readonly IMetricsService _metricsService;
    private readonly IDatabaseLoggerService _dbLogger;
    private readonly SyncSettings _syncConfig;
    private string? _lastItemsHash;
    private string? _currentItemsHash;
    private readonly Dictionary<string, DateTime> _secretExistsCache = new();
    private int _syncCount;

    public SyncService(
        ILogger<SyncService> logger,
        IVaultwardenService vaultwardenService,
        IKubernetesService kubernetesService,
        IMetricsService metricsService,
        IDatabaseLoggerService dbLogger,
        SyncSettings syncConfig)
    {
        _logger = logger;
        _vaultwardenService = vaultwardenService;
        _kubernetesService = kubernetesService;
        _metricsService = metricsService;
        _dbLogger = dbLogger;
        _syncConfig = syncConfig;
    }

    public async Task<SyncSummary> SyncAsync()
    {
        return await SyncAsync(null);
    }

    public async Task<SyncSummary> SyncAsync(ISyncProgressReporter? progressReporter)
    {
        // Prevent concurrent syncs with global file-based lock (works across all processes)
        using var syncLock = new GlobalSyncLock(_logger);
        
        if (!await syncLock.TryAcquireAsync())
        {
            _logger.LogWarning("⚠️  Sync already in progress (in another process or thread) - rejecting concurrent sync attempt");
            return new SyncSummary
            {
                SyncNumber = _syncCount,
                OverallSuccess = false,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Errors = new List<string> { "Sync already in progress" }
            };
        }
        
        var progress = progressReporter ?? new NullProgressReporter();
        var syncStartTime = DateTime.UtcNow;
    
        var summary = new SyncSummary
        {
            StartTime = syncStartTime,
            SyncNumber = GetSyncCount()
        };
        
        // Start database logging
        long syncLogId = 0;
        
        try
        {
            progress.Start("Starting sync operation...");
            progress.SetPhase("Authenticating and fetching items");
            
            _logger.LogDebug("Starting reconciliation (sync #{SyncCount})", summary.SyncNumber);

            // Get all items from Vaultwarden
            var items = await _vaultwardenService.GetItemsAsync();
            summary.TotalItemsFromVaultwarden = items.Count;
            
            // Cache items in database for API to use (no auth needed in API)
            await _dbLogger.CacheVaultwardenItemsAsync(items);
            
            // Start sync log in database
            syncLogId = await _dbLogger.StartSyncLogAsync("Full Sync", items.Count);
            
            // Record items watched
            _metricsService.RecordItemsWatched(items.Count);
            
            if (!items.Any())
            {
                summary.AddWarning("No items found in Vaultwarden vault");
                summary.EndTime = DateTime.UtcNow;
                await _dbLogger.CompleteSyncLogAsync(syncLogId, "Success", "No items found in vault");
                progress.Complete("No items found in vault");
                return summary;
            }

            // Quick change detection - avoid expensive processing if nothing changed
            // BUT: We still need to verify secrets exist (they might have been deleted externally)
            progress.SetPhase("Analyzing changes");
            _currentItemsHash = CalculateQuickItemsHash(items);
            var shouldSkipReconciliation = _lastItemsHash == _currentItemsHash && _lastItemsHash != null;
            
            if (shouldSkipReconciliation)
            {
                _logger.LogInformation("No changes detected in Vaultwarden items hash - but will verify all secrets exist");
                // Don't skip - we still need to verify secrets exist even when hash unchanged
                // This ensures deleted secrets are recreated
            }
            
            // Indicate whether the overall set of items changed since last successful sync.
            // If the quick-hash indicates no change we still perform existence verification for secrets,
            // but the sync summary should reflect that there were no item changes.
            summary.HasChanges = !shouldSkipReconciliation;

            _logger.LogDebug("Proceeding with reconciliation (hash changed: {HashChanged})", !shouldSkipReconciliation);

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

            summary.TotalNamespaces = itemsByNamespace.Count;
            _logger.LogDebug("Found {Count} items with namespace tags across {NamespaceCount} namespaces", 
                itemsByNamespace.Values.Sum(x => x.Count), itemsByNamespace.Count);

            // Calculate total secrets for progress tracking
            var totalSecrets = 0;
            foreach (var (namespaceName, namespaceItems) in itemsByNamespace)
            {
                var itemsBySecretName = GroupItemsBySecretName(namespaceItems);
                totalSecrets += itemsBySecretName.Count;
            }
            
            progress.SetPhase($"Processing {totalSecrets} secrets across {itemsByNamespace.Count} namespaces");
            // progress.Start($"Processing {totalSecrets} secrets", totalSecrets);
            
            // Add all items to progress display initially
            foreach (var (namespaceName, namespaceItems) in itemsByNamespace)
            {
                var itemsBySecretName = GroupItemsBySecretName(namespaceItems);
                foreach (var (secretName, secretItems) in itemsBySecretName)
                {
                    var key = $"{namespaceName}/{secretName}";
                    var displayName = $"{namespaceName}/{secretName}";
                    progress.AddItem(key, displayName, "Pending");
                }
            }

            // Sync each namespace
            foreach (var (namespaceName, namespaceItems) in itemsByNamespace)
            {
                try
                {
                    var namespaceSummary = await SyncNamespaceAsync(namespaceName, namespaceItems, summary, progress, syncLogId);
                    summary.AddNamespace(namespaceSummary);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync namespace {Namespace}", namespaceName);
                    var failedNamespaceSummary = new NamespaceSummary
                    {
                        Name = namespaceName,
                        Success = false,
                        SourceItems = namespaceItems.Count
                    };
                    failedNamespaceSummary.Errors.Add($"Exception: {ex.Message}");
                    summary.AddNamespace(failedNamespaceSummary);
                    summary.AddError($"Namespace {namespaceName}: {ex.Message}");
                    
                    // Update any pending items in this namespace as failed
                    var itemsBySecretName = GroupItemsBySecretName(namespaceItems);
                    foreach (var (secretName, _) in itemsBySecretName)
                    {
                        var key = $"{namespaceName}/{secretName}";
                        progress.UpdateItem(key, "Failed", ex.Message, SyncItemOutcome.Failed);
                    }
                }
            }

            // Cleanup orphaned secrets if enabled (reuse cached items)
            if (_syncConfig.DeleteOrphans)
            {
                progress.SetPhase("Cleaning up orphaned secrets");
                try
                {
                    var orphanSummary = await CleanupOrphanedSecretsAsync(items);
                    summary.OrphanCleanup = orphanSummary;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cleanup orphaned secrets, but sync completed");
                    summary.AddError($"Orphan cleanup failed: {ex.Message}");
                    summary.OrphanCleanup = new OrphanCleanupSummary
                    {
                        Enabled = true,
                        Success = false
                    };
                }
            }
            else
            {
                summary.OrphanCleanup = new OrphanCleanupSummary { Enabled = false };
            }

            // Cleanup stale secret states (database entries for secrets that no longer exist in Vaultwarden)
            try
            {
                var staleCount = await _dbLogger.CleanupStaleSecretStatesAsync(items);
                if (staleCount > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} stale secret state entries", staleCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup stale secret states, but sync completed");
            }

            summary.EndTime = DateTime.UtcNow;

            // Calculate final status and process summary
            var hasProcessedItems = summary.TotalSecretsProcessed > 0;
            var allSkipped = hasProcessedItems && summary.TotalSecretsSkipped == summary.TotalSecretsProcessed;
            var noChanges = !summary.HasChanges || allSkipped;

            // Update database with final sync results
            var deletedCount = summary.OrphanCleanup?.TotalOrphansDeleted ?? 0;
            await _dbLogger.UpdateSyncProgressAsync(
                syncLogId,
                summary.TotalSecretsProcessed,
                summary.TotalSecretsCreated,
                summary.TotalSecretsUpdated,
                summary.TotalSecretsSkipped,
                summary.TotalSecretsFailed,
                deletedCount
            );

            // Determine final status:
            // - "Failed" if any failures occurred
            // - "UP-TO-DATE" if either no changes detected or all items skipped
            // - "Success" if items processed successfully with changes
            var status = !summary.OverallSuccess ? "Failed" :
                        noChanges ? "UP-TO-DATE" : "Success";

            var errorMsg = summary.Errors.Any() ? string.Join("; ", summary.Errors) : null;
            await _dbLogger.CompleteSyncLogAsync(syncLogId, status, errorMsg);            // Update secret states in database
            foreach (var ns in summary.Namespaces ?? new List<NamespaceSummary>())
            {
                // This will be populated when we process each secret
            }
            
            // Record sync metrics
            var syncDuration = (summary.EndTime - syncStartTime).TotalSeconds;
            _metricsService.RecordSyncDuration(syncDuration, summary.OverallSuccess);
            
            // Record secrets synced
            _metricsService.RecordSecretsSynced(summary.TotalSecretsCreated, "created");
            _metricsService.RecordSecretsSynced(summary.TotalSecretsUpdated, "updated");
            if (summary.OrphanCleanup != null)
            {
                _metricsService.RecordSecretsSynced(summary.OrphanCleanup.TotalOrphansDeleted, "deleted");
            }
            
            // Update last successful sync timestamp
            if (summary.OverallSuccess)
            {
                _metricsService.SetLastSuccessfulSync();
            }
            
            // Only update the hash if the sync completed successfully (no failed items)
            if (summary.OverallSuccess)
            {
                _lastItemsHash = _currentItemsHash;
                _logger.LogDebug("Updated items hash to {Hash} after successful sync", 
                    _currentItemsHash?.Substring(0, Math.Min(8, _currentItemsHash?.Length ?? 0)));
            }
            else
            {
                _logger.LogDebug("Not updating items hash due to sync failures - will retry on next run");
            }
            
            _logger.LogDebug("Reconciliation completed: success={Success}", summary.OverallSuccess);
            progress.Complete();
            return summary;
        }
        catch (Exception ex)
        {
            // Re-throw authentication failures immediately - these are critical
            if (ex is InvalidOperationException)
            {
                _logger.LogError(ex, "Authentication failed during sync - re-throwing");
                throw;
            }
            
            _logger.LogError(ex, "Failed to perform sync");
            summary.AddError($"Sync failed: {ex.Message}");
            summary.EndTime = DateTime.UtcNow;
            
            // Complete database log with error
            await _dbLogger.CompleteSyncLogAsync(syncLogId, "Failed", ex.Message);
            
            // Record sync failure
            var syncDuration = (summary.EndTime - syncStartTime).TotalSeconds;
            _metricsService.RecordSyncDuration(syncDuration, false);
            _metricsService.RecordSyncError(ex.GetType().Name);
            
            progress.Complete($"Sync failed: {ex.Message}");
            return summary;
        }
        // Global sync lock is automatically released by 'using' statement
    }

    public async Task<bool> SyncNamespaceAsync(string namespaceName)
    {
        var items = await _vaultwardenService.GetItemsAsync();
        var namespaceItems = items
            .Where(item => item.ExtractNamespaces().Contains(namespaceName))
            .ToList();

        // Create a temporary summary for the single namespace sync
        var tempSummary = new SyncSummary { SyncNumber = GetSyncCount() };
        var namespaceSummary = await SyncNamespaceAsync(namespaceName, namespaceItems, tempSummary);
        return namespaceSummary.Success;
    }

    /// <summary>
    /// Resets the items hash to force a full sync on the next run.
    /// Useful when there are failures that need to be retried.
    /// </summary>
    public void ResetItemsHash()
    {
        _lastItemsHash = null;
        _logger.LogDebug("Reset items hash - next sync will process all items");
    }

    private async Task<NamespaceSummary> SyncNamespaceAsync(string namespaceName, List<Models.VaultwardenItem> items, SyncSummary parentSummary, ISyncProgressReporter? progress = null, long syncLogId = 0)
    {
        var namespaceSummary = new NamespaceSummary
        {
            Name = namespaceName,
            SourceItems = items.Count
        };
        
        try
        {
            _logger.LogDebug("Reconciling namespace {Namespace} with {Count} source items", namespaceName, items.Count);

            // Group items by secret name to handle multiple items pointing to the same secret
            var itemsBySecretName = GroupItemsBySecretName(items);
            
            _logger.LogInformation("SyncNamespaceAsync: Namespace {Namespace} has {SecretCount} secret(s) to process: {SecretNames}", 
                namespaceName, itemsBySecretName.Count, string.Join(", ", itemsBySecretName.Keys));
            
            foreach (var (secretName, secretItems) in itemsBySecretName)
            {
                var key = $"{namespaceName}/{secretName}";
                
                _logger.LogInformation("SyncNamespaceAsync: Processing secret {SecretName} in namespace {Namespace} from {Count} item(s)", 
                    secretName, namespaceName, secretItems.Count);
                
                try
                {
                    // progress?.UpdateItem(key, "Processing...", $"Items: {secretItems.Count}");
                    
                    var secretSummary = await SyncSecretAsync(namespaceName, secretName, secretItems, syncLogId);
                    namespaceSummary.AddSecret(secretSummary);
                    
                    _logger.LogInformation("SyncNamespaceAsync: Secret {SecretName} in namespace {Namespace} completed with outcome: {Outcome}", 
                        secretName, namespaceName, secretSummary.Outcome);
                    
                    if (secretSummary.Outcome == ReconcileOutcome.Failed)
                    {
                        _logger.LogError("SyncNamespaceAsync: Secret {SecretName} in namespace {Namespace} failed: {Error}", 
                            secretName, namespaceName, secretSummary.Error);
                    }
                    else if (secretSummary.Outcome == ReconcileOutcome.Created || secretSummary.Outcome == ReconcileOutcome.Updated)
                    {
                        // Update database state for created/updated items
                        var itemForState = secretItems.FirstOrDefault();
                        if (itemForState != null)
                        {
                            await _dbLogger.UpsertSecretStateAsync(
                                namespaceName,
                                secretName,
                                itemForState.Id,
                                itemForState.Name,
                                SecretStatusConstants.Active,
                                1, // Using 1 as we don't have the combined secret data count here
                                null
                            );

                            // Update sync progress to count skipped items
                            await _dbLogger.UpdateSyncProgressAsync(
                                syncLogId,
                                1, // processed count
                                0, // created
                                0, // updated
                                1, // skipped
                                0, // failed
                                0  // deleted
                            );
                        }
                    }
                    
                    // Update progress display with final result
                    if (progress != null)
                    {
                        var outcome = secretSummary.Outcome switch
                        {
                            ReconcileOutcome.Created => SyncItemOutcome.Created,
                            ReconcileOutcome.Updated => SyncItemOutcome.Updated,
                            ReconcileOutcome.Skipped => SyncItemOutcome.Skipped,
                            ReconcileOutcome.Failed => SyncItemOutcome.Failed,
                            _ => SyncItemOutcome.Failed
                        };
                        
                        var details = secretSummary.Outcome == ReconcileOutcome.Failed 
                            ? secretSummary.Error 
                            : secretSummary.ChangeReason;
                        
                        // progress.UpdateItem(key, secretSummary.GetStatusText(), details, outcome);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SyncNamespaceAsync: Exception syncing secret {SecretName} in namespace {Namespace}. Exception: {ExceptionType}, Message: {Message}", 
                        secretName, namespaceName, ex.GetType().Name, ex.Message);
                    var failedSecret = new SecretSummary
                    {
                        Name = secretName,
                        Outcome = ReconcileOutcome.Failed,
                        SourceItemCount = secretItems.Count,
                        Error = ex.Message
                    };
                    namespaceSummary.AddSecret(failedSecret);
                    namespaceSummary.Errors.Add($"Secret {secretName}: {ex.Message}");
                    
                    // Log failed secret state to database
                    var firstItem = secretItems.FirstOrDefault();
                    if (firstItem != null)
                    {
                        await _dbLogger.UpsertSecretStateAsync(
                            namespaceName,
                            secretName,
                            firstItem.Id,
                            firstItem.Name,
                            SecretStatusConstants.Failed,
                            0,
                            ex.Message
                        );
                    }
                    
                    // progress?.UpdateItem(key, "FAILED", ex.Message, SyncItemOutcome.Failed);
                }
            }

            // Log final results for namespace
            _logger.LogDebug("Namespace {Namespace} reconciliation result: created={Created}, updated={Updated}, skipped={Skipped}, failed={Failed}",
                namespaceName, namespaceSummary.Created, namespaceSummary.Updated, namespaceSummary.Skipped, namespaceSummary.Failed);

            // Update database with namespace totals
            if (syncLogId > 0)
            {
                await _dbLogger.UpdateSyncProgressAsync(
                    syncLogId,
                    namespaceSummary.Created + namespaceSummary.Updated + namespaceSummary.Skipped + namespaceSummary.Failed, // processed
                    namespaceSummary.Created,
                    namespaceSummary.Updated,
                    namespaceSummary.Skipped,
                    namespaceSummary.Failed,
                    0 // deleted is handled by orphan cleanup
                );
            }

            return namespaceSummary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync namespace {Namespace}", namespaceName);
            namespaceSummary.Success = false;
            namespaceSummary.Errors.Add($"Namespace sync failed: {ex.Message}");
            return namespaceSummary;
        }
    }

    private async Task<bool> SyncItemAsync(string namespaceName, Models.VaultwardenItem item)
    {
        try
        {
            // Use extracted secret name if available, otherwise use item name
            var extractedSecretName = item.ExtractSecretName();
            var secretName = !string.IsNullOrEmpty(extractedSecretName) 
                ? SanitizeSecretName(extractedSecretName) 
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

                var updateResult = await _kubernetesService.UpdateSecretAsync(namespaceName, secretName, secretData);
                success = updateResult.Success;
                if (success)
                {
                    _logger.LogInformation("Updated secret {SecretName} in namespace {Namespace} due to content changes", secretName, namespaceName);
                }
            }
            else
            {
                var createResult = await _kubernetesService.CreateSecretAsync(namespaceName, secretName, secretData);
                success = createResult.Success;
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

    private async Task<Dictionary<string, string>> ExtractSecretDataAsync(Models.VaultwardenItem item)
    {
        var data = new Dictionary<string, string>();

        // Hydrate SSH Key payload for SSH items if missing from list output
        var isSshKeyItem = item.Type == 5;
        var hasMissingSshKeyData = item.SshKey == null || 
            string.IsNullOrWhiteSpace(item.SshKey.PrivateKey) ||
            string.IsNullOrWhiteSpace(item.SshKey.PublicKey) ||
            string.IsNullOrWhiteSpace(item.SshKey.Fingerprint);
        var needsSshKeyHydration = isSshKeyItem && hasMissingSshKeyData;
        
        if (needsSshKeyHydration)
        {
            try
            {
                var full = await _vaultwardenService.GetItemAsync(item.Id);
                if (full?.SshKey != null)
                {
                    item.SshKey = full.SshKey;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to hydrate SSH key payload for item {ItemId}", item.Id);
            }
        }

        // Get username if available
        var username = GetUsername(item);
        if (!string.IsNullOrEmpty(username))
        {
            // Use custom username key if specified, otherwise use sanitized secret name with _username suffix
            var usernameKey = item.ExtractSecretKeyUsername();
            if (string.IsNullOrEmpty(usernameKey))
            {
                // Use the sanitized secret name (which preserves hyphens) instead of item name
                var extractedName = item.ExtractSecretName();
                var secretName = !string.IsNullOrEmpty(extractedName) 
                    ? SanitizeSecretName(extractedName) 
                    : SanitizeSecretName(item.Name ?? string.Empty);
                usernameKey = $"{SanitizeFieldName(secretName)}-username";
            }
            data[usernameKey] = FormatMultilineValue(username);
        }

        // Get the password/credential value (login password or SSH private key if SSH item)
        var password = GetLoginPasswordOrSshKey(item);

        // Determine the key to use for the primary value (password/content)
        var passwordKeyResolved = item.ExtractSecretKeyPassword();
        if (string.IsNullOrEmpty(passwordKeyResolved))
        {
            // Use the sanitized item name for the field key (preserves case and uses underscores)
            var extractedSecName = item.ExtractSecretName();
            var itemName = !string.IsNullOrEmpty(extractedSecName) 
                ? extractedSecName 
                : (item.Name ?? string.Empty);
            passwordKeyResolved = SanitizeFieldName(itemName);
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
            data[passwordKeyResolved] = string.IsNullOrWhiteSpace(noteBody) 
                ? (item.Name ?? string.Empty) 
                : FormatMultilineValue(noteBody);
        }

        // Include SSH-specific extras if present
        if (item.SshKey != null)
        {
            // Use the sanitized secret name (which preserves hyphens) instead of item name
            var sshExtractedName = item.ExtractSecretName();
            var secretName = !string.IsNullOrEmpty(sshExtractedName) 
                ? SanitizeSecretName(sshExtractedName) 
                : SanitizeSecretName(item.Name ?? string.Empty);
                
            if (!string.IsNullOrWhiteSpace(item.SshKey.PublicKey))
            {
                var pubKeyKey = $"{SanitizeFieldName(secretName)}-public-key";
                if (!data.ContainsKey(pubKeyKey))
                {
                    data[pubKeyKey] = FormatMultilineValue(item.SshKey.PublicKey!);
                }
            }
            if (!string.IsNullOrWhiteSpace(item.SshKey.Fingerprint))
            {
                var fpKey = $"{SanitizeFieldName(secretName)}-fingerprint";
                if (!data.ContainsKey(fpKey))
                {
                    data[fpKey] = item.SshKey.Fingerprint!;
                }
            }
        }


        // Get the list of fields that should be ignored for this item
        var ignoredFields = item.ExtractIgnoredFields();

        if (item.Fields?.Any() == true)
        {
            foreach (var field in item.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                    continue;
                if (string.IsNullOrEmpty(field.Value))
                    continue;
                
                if (IsMetadataField(field.Name))
                    continue;

                // Skip this field if it's in the ignore list
                if (ignoredFields.Contains(field.Name))
                    continue;

                _logger.LogDebug("Processing field: original name='{OriginalName}', sanitized name='{SanitizedName}'", field.Name, SanitizeFieldName(field.Name));
                var fieldKey = SanitizeFieldName(field.Name);

                if (!data.ContainsKey(fieldKey))
                {
                    // Log before conversion for debugging
                    var hasEscapesBefore = field.Value.Contains("\\n") || field.Value.Contains("\\r") || field.Value.Contains("\\t");
                    var hasNewlinesBefore = field.Value.Contains('\n') || field.Value.Contains('\r');
                    var formattedValue = FormatMultilineValue(field.Value);
                    var hasNewlinesAfter = formattedValue.Contains('\n') || formattedValue.Contains('\r');
                    
                    if (hasEscapesBefore && !hasNewlinesBefore)
                    {
                        _logger.LogDebug("Field {FieldName}: Converting escape sequences (hasEscapes={HasEscapes}, hasNewlinesAfter={HasNewlinesAfter})", 
                            field.Name, hasEscapesBefore, hasNewlinesAfter);
                    }
                    
                    data[fieldKey] = formattedValue;
                }
            }
        }



        return data;
    }

    private static string ExtractPureNoteBody(string notes)
    {
        if (string.IsNullOrEmpty(notes))
            return string.Empty;

        var normalized = notes.Replace("\r\n", "\n").Replace("\r", "\n");
        
        // Trim leading/trailing blank lines
        var lines = normalized.Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);

        return string.Join("\n", lines);
    }

    private static string GetPasswordOrSshKey(Models.VaultwardenItem item)
    {
        // First check for regular password
        if (!string.IsNullOrEmpty(item.Password))
            return item.Password;

        // Prefer SSH Key payload if present on item
        if (item.SshKey != null && !string.IsNullOrWhiteSpace(item.SshKey.PrivateKey))
        {
            return item.SshKey.PrivateKey!;
        }

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
        // Get fresh items if called directly
        var items = await _vaultwardenService.GetItemsAsync();
        var summary = await CleanupOrphanedSecretsAsync(items);
        return summary.Success;
    }

    private async Task<OrphanCleanupSummary> CleanupOrphanedSecretsAsync(List<Models.VaultwardenItem> items)
    {
        var summary = new OrphanCleanupSummary { Enabled = true };
        
        try
        {
            _logger.LogDebug("Starting cleanup of orphaned secrets...");
            
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
            
            foreach (var namespaceName in allNamespacesWithSecrets)
            {
                try
                {
                    // Get items for this namespace (empty list if no items currently sync to this namespace)
                    var namespaceItems = itemsByNamespace.GetValueOrDefault(namespaceName, new List<Models.VaultwardenItem>());
                    var namespaceSummary = await CleanupOrphanedSecretsInNamespaceAsync(namespaceName, namespaceItems);
                    if (namespaceSummary != null)
                    {
                        summary.Namespaces.Add(namespaceSummary);
                        summary.TotalOrphansFound += namespaceSummary.OrphansFound;
                        summary.TotalOrphansDeleted += namespaceSummary.OrphansDeleted;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cleanup orphaned secrets in namespace {Namespace}", namespaceName);
                    summary.Success = false;
                }
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup orphaned secrets");
            summary.Success = false;
            return summary;
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

    private async Task<OrphanNamespaceSummary?> CleanupOrphanedSecretsInNamespaceAsync(string namespaceName, List<Models.VaultwardenItem> items)
    {
        try
        {
            // Only get secrets that have our management labels
            var managedSecrets = await _kubernetesService.GetManagedSecretNamesAsync(namespaceName);
            
            if (!managedSecrets.Any())
            {
                _logger.LogDebug("No managed secrets found in namespace {Namespace}", namespaceName);
                return null;
            }

            // Calculate expected secret names
            var expectedSecrets = items.Select(item => {
                var extractedSecretName = item.ExtractSecretName();
                return !string.IsNullOrEmpty(extractedSecretName) 
                    ? SanitizeSecretName(extractedSecretName) 
                    : SanitizeSecretName(item.Name);
            }).ToHashSet();

            // Find orphaned secrets (exclude auth token secret from cleanup)
            var orphanedSecrets = managedSecrets
                .Where(s => !expectedSecrets.Contains(s))
                .Where(s => s != "vaultwarden-kubernetes-secrets-token") // Exclude auth token secret
                .ToList();

            var namespaceSummary = new OrphanNamespaceSummary
            {
                Name = namespaceName,
                OrphansFound = orphanedSecrets.Count,
                OrphanNames = orphanedSecrets
            };

            if (!orphanedSecrets.Any())
            {
                _logger.LogDebug("No orphaned secrets found in namespace {Namespace}", namespaceName);
                return namespaceSummary;
            }

            _logger.LogDebug("Namespace {Namespace} orphan cleanup: {Count} orphaned secret(s)", namespaceName, orphanedSecrets.Count);

            foreach (var orphanedSecret in orphanedSecrets)
            {
                try
                {
                    if (_syncConfig.DryRun)
                    {
                        _logger.LogDebug("[DRY RUN] Namespace {Namespace}: would delete orphaned secret {SecretName}", 
                            namespaceName, orphanedSecret);
                        namespaceSummary.OrphansDeleted++; // Count as "would delete" for dry run
                    }
                    else
                    {
                        var deleteSuccess = await _kubernetesService.DeleteSecretAsync(namespaceName, orphanedSecret);
                        if (deleteSuccess)
                        {
                            _logger.LogDebug("Namespace {Namespace}: deleted orphaned secret {SecretName}", 
                                namespaceName, orphanedSecret);
                            namespaceSummary.OrphansDeleted++;
                            
                            // Update database status to Deleted
                            await _dbLogger.UpsertSecretStateAsync(
                                namespaceName,
                                orphanedSecret,
                                string.Empty, // VaultwardenItemId not known for orphaned secret
                                orphanedSecret, // Use secret name as item name fallback
                                SecretStatusConstants.Deleted,
                                0, // No data keys for deleted secret
                                "Secret removed - no longer configured in Vaultwarden"
                            );
                            _logger.LogDebug("Updated database status to Deleted for {Namespace}/{SecretName}", 
                                namespaceName, orphanedSecret);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete orphaned secret {SecretName} in namespace {Namespace}", 
                        orphanedSecret, namespaceName);
                }
            }

            return namespaceSummary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup orphaned secrets in namespace {Namespace}", namespaceName);
            return null;
        }
    }

    private static string SanitizeSecretName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Secret name cannot be null, empty, or whitespace. Please provide a valid name or use the 'secret-name' custom field to specify a valid Kubernetes secret name.", nameof(name));
        }

        // Basic sanitization - replace invalid characters with hyphens and convert to lowercase
        // Let Kubernetes API handle the actual validation and provide real error messages
        var sanitized = name.ToLowerInvariant()
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
            .Replace("?", "-");
        
        // Collapse multiple consecutive hyphens and trim
        sanitized = Regex.Replace(sanitized, "-+", "-").Trim('-');
        
        // Basic check for completely empty result
        if (string.IsNullOrEmpty(sanitized))
        {
            throw new ArgumentException($"Secret name cannot be null, empty, or whitespace. '{name}' becomes empty after sanitization. Please provide a name with at least one alphanumeric character or use the 'secret-name' custom field to specify a valid Kubernetes secret name.", nameof(name));
        }
        
        // Truncate to Kubernetes limit (253 characters for secret names)
        if (sanitized.Length > 253)
        {
            sanitized = sanitized.Substring(0, 253).TrimEnd('-');
        }
        
        return sanitized;
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
                ? SanitizeSecretName(extractedSecretName) 
                : SanitizeSecretName(item.Name);

            if (!itemsBySecretName.ContainsKey(secretName))
            {
                itemsBySecretName[secretName] = new List<Models.VaultwardenItem>();
            }
            itemsBySecretName[secretName].Add(item);
        }

        return itemsBySecretName;
    }

    private async Task<SecretSummary> SyncSecretAsync(string namespaceName, string secretName, List<Models.VaultwardenItem> items, long syncLogId)
    {
        var secretSummary = new SecretSummary
        {
            Name = secretName,
            SourceItemCount = items.Count
        };
        
        _logger.LogInformation("SyncSecretAsync: Starting sync for secret {SecretName} in namespace {Namespace} from {Count} item(s)", 
            secretName, namespaceName, items.Count);
        
        try
        {
            // Validate that namespace exists before attempting any operations
            var namespaceExists = await _kubernetesService.NamespaceExistsAsync(namespaceName);
            if (!namespaceExists)
            {
                var errorMsg = $"Namespace '{namespaceName}' does not exist in Kubernetes cluster";
                _logger.LogError("SyncSecretAsync: {Error}. Skipping secret {SecretName}", errorMsg, secretName);
                secretSummary.Outcome = ReconcileOutcome.Failed;
                secretSummary.Error = errorMsg;
                
                // Log failed secret state to database
                var itemForState = items.FirstOrDefault();
                if (itemForState != null)
                {
                    await _dbLogger.UpsertSecretStateAsync(
                        namespaceName,
                        secretName,
                        itemForState.Id,
                        itemForState.Name,
                        SecretStatusConstants.Failed,
                        0,
                        errorMsg
                    );
                }
                
                return secretSummary;
            }
            
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
                
                // Skip expensive debug logging completely in production
                // LogHashCalculationData(item, _logger);
            }

            _logger.LogDebug("SyncSecretAsync: Combined secret data for {SecretName} has {KeyCount} keys: {Keys}", 
                secretName, combinedSecretData.Count, string.Join(", ", combinedSecretData.Keys));

            // Create a combined hash for all items
            var combinedHash = string.Join("|", itemHashes.OrderBy(h => h));
            var hashAnnotationKey = Constants.Kubernetes.HashAnnotationKey;

            if (_syncConfig.DryRun)
            {
                _logger.LogDebug("[DRY RUN] Secret {SecretName} in {Namespace}: ensure up-to-date from {Count} item(s)", 
                    secretName, namespaceName, items.Count);
                secretSummary.Outcome = ReconcileOutcome.Skipped;
                
                // Log to database even in dry run
                var itemForLog = items.FirstOrDefault();
                if (itemForLog != null)
                {
                    await _dbLogger.UpsertSecretStateAsync(
                        namespaceName,
                        secretName,
                        itemForLog.Id,
                        itemForLog.Name,
                        "DryRun",
                        combinedSecretData.Count,
                        null
                    );
                }
                
                return secretSummary;
            }

            // Check if there's an existing secret with the old name (based on item name)
            var oldSecretName = SanitizeSecretName(items.First().Name);
            var oldSecretExists = await SecretExistsCachedAsync(namespaceName, oldSecretName);
            var newSecretExists = await SecretExistsCachedAsync(namespaceName, secretName);

            bool success = true;
            bool didCreate = false;
            bool didUpdate = false;
            string? changeReason = null;

            // If the secret name changed and old secret exists, delete it
            if (oldSecretName != secretName && oldSecretExists)
            {
                if (_syncConfig.DryRun)
                {
                    _logger.LogDebug("[DRY RUN] Would delete old secret {OldSecretName} in namespace {Namespace} due to name change", 
                        oldSecretName, namespaceName);
                }
                else
                {
                    var deleteSuccess = await _kubernetesService.DeleteSecretAsync(namespaceName, oldSecretName);
                    if (deleteSuccess)
                    {
                        _logger.LogDebug("Deleted old secret {OldSecretName} in namespace {Namespace} due to name change", 
                            oldSecretName, namespaceName);
                    }
                    else
                    {
                        success = false;
                    }
                }
            }

            // Create or update the new secret
            // Note: Verify secret actually exists even if cache says it does (cache may be stale if secret was deleted externally)
            _logger.LogDebug("SyncSecretAsync: Checking if secret {SecretName} exists in namespace {Namespace}", secretName, namespaceName);
            var existingData = await _kubernetesService.GetSecretDataAsync(namespaceName, secretName);
            var actuallyExists = existingData != null;
            _logger.LogDebug("SyncSecretAsync: Secret {SecretName} exists check result: {Exists}", secretName, actuallyExists);
            
            // If cache said it exists but it doesn't, clear the cache entry
            if (newSecretExists && !actuallyExists)
            {
                _logger.LogInformation("Secret {SecretName} in namespace {Namespace} was cached as existing but doesn't exist - clearing cache and will create", 
                    secretName, namespaceName);
                var cacheKey = $"{namespaceName}/{secretName}";
                _secretExistsCache.Remove(cacheKey);
            }
            
            // Also verify via SecretExistsAsync if GetSecretDataAsync returned null (in case of connection errors)
            if (!actuallyExists)
            {
                var verifiedExists = await _kubernetesService.SecretExistsAsync(namespaceName, secretName);
                if (verifiedExists)
                {
                    _logger.LogDebug("Secret {SecretName} in namespace {Namespace} exists but GetSecretDataAsync returned null - will retry", 
                        secretName, namespaceName);
                    // Retry getting data
                    existingData = await _kubernetesService.GetSecretDataAsync(namespaceName, secretName);
                    actuallyExists = existingData != null;
                }
                else
                {
                    _logger.LogDebug("Secret {SecretName} in namespace {Namespace} does not exist - will create", 
                        secretName, namespaceName);
                }
            }
            
            if (actuallyExists)
            {
                // Check if the secret data has changed
                var hasDataChanged = HasSecretDataChanged(existingData!, combinedSecretData);

                // Check if the hash has changed (stored in annotations)
                // Note: GetSecretAnnotationsAsync may return null if secret doesn't exist or on error
                var existingAnnotations = await _kubernetesService.GetSecretAnnotationsAsync(namespaceName, secretName);
                string? oldHashValue = existingAnnotations?.GetValueOrDefault(hashAnnotationKey);
                bool hasHashChanged = oldHashValue != combinedHash;

                // Log detailed information about what changed
                if (hasHashChanged)
                {
                    _logger.LogDebug("Hash changed for secret {SecretName} in namespace {Namespace}: old={OldHash}, new={NewHash}", 
                        secretName, namespaceName, oldHashValue, combinedHash);
                }

                if (!hasDataChanged && !hasHashChanged)
                {
                    // Even if no changes detected, verify the secret still exists
                    // It might have been deleted externally after we retrieved the data
                    var stillExists = await _kubernetesService.SecretExistsAsync(namespaceName, secretName);
                    if (!stillExists)
                    {
                        _logger.LogInformation("Secret {SecretName} in namespace {Namespace} was found but no longer exists - will recreate", 
                            secretName, namespaceName);
                        
                        // Clear cache entry
                        var cacheKey = $"{namespaceName}/{secretName}";
                        _secretExistsCache.Remove(cacheKey);
                        
                        // Fall through to create logic below by setting actuallyExists to false
                        actuallyExists = false;
                    }
                    else
                    {
                        _logger.LogDebug("Reconciled secret {SecretName} in namespace {Namespace}: Skipped (UpToDate and exists)", secretName, namespaceName);
                        secretSummary.Outcome = ReconcileOutcome.Skipped;
                        
                        // Log secret state to database
                        var itemForState = items.FirstOrDefault();
                        if (itemForState != null)
                        {
                            await _dbLogger.UpsertSecretStateAsync(
                                namespaceName,
                                secretName,
                                itemForState.Id,
                                itemForState.Name,
                                SecretStatusConstants.Active,
                                combinedSecretData.Count,
                                null
                            );
                        }

                        // Update database logger progress with skipped item
                        if (syncLogId > 0)
                        {
                            await _dbLogger.UpdateSyncProgressAsync(syncLogId, 0, 0, 0, 1, 0, 0);
                        }
                        
                        return secretSummary;
                    }
                }

                // If we reach here, changes were detected and secret exists - proceed with update
                if (actuallyExists)
                {
                    // Store the hash in annotations instead of secret data
                    var annotations = new Dictionary<string, string>
                    {
                        { hashAnnotationKey, combinedHash }
                    };

                    var updateResult = await _kubernetesService.UpdateSecretAsync(namespaceName, secretName, combinedSecretData, annotations);
                    success = updateResult.Success;
                    if (success)
                    {
                        didUpdate = true;
                        if (hasDataChanged && hasHashChanged)
                            changeReason = "content+metadata";
                        else if (hasDataChanged)
                            changeReason = "content";
                        else if (hasHashChanged)
                            changeReason = "metadata";
                        else if (string.IsNullOrEmpty(oldHashValue))
                            changeReason = "initial-hash";
                        
                        _logger.LogDebug("Reconciled secret {SecretName} in namespace {Namespace}: Updated ({Reason})", 
                            secretName, namespaceName, changeReason);
                    }
                    else
                    {
                        // If update failed with NotFound (secret was deleted externally), retry as create
                        if (updateResult.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
                            updateResult.ErrorMessage?.Contains("does not exist", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            _logger.LogInformation("Update failed because secret doesn't exist - retrying as create: {SecretName} in namespace {Namespace}", 
                                secretName, namespaceName);
                            
                            // Clear cache entry
                            var cacheKey = $"{namespaceName}/{secretName}";
                            _secretExistsCache.Remove(cacheKey);
                            
                            // Retry as create
                            var createResult = await _kubernetesService.CreateSecretAsync(namespaceName, secretName, combinedSecretData, annotations);
                            success = createResult.Success;
                            if (success)
                            {
                                didCreate = true;
                                _logger.LogInformation("Reconciled secret {SecretName} in namespace {Namespace}: Created (after update failed)", 
                                    secretName, namespaceName);
                            }
                            else
                            {
                                secretSummary.Error = createResult.ErrorMessage ?? "Failed to create secret after update failed";
                            }
                        }
                        else
                        {
                            secretSummary.Error = updateResult.ErrorMessage ?? "Failed to update secret";
                        }
                    }
                }
            }
            
            // If secret doesn't exist (or was deleted), create it
            if (!actuallyExists)
            {
                // Secret doesn't exist - create it
                _logger.LogInformation("Creating secret {SecretName} in namespace {Namespace} (secret does not exist). Data keys: {Keys}", 
                    secretName, namespaceName, string.Join(", ", combinedSecretData.Keys));
                
                // Store the hash in annotations instead of secret data
                var annotations = new Dictionary<string, string>
                {
                    { hashAnnotationKey, combinedHash }
                };

                var createResult = await _kubernetesService.CreateSecretAsync(namespaceName, secretName, combinedSecretData, annotations);
                success = createResult.Success;
                if (success)
                {
                    didCreate = true;
                    _logger.LogInformation("Successfully created secret {SecretName} in namespace {Namespace}", secretName, namespaceName);
                }
                else
                {
                    _logger.LogError("Failed to create secret {SecretName} in namespace {Namespace}: {Error}", 
                        secretName, namespaceName, createResult.ErrorMessage ?? "Unknown error");
                    secretSummary.Error = createResult.ErrorMessage ?? "Failed to create secret";
                }
            }

            // Set the outcome based on what happened
            if (!success)
            {
                secretSummary.Outcome = ReconcileOutcome.Failed;
                // Error message already set above when the operation failed
            }
            else if (didCreate)
            {
                secretSummary.Outcome = ReconcileOutcome.Created;
            }
            else if (didUpdate)
            {
                secretSummary.Outcome = ReconcileOutcome.Updated;
                secretSummary.ChangeReason = changeReason;
            }
            else
            {
                secretSummary.Outcome = ReconcileOutcome.Skipped;
                // Update database progress immediately for skipped items
                if (syncLogId > 0)
                {
                    await _dbLogger.UpdateSyncProgressAsync(syncLogId, 0, 0, 0, 1, 0, 0);
                }
            }
            
            // Log secret state to database
            // All successful outcomes (Created, Updated, Skipped) are marked as Active
            var status = secretSummary.Outcome switch
            {
                ReconcileOutcome.Created => SecretStatusConstants.Active,
                ReconcileOutcome.Updated => SecretStatusConstants.Active,
                ReconcileOutcome.Skipped => SecretStatusConstants.Active,
                ReconcileOutcome.Failed => SecretStatusConstants.Failed,
                _ => SecretStatusConstants.Failed
            };
            
            var firstItem = items.FirstOrDefault();
            if (firstItem != null)
            {
                await _dbLogger.UpsertSecretStateAsync(
                    namespaceName,
                    secretName,
                    firstItem.Id,
                    firstItem.Name,
                    status,
                    combinedSecretData.Count,
                    secretSummary.Error
                );
            }
            
            return secretSummary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile secret {SecretName} in namespace {Namespace}. Exception: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}", 
                secretName, namespaceName, ex.GetType().Name, ex.Message, ex.StackTrace);
            secretSummary.Outcome = ReconcileOutcome.Failed;
            secretSummary.Error = ex.Message;
            
            // Log failed secret state to database
            var firstItem = items.FirstOrDefault();
            if (firstItem != null)
            {
                await _dbLogger.UpsertSecretStateAsync(
                    namespaceName,
                    secretName,
                    firstItem.Id,
                    firstItem.Name,
                    SecretStatusConstants.Failed,
                    0,
                    ex.Message
                );
            }
            
            return secretSummary;
        }
    }

    private async Task<bool> SecretExistsCachedAsync(string namespaceName, string secretName)
    {
        var cacheKey = $"{namespaceName}/{secretName}";
        var now = DateTime.UtcNow;
        
        // Cache secret existence checks to reduce Kubernetes API calls
        // However, don't trust the cache blindly - if cached as existing, we still verify
        // before critical operations. The cache mainly helps avoid repeated checks.
        if (_secretExistsCache.TryGetValue(cacheKey, out var cachedTime) && 
            (now - cachedTime).TotalSeconds < Constants.Cache.SecretExistsCacheTimeoutSeconds)
        {
            // Cache says it exists, but we'll verify when we actually need the data
            // This is just a hint to avoid unnecessary API calls
            return true;
        }
        
        // Cache miss or expired - check actual existence
        var exists = await _kubernetesService.SecretExistsAsync(namespaceName, secretName);
        if (exists)
        {
            _secretExistsCache[cacheKey] = now;
        }
        else
        {
            _secretExistsCache.Remove(cacheKey); // Remove from cache if doesn't exist
        }
        
        return exists;
    }

    private int GetSyncCount()
    {
        return ++_syncCount;
    }

    private static bool IsMetadataField(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return false;
        
        var metadataFields = new[]
        {
            Models.FieldNameConfig.NamespacesFieldName,
            "namespaces", // Legacy/alternative name
            Models.FieldNameConfig.SecretNameFieldName,
            "secret-name", // Legacy/alternative name
            Models.FieldNameConfig.SecretKeyPasswordFieldName,
            "secret-key-password", // Legacy/alternative name
            "secret-key", // Legacy/alternative name for secret-key-password
            Models.FieldNameConfig.SecretKeyUsernameFieldName,
            "secret-key-username", // Legacy/alternative name
            Models.FieldNameConfig.IgnoreFieldName
        };
        
        return metadataFields.Any(meta => 
            string.Equals(fieldName, meta, StringComparison.OrdinalIgnoreCase));
    }

    private static string CalculateQuickItemsHash(List<Models.VaultwardenItem> items)
    {
        // Include content-sensitive data to detect actual changes
        // Use RevisionDate (should change when content changes) + key fields
        var quickData = items
            .OrderBy(i => i.Id)
            .Select(i => $"{i.Id}:{i.RevisionDate:O}:{GetContentHash(i)}")
            .ToList();
        
        var combinedData = $"{items.Count}|{string.Join("|", quickData)}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combinedData));
        return Convert.ToBase64String(hashBytes);
    }

    private static string GetContentHash(Models.VaultwardenItem item)
    {
        // This should match EXACTLY what ExtractSecretDataAsync() uses to build secrets
        // Any field that contributes to secret data must be included here
        var contentParts = new List<string>();
        
        // Item name and type (affects secret structure)
        contentParts.Add($"name:{item.Name}");
        contentParts.Add($"type:{item.Type}");
        
        // Top-level password field
        if (!string.IsNullOrEmpty(item.Password))
        {
            contentParts.Add($"password:{item.Password}");
        }
        
        // Login items (username and password)
        if (item.Login != null)
        {
            contentParts.Add($"login_user:{item.Login.Username ?? ""}");
            contentParts.Add($"login_pass:{item.Login.Password ?? ""}");
            
            // URIs can affect the secret (sometimes used in custom logic)
            if (item.Login.Uris != null)
            {
                foreach (var uri in item.Login.Uris)
                {
                    contentParts.Add($"uri:{uri.Uri}");
                }
            }
        }
        
        // Notes - this includes secure note content AND any embedded kv pairs
        if (!string.IsNullOrEmpty(item.Notes))
        {
            contentParts.Add($"notes:{item.Notes}");
        }
        
        // SSH keys (all parts)
        if (item.SshKey != null)
        {
            contentParts.Add($"ssh_private:{item.SshKey.PrivateKey ?? ""}");
            contentParts.Add($"ssh_public:{item.SshKey.PublicKey ?? ""}");
            contentParts.Add($"ssh_fingerprint:{item.SshKey.Fingerprint ?? ""}");
        }
        
        // Card data (all fields that could become secret keys)
        if (item.Card != null)
        {
            contentParts.Add($"card_name:{item.Card.CardholderName ?? ""}");
            contentParts.Add($"card_number:{item.Card.Number ?? ""}");
            contentParts.Add($"card_code:{item.Card.Code ?? ""}");
            contentParts.Add($"card_brand:{item.Card.Brand ?? ""}");
            contentParts.Add($"card_exp:{item.Card.ExpMonth ?? ""}/{item.Card.ExpYear ?? ""}");
        }
        
        // Identity data
        if (item.Identity != null)
        {
            contentParts.Add($"identity:{item.Identity.FirstName}:{item.Identity.LastName}:{item.Identity.Email}:{item.Identity.Username}");
        }
        
        // Custom fields (these become secret keys directly, EXCEPT metadata fields)
        if (item.Fields != null)
        {
            foreach (var field in item.Fields.OrderBy(f => f.Name))
            {
                // Skip metadata fields that control sync behavior but don't go into secrets
                if (!IsMetadataField(field.Name))
                {
                    contentParts.Add($"field:{field.Name}:{field.Value}:{field.Type}");
                }
            }
        }
        
        // Attachments (could affect secret if processed)
        if (item.Attachments != null)
        {
            foreach (var attachment in item.Attachments.OrderBy(a => a.FileName))
            {
                contentParts.Add($"attachment:{attachment.FileName}:{attachment.Size}");
            }
        }
        
        if (contentParts.Count == 0) return "no-content";
        
        var combinedContent = string.Join("|", contentParts);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combinedContent));
        return Convert.ToBase64String(hashBytes).Substring(0, 8); // First 8 chars for compact representation
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

            item.ExtractSecretKeyPassword() ?? "",
            item.ExtractSecretKeyUsername() ?? "",
            string.Join(",", item.ExtractNamespaces().OrderBy(ns => ns))
        };

        if (item.SshKey != null)
        {
            hashData.Add(item.SshKey.PrivateKey ?? "");
            hashData.Add(item.SshKey.PublicKey ?? "");
            hashData.Add(item.SshKey.Fingerprint ?? "");
        }

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

        // Add custom fields if available (excluding metadata fields)
        if (item.Fields?.Any() == true)
        {
            var sortedFields = item.Fields
                .Where(f => !IsMetadataField(f.Name))
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

            $"ExtractSecretKeyPassword: {item.ExtractSecretKeyPassword()}",
            $"ExtractSecretKeyUsername: {item.ExtractSecretKeyUsername()}",
            $"Namespaces: {string.Join(",", item.ExtractNamespaces().OrderBy(ns => ns))}",
            $"RevisionDate: {item.RevisionDate:O}"
        };

        if (item.SshKey != null)
        {
            hashData.Add($"SshKey.PrivateKey: {(string.IsNullOrEmpty(item.SshKey.PrivateKey) ? "<empty>" : "<set>")}");
            hashData.Add($"SshKey.PublicKey: {(string.IsNullOrEmpty(item.SshKey.PublicKey) ? "<empty>" : "<set>")}");
            hashData.Add($"SshKey.Fingerprint: {item.SshKey.Fingerprint}");
        }

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
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name cannot be null, empty, or whitespace. Please provide a valid field name.", nameof(fieldName));
        }

        // Get the replacement character from environment variable, default to dash for better Kubernetes compatibility
        var replacementChar = Environment.GetEnvironmentVariable("SYNC__FIELD__REPLACEMENT_CHAR")?.Trim() ?? "-";
        if (replacementChar.Length != 1 || !"-._".Contains(replacementChar))
        {
            replacementChar = "-"; // Fallback to dash if invalid
        }

        // Only replace truly forbidden characters, preserve case and valid characters
        var sanitized = fieldName;
        
        // Replace only forbidden characters with the configured replacement character (preserve case, hyphens, dots, etc.)
        // Kubernetes env var pattern: [-._a-zA-Z][-._a-zA-Z0-9]*
        sanitized = Regex.Replace(sanitized, $@"[^-._a-zA-Z0-9]", Regex.Escape(replacementChar));
        
        // Replace multiple consecutive replacement characters with single one
        sanitized = Regex.Replace(sanitized, $"{Regex.Escape(replacementChar)}+", replacementChar);
        
        // Trim leading and trailing replacement characters
        sanitized = sanitized.Trim(replacementChar[0]);
        
        // Additional validation: ensure the field name contains at least one alphanumeric character
        // This prevents cases like "..." or "---" from being considered valid
        if (!Regex.IsMatch(sanitized, @"[a-zA-Z0-9]"))
        {
            throw new ArgumentException($"Field name '{fieldName}' must contain at least one alphanumeric character. Please provide a valid field name.", nameof(fieldName));
        }
        
        return sanitized;
    }

    /// <summary>
    /// Formats a value to ensure proper handling of multiline content.
    /// This method ensures that multiline values (like SSH keys, certificates, etc.)
    /// are preserved correctly when stored in Kubernetes secrets.
    /// Also converts literal escape sequences (like \n) to actual characters.
    /// </summary>
    /// <param name="value">The value to format</param>
    /// <returns>The formatted value with proper multiline handling</returns>
    private static string FormatMultilineValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Check if value contains literal escape sequences (backslash + character)
        // Note: In C# strings, "\\n" represents the literal characters backslash + n
        var hasLiteralEscapes = value.Contains("\\n") || value.Contains("\\r") || value.Contains("\\t");
        var hasActualNewlines = value.Contains('\n') || value.Contains('\r');
        
        // First, convert literal escape sequences to actual characters
        // This handles cases where values are stored as strings with \n escape sequences
        // (e.g., "users:\n  authelia:\n..." should become actual multiline content)
        // Note: We need to handle double backslashes first to avoid converting \\n incorrectly
        var withEscapesConverted = value
            .Replace("\\\\", "\u0001")  // Temporarily replace \\ with a placeholder (must be first)
            .Replace("\\n", "\n")        // Convert \n to actual newline
            .Replace("\\r", "\r")        // Convert \r to actual carriage return
            .Replace("\\t", "\t")        // Convert \t to actual tab
            .Replace("\u0001", "\\");    // Restore literal backslashes

        // Normalize line endings to ensure consistent handling across platforms
        var normalizedValue = withEscapesConverted.Replace("\r\n", "\n").Replace("\r", "\n");
        
        // Verify conversion happened
        var hasNewlinesAfter = normalizedValue.Contains('\n');
        if (hasLiteralEscapes && !hasNewlinesAfter)
        {
            // This shouldn't happen, but if it does, log it (can't use _logger in static method)
            // The conversion should have worked
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

        // First convert escape sequences, then normalize line endings
        // Note: We need to handle double backslashes first to avoid converting \\n incorrectly
        var withEscapesConverted = value
            .Replace("\\\\", "\u0001")  // Temporarily replace \\ with a placeholder (must be first)
            .Replace("\\n", "\n")        // Convert \n to actual newline
            .Replace("\\r", "\r")        // Convert \r to actual carriage return
            .Replace("\\t", "\t")        // Convert \t to actual tab
            .Replace("\u0001", "\\");    // Restore literal backslashes

        var normalized = withEscapesConverted.Replace("\r\n", "\n").Replace("\r", "\n");

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