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
        // Batch all statistics into a single query using GroupBy
        var stats = await _context.SyncLogs
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalSyncs = g.Count(),
                SuccessfulSyncs = g.Count(s => s.Status == "Success"),
                FailedSyncs = g.Count(s => s.Status == "Failed"),
                TotalSecretsCreated = g.Sum(s => s.CreatedSecrets),
                TotalSecretsUpdated = g.Sum(s => s.UpdatedSecrets),
                AvgDuration = g.Where(s => s.DurationSeconds > 0).Average(s => (double?)s.DurationSeconds) ?? 0
            })
            .FirstOrDefaultAsync();

        var lastSync = await _context.SyncLogs
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();

        // Use EndTime if available (when sync completed), otherwise use StartTime
        DateTime? lastSyncTime = lastSync?.EndTime ?? lastSync?.StartTime;

        return new Dictionary<string, object>
        {
            ["totalSyncs"] = stats?.TotalSyncs ?? 0,
            ["successfulSyncs"] = stats?.SuccessfulSyncs ?? 0,
            ["failedSyncs"] = stats?.FailedSyncs ?? 0,
            ["totalSecretsCreated"] = stats?.TotalSecretsCreated ?? 0,
            ["totalSecretsUpdated"] = stats?.TotalSecretsUpdated ?? 0,
            ["averageDuration"] = stats?.AvgDuration ?? 0,
            ["lastSyncTime"] = lastSyncTime!,
            ["lastSyncStatus"] = lastSync?.Status ?? "Never"
        };
    }
}
