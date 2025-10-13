using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Database;
using VaultwardenK8sSync.Database.Models;
using VaultwardenK8sSync.Database.Repositories;

namespace VaultwardenK8sSync.Services;

public class DatabaseLoggerService : IDatabaseLoggerService
{
    private readonly SyncDbContext? _context;
    private readonly ISyncLogRepository? _syncLogRepository;
    private readonly ISecretStateRepository? _secretStateRepository;
    private readonly ILogger<DatabaseLoggerService> _logger;
    private readonly bool _isEnabled;
    private readonly Dictionary<long, SyncLog> _activeSyncLogs = new();
    private readonly Dictionary<long, DateTime> _syncStartTimes = new();

    public DatabaseLoggerService(
        ILogger<DatabaseLoggerService> logger,
        SyncDbContext? context = null,
        ISyncLogRepository? syncLogRepository = null,
        ISecretStateRepository? secretStateRepository = null)
    {
        _logger = logger;
        _context = context;
        _syncLogRepository = syncLogRepository;
        _secretStateRepository = secretStateRepository;
        _isEnabled = context != null && syncLogRepository != null && secretStateRepository != null;

        if (!_isEnabled)
        {
            _logger.LogInformation("Database logging is disabled - database context not configured");
        }
    }

    public async Task<long> StartSyncLogAsync(string phase, int totalItems = 0)
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
                DurationSeconds = 0
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

        try
        {
            _logger.LogInformation("💾 Completing sync log {SyncLogId} with status: {Status}", syncLogId, status);
            
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
            }
            else
            {
                _logger.LogWarning("⚠️  Sync log {SyncLogId} not found in active logs", syncLogId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to complete sync log {SyncLogId}", syncLogId);
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
}
