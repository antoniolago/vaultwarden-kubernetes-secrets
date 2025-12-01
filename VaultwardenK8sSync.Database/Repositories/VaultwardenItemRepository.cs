using Microsoft.EntityFrameworkCore;
using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database.Repositories;

public class VaultwardenItemRepository : IVaultwardenItemRepository
{
    private readonly SyncDbContext _context;

    public VaultwardenItemRepository(SyncDbContext context)
    {
        _context = context;
    }

    public async Task<List<VaultwardenItem>> GetAllAsync()
    {
        return await _context.VaultwardenItems
            .OrderBy(i => i.Name)
            .ToListAsync();
    }

    public async Task<VaultwardenItem?> GetByItemIdAsync(string itemId)
    {
        return await _context.VaultwardenItems
            .FirstOrDefaultAsync(i => i.ItemId == itemId);
    }

    public async Task<DateTime?> GetLastFetchTimeAsync()
    {
        return await _context.VaultwardenItems
            .OrderByDescending(i => i.LastFetched)
            .Select(i => i.LastFetched)
            .FirstOrDefaultAsync();
    }
}
