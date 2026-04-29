using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace VaultwardenK8sSync.Services;

/// <summary>
/// Dynamic real-time progress display for sync operations.
/// Buffers output during sync and only emits when changes are detected
/// compared to the previous sync summary.
/// </summary>
public class DynamicSyncProgressDisplay : IDisposable
{
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, SyncItemStatus> _items = new();
    private readonly ILogger? _logger;
    private readonly SyncSummary? _previousSummary;
    private readonly List<(string Message, LogLevel Level)> _buffer = new();
    private bool _disposed;
    private bool _isActive;
    private DateTime _startTime;
    private string _currentPhase = "";

    private int _totalItems;
    private int _processedItems;
    private int _createdSecrets;
    private int _updatedSecrets;
    private int _skippedSecrets;
    private int _failedSecrets;

    public DynamicSyncProgressDisplay(ILogger? logger = null, SyncSummary? previousSummary = null)
    {
        _logger = logger;
        _previousSummary = previousSummary;
    }

    public void Start(string phase, int totalItems = 0)
    {
        lock (_lock)
        {
            _isActive = true;
            _startTime = DateTime.UtcNow;
            _currentPhase = phase;
            _totalItems = totalItems;
            _processedItems = 0;
            _createdSecrets = 0;
            _updatedSecrets = 0;
            _skippedSecrets = 0;
            _failedSecrets = 0;
            _items.Clear();
            _buffer.Clear();

            BufferLine($"=== Sync started: {phase} ===");
            if (totalItems > 0)
            {
                BufferLine($"Total items to process: {totalItems}");
            }
        }
    }

    public void SetPhase(string phase)
    {
        lock (_lock)
        {
            _currentPhase = phase;
            BufferLine($"--- Phase: {phase}");
        }
    }

    public void AddItem(string key, string name, string status = "Pending")
    {
        var item = new SyncItemStatus
        {
            Key = key,
            Name = name,
            Status = status,
            UpdateTime = DateTime.UtcNow
        };
        
        _items.AddOrUpdate(key, item, (k, existing) => 
        {
            existing.Name = name;
            existing.Status = status;
            existing.UpdateTime = DateTime.UtcNow;
            return existing;
        });
    }

    public void UpdateItem(string key, string status, string? details = null, SyncItemOutcome? outcome = null)
    {
        SyncItemStatus updatedItem;

        lock (_lock)
        {
            updatedItem = _items.AddOrUpdate(
                key,
                _ => new SyncItemStatus
                {
                    Key = key,
                    Name = string.Empty,
                    Status = status,
                    Details = details,
                    Outcome = outcome,
                    UpdateTime = DateTime.UtcNow
                },
                (_, existing) =>
                {
                    existing.Status = status;
                    existing.Details = details;
                    existing.Outcome = outcome ?? existing.Outcome;
                    existing.UpdateTime = DateTime.UtcNow;
                    return existing;
                });

            updatedItem.Status = status;
            updatedItem.Details = details;
            updatedItem.Outcome = outcome ?? updatedItem.Outcome;

            if (outcome.HasValue && !updatedItem.Counted)
            {
                updatedItem.Counted = true;
                _processedItems++;

                switch (outcome.Value)
                {
                    case SyncItemOutcome.Created:
                        _createdSecrets++;
                        break;
                    case SyncItemOutcome.Updated:
                        _updatedSecrets++;
                        break;
                    case SyncItemOutcome.Skipped:
                        _skippedSecrets++;
                        break;
                    case SyncItemOutcome.Failed:
                        _failedSecrets++;
                        break;
                }
            }

            LogItemUpdate(updatedItem, status, details);
        }
    }

