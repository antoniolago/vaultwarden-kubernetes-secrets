using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

/// <summary>
/// Dynamic real-time progress display for sync operations
/// Shows all items and their current status in a live-updating format
/// </summary>
public class DynamicSyncProgressDisplay : IDisposable
{
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, SyncItemStatus> _items = new();
    private readonly ILogger? _logger;
    private bool _disposed;
    private bool _isActive;
    private DateTime _startTime;
    private string _currentPhase = "";

    // Sync statistics
    private int _totalItems;
    private int _processedItems;
    private int _createdSecrets;
    private int _updatedSecrets;
    private int _skippedSecrets;
    private int _failedSecrets;

    public DynamicSyncProgressDisplay(ILogger? logger = null)
    {
        _logger = logger;
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

            WriteLine($"=== Sync started: {phase} ===");
            if (totalItems > 0)
            {
                WriteLine($"Total items to process: {totalItems}");
            }
        }
    }

    public void SetPhase(string phase)
    {
        lock (_lock)
        {
            _currentPhase = phase;
            WriteLine($"--- Phase: {phase}");
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

            if (!string.IsNullOrWhiteSpace(updatedItem.Name))
            {
                // Preserve stored name; AddItem may have set it earlier.
            }

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
            
            // Calculate final statistics
            var totalProcessed = _processedItems;
            var hasErrors = _failedSecrets > 0;
            var hasChanges = _createdSecrets > 0 || _updatedSecrets > 0;
            
            // Determine final status
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
            
            WriteLine($"\n{statusIcon} {statusText} {durationText}");

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
                WriteLine($"   Summary: {string.Join(", ", summaryParts)}");
            }

            var failedItems = _items.Values
                .Where(item => item.Outcome == SyncItemOutcome.Failed)
                .OrderBy(item => item.Name)
                .ToList();

            if (failedItems.Any())
            {
                WriteLine("   Failed items:");
                foreach (var item in failedItems)
                {
                    var name = string.IsNullOrWhiteSpace(item.Name) ? item.Key : item.Name;
                    var details = string.IsNullOrWhiteSpace(item.Details) ? "No additional details" : item.Details;
                    WriteLine($"     • {name}: {details}");
                }
            }

            if (!string.IsNullOrEmpty(finalMessage))
            {
                WriteLine($"   {finalMessage}");
            }
            
            WriteLine(string.Empty);
        }
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

        WriteLine($"[{timestamp}] {icon} {name}{keySuffix}: {status}{detailText}");
    }

    private string GetStatusIcon(SyncItemStatus item)
    {
        if (item.Outcome.HasValue)
        {
            return GetOutcomeIcon(item.Outcome);
        }

        return "•";
    }

    private void WriteLine(string message)
    {
        if (_logger != null)
        {
            _logger.LogInformation("{Message}", message);
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
