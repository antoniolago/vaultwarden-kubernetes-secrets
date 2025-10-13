using Microsoft.EntityFrameworkCore;
using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database.Repositories;

public class SyncLogRepository : ISyncLogRepository
{
    private readonly SyncDbContext _context;

    public SyncLogRepository(SyncDbContext context)
    {
        _context = context;
    }

    public async Task<SyncLog> CreateAsync(SyncLog syncLog)
    {
        _context.SyncLogs.Add(syncLog);
        await _context.SaveChangesAsync();
        return syncLog;
    }

    public async Task<SyncLog?> GetByIdAsync(long id)
    {
        return await _context.SyncLogs.FindAsync(id);
    }

    public async Task<SyncLog> UpdateAsync(SyncLog syncLog)
    {
        _context.SyncLogs.Update(syncLog);
        await _context.SaveChangesAsync();
        return syncLog;
    }

    public async Task<List<SyncLog>> GetRecentAsync(int count = 50)
    {
        return await _context.SyncLogs
            .OrderByDescending(s => s.StartTime)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<SyncLog>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _context.SyncLogs
            .Where(s => s.StartTime >= start && s.StartTime <= end)
            .OrderByDescending(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<Dictionary<string, object>> GetStatisticsAsync()
    {
        var totalSyncs = await _context.SyncLogs.CountAsync();
        var successfulSyncs = await _context.SyncLogs.CountAsync(s => s.Status == "Success");
        var failedSyncs = await _context.SyncLogs.CountAsync(s => s.Status == "Failed");
        var totalSecretsCreated = await _context.SyncLogs.SumAsync(s => s.CreatedSecrets);
        var totalSecretsUpdated = await _context.SyncLogs.SumAsync(s => s.UpdatedSecrets);
        var avgDuration = await _context.SyncLogs
            .Where(s => s.DurationSeconds > 0)
            .AverageAsync(s => (double?)s.DurationSeconds) ?? 0;

        var lastSync = await _context.SyncLogs
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();

        return new Dictionary<string, object>
        {
            ["totalSyncs"] = totalSyncs,
            ["successfulSyncs"] = successfulSyncs,
            ["failedSyncs"] = failedSyncs,
            ["totalSecretsCreated"] = totalSecretsCreated,
            ["totalSecretsUpdated"] = totalSecretsUpdated,
            ["averageDuration"] = avgDuration,
            ["lastSyncTime"] = lastSync?.StartTime,
            ["lastSyncStatus"] = lastSync?.Status ?? "Never"
        };
    }
}
