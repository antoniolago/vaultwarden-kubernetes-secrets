using Microsoft.AspNetCore.Mvc;
using VaultwardenK8sSync.Database.Models;
using VaultwardenK8sSync.Database.Repositories;

namespace VaultwardenK8sSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncLogsController : ControllerBase
{
    private readonly ISyncLogRepository _repository;
    private readonly ILogger<SyncLogsController> _logger;

    public SyncLogsController(ISyncLogRepository repository, ILogger<SyncLogsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Get recent sync logs
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SyncLog>>> GetRecentLogs([FromQuery] int count = 50)
    {
        try
        {
            var logs = await _repository.GetRecentAsync(count);
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sync logs");
            return StatusCode(500, "Error retrieving sync logs");
        }
    }

    /// <summary>
    /// Get sync log by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SyncLog>> GetById(long id)
    {
        try
        {
            var log = await _repository.GetByIdAsync(id);
            if (log == null)
                return NotFound();
            
            return Ok(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sync log {Id}", id);
            return StatusCode(500, "Error retrieving sync log");
        }
    }

    /// <summary>
    /// Get sync logs within a date range
    /// </summary>
    [HttpGet("range")]
    public async Task<ActionResult<List<SyncLog>>> GetByDateRange(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end)
    {
        try
        {
            var logs = await _repository.GetByDateRangeAsync(start, end);
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sync logs for date range");
            return StatusCode(500, "Error retrieving sync logs");
        }
    }

    /// <summary>
    /// Get sync statistics
    /// </summary>
    [HttpGet("statistics")]
    public async Task<ActionResult<Dictionary<string, object>>> GetStatistics()
    {
        try
        {
            var stats = await _repository.GetStatisticsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving statistics");
            return StatusCode(500, "Error retrieving statistics");
        }
    }
}
