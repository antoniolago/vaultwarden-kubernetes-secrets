using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Database;
using VaultwardenK8sSync.Database.Models;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Models;
using System.Text.Json;

namespace VaultwardenK8sSync.Services;

public class DatabaseLoggerService : IDatabaseLoggerService
{
    private readonly SyncDbContext? _context;
    private readonly ISyncLogRepository? _syncLogRepository;
    private readonly ISecretStateRepository? _secretStateRepository;
    private readonly IVaultwardenService? _vaultwardenService;
    private readonly ILogger<DatabaseLoggerService> _logger;
    private readonly bool _isEnabled;
    private readonly Dictionary<long, SyncLog> _activeSyncLogs = new();
    private readonly Dictionary<long, DateTime> _syncStartTimes = new();

    public DatabaseLoggerService(
        ILogger<DatabaseLoggerService> logger,
        SyncDbContext? context = null,
        ISyncLogRepository? syncLogRepository = null,
        ISecretStateRepository? secretStateRepository = null,
        IVaultwardenService? vaultwardenService = null)
    {
        _logger = logger;
        _context = context;
        _syncLogRepository = syncLogRepository;
        _secretStateRepository = secretStateRepository;
        _vaultwardenService = vaultwardenService;
        _isEnabled = context != null && syncLogRepository != null && secretStateRepository != null;

        if (!_isEnabled)
        {
            _logger.LogInformation("Database logging is disabled - database context not configured");
        }
    }

