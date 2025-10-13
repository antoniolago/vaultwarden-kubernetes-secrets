using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database.Repositories;

public interface ISyncLogRepository
{
    Task<SyncLog> CreateAsync(SyncLog syncLog);
    Task<SyncLog?> GetByIdAsync(long id);
    Task<SyncLog> UpdateAsync(SyncLog syncLog);
    Task<List<SyncLog>> GetRecentAsync(int count = 50);
    Task<List<SyncLog>> GetByDateRangeAsync(DateTime start, DateTime end);
    Task<Dictionary<string, object>> GetStatisticsAsync();
}
