using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VaultwardenK8sSync.Database;
using VaultwardenK8sSync.Database.Repositories;

namespace VaultwardenK8sSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly ISecretStateRepository _secretStateRepository;
    private readonly SyncDbContext _context;
    private readonly ILogger<DashboardController> _logger;
    private readonly AppSettings _appSettings;

    public DashboardController(
        ISyncLogRepository syncLogRepository,
        ISecretStateRepository secretStateRepository,
        SyncDbContext context,
        ILogger<DashboardController> logger,
        IOptions<AppSettings> appSettings)
    {
        _syncLogRepository = syncLogRepository;
        _secretStateRepository = secretStateRepository;
        _context = context;
        _logger = logger;
        _appSettings = appSettings.Value;
    }

    /// <summary>
    /// Get dashboard overview with key metrics
    /// </summary>
    [HttpGet("overview")]
    public async Task<ActionResult<object>> GetOverview()
    {
        try
        {
            var stats = await _syncLogRepository.GetStatisticsAsync();
            var activeSecrets = await _secretStateRepository.GetActiveSecretsAsync();
            var allSecrets = await _secretStateRepository.GetAllAsync();
            var recentLogs = await _syncLogRepository.GetRecentAsync(10);

            // Group secrets by namespace
            var secretsByNamespace = activeSecrets
                .GroupBy(s => s.Namespace)
                .Select(g => new { Namespace = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            // Calculate success rate
            var totalSyncs = (int)stats["totalSyncs"];
            var successfulSyncs = (int)stats["successfulSyncs"];
            var failedSyncs = (int)stats["failedSyncs"];
            var successRate = totalSyncs > 0 ? (double)successfulSyncs / totalSyncs * 100 : 0;

            // Get unique namespaces count
            var totalNamespaces = allSecrets.Select(s => s.Namespace).Distinct().Count();

            return Ok(new
            {
                // Flat structure matching frontend interface
                totalSyncs,
                successfulSyncs,
                failedSyncs,
                activeSecrets = activeSecrets.Count,
                totalNamespaces,
                lastSyncTime = stats["lastSyncTime"],
                averageSyncDuration = (double)stats["averageDuration"],
                successRate = Math.Round(successRate, 2),
                // Additional data
                secretsByNamespace,
                recentActivity = recentLogs.Take(5).Select(l => new
                {
                    l.Id,
                    l.StartTime,
                    l.Status,
                    l.CreatedSecrets,
                    l.UpdatedSecrets,
                    l.FailedSecrets,
                    l.DurationSeconds
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dashboard overview");
            return StatusCode(500, "Error retrieving dashboard data");
        }
    }

    /// <summary>
    /// Get sync timeline data for charts
    /// </summary>
    [HttpGet("timeline")]
    public async Task<ActionResult<object>> GetTimeline([FromQuery] int days = 7)
    {
        try
        {
            var startDate = DateTime.UtcNow.AddDays(-days);
            var logs = await _syncLogRepository.GetByDateRangeAsync(startDate, DateTime.UtcNow);

            var timeline = logs
                .GroupBy(l => l.StartTime.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalSyncs = g.Count(),
                    SuccessfulSyncs = g.Count(l => l.Status == "Success"),
                    FailedSyncs = g.Count(l => l.Status == "Failed"),
                    SecretsCreated = g.Sum(l => l.CreatedSecrets),
                    SecretsUpdated = g.Sum(l => l.UpdatedSecrets),
                    AvgDuration = g.Average(l => l.DurationSeconds)
                })
                .OrderBy(t => t.Date)
                .ToList();

            return Ok(timeline);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving timeline data");
            return StatusCode(500, "Error retrieving timeline data");
        }
    }

    /// <summary>
    /// Get namespace distribution
    /// </summary>
    [HttpGet("namespaces")]
    public async Task<ActionResult<object>> GetNamespaceDistribution()
    {
        try
        {
            var secrets = await _secretStateRepository.GetAllAsync();
            
            var distribution = secrets
                .GroupBy(s => s.Namespace)
                .Select(g => new
                {
                    Namespace = g.Key,
                    SecretCount = g.Count(),
                    ActiveSecrets = g.Count(s => s.Status == "Active"),
                    FailedSecrets = g.Count(s => s.Status == "Failed"),
                    TotalDataKeys = g.Sum(s => s.DataKeysCount),
                    LastSyncTime = g.Max(s => s.LastSynced),
                    SuccessRate = g.Count() > 0 
                        ? Math.Round((double)g.Count(s => s.Status == "Active") / g.Count() * 100, 2)
                        : 0.0
                })
                .OrderBy(n => n.Namespace)
                .ToList();

            return Ok(distribution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving namespace distribution");
            return StatusCode(500, "Error retrieving namespace distribution");
        }
    }

    /// <summary>
    /// Get sync configuration and status for progress tracking
    /// </summary>
    [HttpGet("sync-status")]
    public async Task<ActionResult<object>> GetSyncStatus()
    {
        try
        {
            var stats = await _syncLogRepository.GetStatisticsAsync();
            var lastSyncTime = stats["lastSyncTime"] as DateTime?;
            
            return Ok(new
            {
                syncIntervalSeconds = _appSettings.Sync.SyncIntervalSeconds,
                continuousSync = _appSettings.Sync.ContinuousSync,
                lastSyncTime = lastSyncTime,
                nextSyncTime = lastSyncTime.HasValue 
                    ? lastSyncTime.Value.AddSeconds(_appSettings.Sync.SyncIntervalSeconds)
                    : (DateTime?)null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sync status");
            return StatusCode(500, "Error retrieving sync status");
        }
    }
}