    public async Task<long> StartSyncLogAsync(string phase, int totalItems = 0, int syncIntervalSeconds = 0, bool continuousSync = false)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Database logging is disabled - StartSyncLogAsync called but service not enabled");
            return 0;
        }

        try
        {
            _logger.LogInformation("📝 Creating sync log entry for phase: {Phase} with {TotalItems} items", phase, totalItems);
            
            var syncLog = new SyncLog
            {
                StartTime = DateTime.UtcNow,
                Status = "InProgress",
                Phase = phase,
                TotalItems = totalItems,
                ProcessedItems = 0,
                CreatedSecrets = 0,
                UpdatedSecrets = 0,
                SkippedSecrets = 0,
                FailedSecrets = 0,
                DurationSeconds = 0,
                SyncIntervalSeconds = syncIntervalSeconds,
                ContinuousSync = continuousSync
            };

            var created = await _syncLogRepository!.CreateAsync(syncLog);
            _activeSyncLogs[created.Id] = created;
            _syncStartTimes[created.Id] = DateTime.UtcNow;
            
            _logger.LogInformation("✅ Sync log created with ID: {SyncLogId}", created.Id);
            return created.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create sync log entry in database");
            return 0;
        }
    }

    public async Task UpdateSyncProgressAsync(long syncLogId, int processedItems, int created, int updated, int skipped, int failed, int deleted = 0)
    {
        if (!_isEnabled || syncLogId == 0) return;

        try
        {
            if (_activeSyncLogs.TryGetValue(syncLogId, out var syncLog))
            {
                syncLog.ProcessedItems = processedItems;
                syncLog.CreatedSecrets = created;
                syncLog.UpdatedSecrets = updated;
                syncLog.SkippedSecrets = skipped;
                syncLog.FailedSecrets = failed;
                syncLog.DeletedSecrets = deleted;

                await _syncLogRepository!.UpdateAsync(syncLog);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update sync log {SyncLogId}", syncLogId);
        }
    }

    public async Task CompleteSyncLogAsync(long syncLogId, string status, string? errorMessage = null)
    {
        if (!_isEnabled || syncLogId == 0)
        {
            if (syncLogId == 0)
                _logger.LogWarning("CompleteSyncLogAsync called with syncLogId = 0");
            return;
        }

        // Retry up to 3 times with delays to handle database concurrency issues
        var maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation("💾 Completing sync log {SyncLogId} with status: {Status} (attempt {Attempt})", syncLogId, status, attempt);
                
                if (_activeSyncLogs.TryGetValue(syncLogId, out var syncLog))
                {
                    syncLog.EndTime = DateTime.UtcNow;
                    syncLog.Status = status;
                    syncLog.ErrorMessage = errorMessage;

                    if (_syncStartTimes.TryGetValue(syncLogId, out var startTime))
                    {
                        syncLog.DurationSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
                        _syncStartTimes.Remove(syncLogId);
                    }

                    await _syncLogRepository!.UpdateAsync(syncLog);
                    _activeSyncLogs.Remove(syncLogId);
                    
                    _logger.LogInformation("✅ Sync log {SyncLogId} saved to database", syncLogId);
                    return; // Success - exit retry loop
                }
                else
                {
                    _logger.LogWarning("⚠️  Sync log {SyncLogId} not found in active logs", syncLogId);
                    return; // Nothing to update - exit retry loop
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to complete sync log {SyncLogId} (attempt {Attempt}/{MaxAttempts})", 
                    syncLogId, attempt, maxAttempts);
                
                if (attempt == maxAttempts)
                {
                    // Last attempt failed - clean up and rethrow
                    _activeSyncLogs.Remove(syncLogId);
                    _syncStartTimes.Remove(syncLogId);
                    throw new Exception($"Failed to complete sync log {syncLogId} after {maxAttempts} attempts", ex);
                }
                
                // Wait before retry with exponential backoff
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }

    public async Task LogSyncItemAsync(
        long syncLogId, 
        string itemKey, 
        string itemName, 
        string namespaceName, 
        string secretName, 
        string status, 
        string outcome, 
        string? details = null)
    {
        if (!_isEnabled || syncLogId == 0) return;

        try
        {
            var syncItem = new SyncItem
            {
                SyncLogId = syncLogId,
                ItemKey = itemKey,
                ItemName = itemName,
                Namespace = namespaceName,
                SecretName = secretName,
                Status = status,
                Outcome = outcome,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            _context!.SyncItems.Add(syncItem);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log sync item {ItemKey}", itemKey);
        }
    }

    public async Task UpsertSecretStateAsync(
        string namespaceName, 
        string secretName, 
        string vaultwardenItemId, 
        string vaultwardenItemName, 
        string status, 
        int dataKeysCount, 
        string? lastError = null)
    {
        if (!_isEnabled) return;

        try
        {
            var secretState = new SecretState
            {
                Namespace = namespaceName,
                SecretName = secretName,
                VaultwardenItemId = vaultwardenItemId,
                VaultwardenItemName = vaultwardenItemName,
                LastSynced = DateTime.UtcNow,
                Status = status,
                DataKeysCount = dataKeysCount,
                LastError = lastError
            };

            await _secretStateRepository!.UpsertAsync(secretState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert secret state for {Namespace}/{SecretName}", namespaceName, secretName);
        }
    }

    public async Task CacheVaultwardenItemsAsync(List<Models.VaultwardenItem> items)
    {
        if (!_isEnabled || _context == null)
        {
            _logger.LogDebug("Skipping Vaultwarden items cache - database not enabled");
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            
            // Fetch organization names mapping and current user email
            Dictionary<string, string> orgMap = new();
            string? userEmail = null;
            
            if (_vaultwardenService != null)
            {
                try
                {
                    orgMap = await _vaultwardenService.GetOrganizationsMapAsync();
                    _logger.LogInformation("Fetched {Count} organization names for caching", orgMap.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch organization names, proceeding without them");
                }
                
                try
                {
                    userEmail = await _vaultwardenService.GetCurrentUserEmailAsync();
                    _logger.LogInformation("Fetched current user email: {Email}", userEmail ?? "unknown");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch user email, proceeding without it");
                }
            }
            
            // Clear old cached items
            _context.VaultwardenItems.RemoveRange(_context.VaultwardenItems);
            
            // Add new items
            foreach (var item in items)
            {
                var namespaces = item.ExtractNamespaces();
                var hasNamespacesField = item.Fields?.Any(f => 
                    f.Name?.Equals("namespaces", StringComparison.OrdinalIgnoreCase) == true) ?? false;
                
                // Resolve organization name from ID
                string? orgName = null;
                if (!string.IsNullOrEmpty(item.OrganizationId) && orgMap.ContainsKey(item.OrganizationId))
                {
                    orgName = orgMap[item.OrganizationId];
                }
                
                // Determine owner: org name if org item, user email if personal
                string? owner = null;
                if (!string.IsNullOrEmpty(orgName))
                {
                    owner = orgName;
                }
                else if (!string.IsNullOrEmpty(item.OrganizationId))
                {
                    // Has org ID but no name resolved
                    owner = $"Org ({item.OrganizationId.Substring(0, 8)})";
                }
                else
                {
                    // Personal item - use user email
                    owner = userEmail ?? "Personal";
                }
                
                // Extract field names
                var fieldNames = item.Fields?
                    .Select(f => f.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList() ?? new List<string>();
                
                var dbItem = new Database.Models.VaultwardenItem
                {
                    ItemId = item.Id,
                    Name = item.Name,
                    FolderId = item.FolderId,
                    OrganizationId = item.OrganizationId,
                    OrganizationName = orgName,
                    Owner = owner,
                    FieldCount = item.Fields?.Count ?? 0,
                    FieldNamesJson = fieldNames.Any() ? JsonSerializer.Serialize(fieldNames) : null,
                    Notes = item.Notes,
                    LastFetched = now,
                    HasNamespacesField = hasNamespacesField,
                    NamespacesJson = namespaces.Any() ? JsonSerializer.Serialize(namespaces) : null
                };
                
                _context.VaultwardenItems.Add(dbItem);
            }
            
            await _context.SaveChangesAsync();
            _logger.LogInformation("Cached {Count} Vaultwarden items in database", items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache Vaultwarden items");
        }
    }

    public async Task<int> CleanupStaleSecretStatesAsync(List<Models.VaultwardenItem> currentItems)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("Skipping stale secret states cleanup - database not enabled");
            return 0;
        }

        try
        {
            // Build a set of all valid (namespace, secretName) combinations from current items
            var validSecretKeys = new HashSet<string>();
            
            foreach (var item in currentItems)
            {
                var namespaces = item.ExtractNamespaces();
                foreach (var namespaceName in namespaces)
                {
                    // Get the secret name that would be used for this item
                    var extractedSecretName = item.ExtractSecretName();
                    var secretName = !string.IsNullOrEmpty(extractedSecretName) 
                        ? SanitizeSecretName(extractedSecretName) 
                        : SanitizeSecretName(item.Name);
                    
                    var key = $"{namespaceName}:{secretName}";
                    validSecretKeys.Add(key);
                }
            }

            // Get all secret states from database
            var allSecretStates = await _secretStateRepository!.GetAllAsync();
            
            // Find stale entries - those that don't match any current Vaultwarden items
            var staleStates = allSecretStates
                .Where(state => !validSecretKeys.Contains($"{state.Namespace}:{state.SecretName}"))
                .ToList();

            if (staleStates.Any())
            {
                _logger.LogInformation("🧹 Found {Count} stale secret state entries to cleanup", staleStates.Count);
                
                foreach (var staleState in staleStates)
                {
                    _logger.LogDebug("Removing stale secret state: {Namespace}/{SecretName}", 
                        staleState.Namespace, staleState.SecretName);
                    await _secretStateRepository.DeleteAsync(staleState.Id);
                }
                
                _logger.LogInformation("✅ Cleaned up {Count} stale secret state entries", staleStates.Count);
                return staleStates.Count;
            }
            else
            {
                _logger.LogDebug("No stale secret states found");
                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup stale secret states");
            return 0;
        }
    }

    private static string SanitizeSecretName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed-secret";

        // Convert to lowercase and replace invalid characters with hyphens
        var sanitized = name.ToLowerInvariant()
            .Replace("_", "-")
            .Replace(" ", "-")
            .Replace(".", "-");

        // Remove any characters that aren't alphanumeric or hyphens
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "[^a-z0-9-]", "");

        // Remove leading/trailing hyphens and collapse multiple hyphens
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "-+", "-")
            .Trim('-');

        // Ensure it's not empty after sanitization
        return string.IsNullOrEmpty(sanitized) ? "unnamed-secret" : sanitized;
    }
}