    public void Complete(string? finalMessage = null)
    {
        lock (_lock)
        {
            if (!_isActive) return;
            
            _isActive = false;
            var duration = DateTime.UtcNow - _startTime;
            var durationText = $"({duration.TotalSeconds:F1}s)";
            
            var totalProcessed = _processedItems;
            var hasErrors = _failedSecrets > 0;
            var hasChanges = _createdSecrets > 0 || _updatedSecrets > 0;
            var hasWarningsOrSkipped = _skippedSecrets > 0;
            
            if (_previousSummary != null && !hasErrors && !hasChanges && !hasWarningsOrSkipped)
            {
                _buffer.Clear();
                return;
            }
            string statusIcon;
            string statusText;
            
            if (hasErrors)
            {
                statusIcon = "❌";
                statusText = "COMPLETED WITH ERRORS";
            }
            else if (hasChanges)
            {
                statusIcon = "✅";
                statusText = "COMPLETED SUCCESSFULLY";
            }
            else
            {
                statusIcon = "⭕";
                statusText = "COMPLETED - NO CHANGES";
            }
            
            BufferLine($"\n{statusIcon} {statusText} {durationText}");

            var summaryParts = new List<string>();
            if (_totalItems > 0)
            {
                summaryParts.Add($"processed {totalProcessed}/{_totalItems}");
            }
            else if (totalProcessed > 0)
            {
                summaryParts.Add($"processed {totalProcessed}");
            }

            if (_createdSecrets > 0) summaryParts.Add($"{_createdSecrets} created");
            if (_updatedSecrets > 0) summaryParts.Add($"{_updatedSecrets} updated");
            if (_skippedSecrets > 0) summaryParts.Add($"{_skippedSecrets} skipped");
            if (_failedSecrets > 0) summaryParts.Add($"{_failedSecrets} failed");

            if (summaryParts.Any())
            {
                BufferLine($"   Summary: {string.Join(", ", summaryParts)}");
            }

            var failedItems = _items.Values
                .Where(item => item.Outcome == SyncItemOutcome.Failed)
                .OrderBy(item => item.Name)
                .ToList();

            if (failedItems.Any())
            {
                BufferLine("   Failed items:");
                foreach (var item in failedItems)
                {
                    var name = string.IsNullOrWhiteSpace(item.Name) ? item.Key : item.Name;
                    var details = string.IsNullOrWhiteSpace(item.Details) ? "No additional details" : item.Details;
                    BufferLine($"     • {name}: {details}");
                }
            }

            if (!string.IsNullOrEmpty(finalMessage))
            {
                BufferLine($"   {finalMessage}");
            }
            
            BufferLine(string.Empty);

            foreach (var (message, level) in _buffer)
            {
                WriteLine(message, level);
            }
            _buffer.Clear();
        }
    }

    private void BufferLine(string message, LogLevel level = LogLevel.Information)
    {
        _buffer.Add((message, level));
    }

    private string GetOutcomeIcon(SyncItemOutcome? outcome)
    {
        return outcome switch
        {
            SyncItemOutcome.Created => "🆕",
            SyncItemOutcome.Updated => "🔄",
            SyncItemOutcome.Skipped => "✅",
            SyncItemOutcome.Failed => "❌",
            _ => "⠿"
        };
    }

    private void LogItemUpdate(SyncItemStatus item, string status, string? details)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        var icon = GetStatusIcon(item);
        var name = string.IsNullOrWhiteSpace(item.Name) ? item.Key : item.Name;
        var keySuffix = !string.IsNullOrWhiteSpace(item.Key) && item.Key != name ? $" [{item.Key}]" : string.Empty;
        var detailText = string.IsNullOrWhiteSpace(details) ? string.Empty : $" - {details}";

        // Only log Skipped/Debug — log Created/Updated/Failed at Info level so changed items are visible
        var level = item.Outcome switch
        {
            SyncItemOutcome.Created => LogLevel.Information,
            SyncItemOutcome.Updated => LogLevel.Information,
            SyncItemOutcome.Failed => LogLevel.Information,
            SyncItemOutcome.Skipped => LogLevel.Debug,
            _ => LogLevel.Debug
        };
        BufferLine($"[{timestamp}] {icon} {name}{keySuffix}: {status}{detailText}", level);
    }

    private string GetStatusIcon(SyncItemStatus item)
    {
        if (item.Outcome.HasValue)
        {
            return GetOutcomeIcon(item.Outcome);
        }

        return "•";
    }

    private void WriteLine(string message, LogLevel level = LogLevel.Information)
    {
        if (_logger != null)
        {
            _logger.Log(level, "{Message}", message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _disposed = true;
            _isActive = false;
        }
    }
}

public class SyncItemStatus
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Details { get; set; }
    public SyncItemOutcome? Outcome { get; set; }
    public DateTime UpdateTime { get; set; }
    public bool Counted { get; set; } = false;
}

public enum SyncItemOutcome
{
    Created,
    Updated,
    Skipped,
    Failed
}
