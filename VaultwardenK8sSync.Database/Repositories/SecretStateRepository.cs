using Microsoft.EntityFrameworkCore;
using VaultwardenK8sSync.Database.Models;

namespace VaultwardenK8sSync.Database.Repositories;

public class SecretStateRepository : ISecretStateRepository
{
    private readonly SyncDbContext _context;

    public SecretStateRepository(SyncDbContext context)
    {
        _context = context;
    }

    public async Task<SecretState> UpsertAsync(SecretState secretState)
    {
        var existing = await GetByNamespaceAndNameAsync(secretState.Namespace, secretState.SecretName);
        
        if (existing != null)
        {
            existing.VaultwardenItemId = secretState.VaultwardenItemId;
            existing.VaultwardenItemName = secretState.VaultwardenItemName;
            existing.LastSynced = secretState.LastSynced;
            existing.Status = secretState.Status;
            existing.DataKeysCount = secretState.DataKeysCount;
            existing.LastError = secretState.LastError;
            
            _context.SecretStates.Update(existing);
        }
        else
        {
            secretState.CreatedAt = DateTime.UtcNow;
            _context.SecretStates.Add(secretState);
        }

        await _context.SaveChangesAsync();
        return existing ?? secretState;
    }

    public async Task<SecretState?> GetByNamespaceAndNameAsync(string namespaceName, string secretName)
    {
        return await _context.SecretStates
            .FirstOrDefaultAsync(s => s.Namespace == namespaceName && s.SecretName == secretName);
    }

    public async Task<List<SecretState>> GetAllAsync()
    {
        return await _context.SecretStates
            .OrderBy(s => s.Namespace)
            .ThenBy(s => s.SecretName)
            .ToListAsync();
    }

    public async Task<List<SecretState>> GetByNamespaceAsync(string namespaceName)
    {
        return await _context.SecretStates
            .Where(s => s.Namespace == namespaceName)
            .OrderBy(s => s.SecretName)
            .ToListAsync();
    }

    public async Task<List<SecretState>> GetActiveSecretsAsync()
    {
        return await _context.SecretStates
            .Where(s => s.Status == "Active")
            .OrderBy(s => s.Namespace)
            .ThenBy(s => s.SecretName)
            .ToListAsync();
    }

    public async Task<(int ActiveSecretsCount, int TotalNamespaces)> GetOverviewStatsAsync()
    {
        // Single query to get both active secrets count and total namespaces
        var stats = await _context.SecretStates
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ActiveSecretsCount = g.Count(s => s.Status == "Active"),
                TotalNamespaces = g.Select(s => s.Namespace).Distinct().Count()
            })
            .FirstOrDefaultAsync();

        return (stats?.ActiveSecretsCount ?? 0, stats?.TotalNamespaces ?? 0);
    }

    public async Task DeleteAsync(long id)
    {
        var entity = await _context.SecretStates.FindAsync(id);
        if (entity != null)
        {
            _context.SecretStates.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }
}
