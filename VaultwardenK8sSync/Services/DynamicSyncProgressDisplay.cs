using System.Collections.Concurrent;
using System.Text;
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
    private readonly Timer? _updateTimer;
    private readonly ConcurrentDictionary<string, SyncItemStatus> _items = new();
    private readonly ILogger? _logger;
    private bool _disposed = false;
    private bool _isActive = false;
    private int _lastRenderedLines = 0;
    private DateTime _startTime;
    private string _currentPhase = "";
    private bool _supportsAdvancedConsole = true;

    // Sync statistics
    private int _totalItems = 0;
    private int _processedItems = 0;
    private int _createdSecrets = 0;
    private int _updatedSecrets = 0;
    private int _skippedSecrets = 0;
    private int _failedSecrets = 0;

    public DynamicSyncProgressDisplay(ILogger? logger = null)
    {
        _logger = logger;
        
        // Test if console supports advanced features
        try
        {
            // Test cursor visibility support (Windows-specific feature)
            if (OperatingSystem.IsWindows())
            {
                var originalCursor = Console.CursorVisible;
                Console.CursorVisible = false;
                Console.CursorVisible = originalCursor;
            }
            
            // Check multiple conditions for console support
            _supportsAdvancedConsole = 
                Console.IsOutputRedirected == false &&
                Console.IsErrorRedirected == false &&
                !IsRunningInDebugConsole() &&
                !IsRunningInCI();
        }
        catch
        {
            _supportsAdvancedConsole = false;
        }
        
        if (_supportsAdvancedConsole)
        {
            try 
            { 
                if (OperatingSystem.IsWindows())
                {
                    Console.CursorVisible = false; 
                }
                _updateTimer = new Timer(UpdateDisplay, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
            } 
            catch 
            { 
                _supportsAdvancedConsole = false;
            }
        }
    }

    public void Start(string phase, int totalItems = 0)
    {
        lock (_lock)
        {
            _isActive = true;
            _startTime = DateTime.UtcNow;
            _currentPhase = phase;
            _totalItems = totalItems;
            
        }
    }

    public void SetPhase(string phase)
    {
        lock (_lock)
        {
            _currentPhase = phase;
            if (!_supportsAdvancedConsole)
            {
                Console.WriteLine($"🔄 {phase}");
            }
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
        _items.AddOrUpdate(key, 
            new SyncItemStatus 
            { 
                Key = key, 
                Status = status, 
                Details = details,
                Outcome = outcome,
                UpdateTime = DateTime.UtcNow 
            },
            (k, existing) => 
            {
                existing.Status = status;
                existing.Details = details;
                existing.Outcome = outcome;
                existing.UpdateTime = DateTime.UtcNow;
                
                // Update statistics
                if (outcome.HasValue && !existing.Counted)
                {
                    existing.Counted = true;
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
                
                return existing;
            });
        
        if (!_supportsAdvancedConsole)
        {
            // Fallback to simple logging
            var icon = GetOutcomeIcon(outcome);
            Console.WriteLine($"  {icon} {status}");
        }
    }

    public void Complete(string? finalMessage = null)
    {
        lock (_lock)
        {
            if (!_isActive) return;
            
            _isActive = false;
            
            if (_supportsAdvancedConsole)
            {
                ClearDisplay();
            }
            
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
            
            // Show final summary
            Console.WriteLine($"\n{statusIcon} {statusText} {durationText}");
            
            if (totalProcessed > 0)
            {
                var stats = new List<string>();
                if (_createdSecrets > 0) stats.Add($"{_createdSecrets} created");
                if (_updatedSecrets > 0) stats.Add($"{_updatedSecrets} updated");
                if (_skippedSecrets > 0) stats.Add($"{_skippedSecrets} up-to-date");
                if (_failedSecrets > 0) stats.Add($"{_failedSecrets} failed");
                
                if (stats.Any())
                {
                    Console.WriteLine($"   Secrets: {string.Join(", ", stats)}");
                }
            }
            
            if (!string.IsNullOrEmpty(finalMessage))
            {
                Console.WriteLine($"   {finalMessage}");
            }
            
            Console.WriteLine(); // Extra line for spacing
        }
    }

    private void UpdateDisplay(object? state)
    {
        lock (_lock)
        {
            if (!_isActive || _disposed || !_supportsAdvancedConsole) return;
            
            try
            {
                // During authentication/fetching phase with no items, update less frequently
                if (_items.IsEmpty && _currentPhase.Contains("Authenticating"))
                {
                    // Skip some updates during auth phase to reduce console interference
                    var elapsed = DateTime.UtcNow - _startTime;
                    if (elapsed.TotalSeconds % 2 < 0.5) // Only update every ~2 seconds during auth
                        return;
                }
                
                var content = BuildDisplayContent();
                RenderUpdate(content);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to update dynamic display, falling back to simple mode");
                _supportsAdvancedConsole = false;
            }
        }
    }

    private string BuildDisplayContent()
    {
        var sb = new StringBuilder();
        var elapsed = DateTime.UtcNow - _startTime;
        
        // Header with current phase and elapsed time
        sb.AppendLine($"🔄 {_currentPhase} ({elapsed.TotalSeconds:F1}s)");
        
        // Progress statistics
        if (_totalItems > 0)
        {
            var progressPercentage = _totalItems > 0 ? (_processedItems * 100.0 / _totalItems) : 0;
            var progressBar = CreateProgressBar(progressPercentage, 30);
            sb.AppendLine($"   Progress: {progressBar} {_processedItems}/{_totalItems} ({progressPercentage:F0}%)");
        }
        
        // Quick stats summary
        if (_processedItems > 0)
        {
            var stats = new List<string>();
            if (_createdSecrets > 0) stats.Add($"Created: {_createdSecrets}");
            if (_updatedSecrets > 0) stats.Add($"Updated: {_updatedSecrets}");
            if (_skippedSecrets > 0) stats.Add($"Skipped: {_skippedSecrets}");
            if (_failedSecrets > 0) stats.Add($"Failed: {_failedSecrets}");
            
            if (stats.Any())
            {
                sb.AppendLine($"   Stats: {string.Join(", ", stats)}");
            }
        }
        
        sb.AppendLine(); // Separator
        
        // Item details (show most recent items, limit to fit screen)
        var items = _items.Values
            .OrderByDescending(x => x.UpdateTime)
            .Take(15) // Limit to 15 most recent items
            .ToList();
            
        if (items.Any())
        {
            foreach (var item in items)
            {
                var icon = GetStatusIcon(item);
                var name = TruncateText(item.Name, 40);
                var status = TruncateText(item.Status, 25);
                
                sb.AppendLine($"  {icon} {name,-40} {status}");
                
                if (!string.IsNullOrEmpty(item.Details))
                {
                    var details = TruncateText(item.Details, 60);
                    sb.AppendLine($"     └─ {details}");
                }
            }
        }
        else
        {
            sb.AppendLine("  Initializing...");
        }
        
        return sb.ToString().TrimEnd();
    }

    private string CreateProgressBar(double percentage, int width)
    {
        var filled = (int)(percentage / 100.0 * width);
        var empty = width - filled;
        
        return $"[{new string('█', filled)}{new string('░', empty)}]";
    }

    private string GetStatusIcon(SyncItemStatus item)
    {
        if (item.Outcome.HasValue)
        {
            return GetOutcomeIcon(item.Outcome.Value);
        }
        
        // Default spinner for in-progress items
        var chars = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        var index = (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 150) % chars.Length;
        return chars[index];
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

    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
    }

    private void RenderUpdate(string content)
    {
        try
        {
            // Get current cursor position to be safer
            var currentTop = Console.CursorTop;
            var newLineCount = content.Split('\n').Length;
            
            // Only clear if we have previously rendered content and cursor position makes sense
            if (_lastRenderedLines > 0 && currentTop >= _lastRenderedLines)
            {
                // Move cursor to start of our display area
                Console.SetCursorPosition(0, currentTop - _lastRenderedLines);
                
                // Clear only our previous content, line by line
                for (int i = 0; i < _lastRenderedLines; i++)
                {
                    // Get actual console width, with safety bounds
                    var width = Math.Max(20, Math.Min(Console.WindowWidth - 1, 120));
                    Console.Write(new string(' ', width));
                    
                    // Only add newline if not on last line to avoid scrolling
                    if (i < _lastRenderedLines - 1)
                    {
                        Console.WriteLine();
                    }
                }
                
                // Move cursor back to start position
                Console.SetCursorPosition(0, currentTop - _lastRenderedLines);
            }
            
            // Write new content
            Console.Write(content);
            
            // Update line count for next render
            _lastRenderedLines = newLineCount;
            
            // Ensure cursor is positioned correctly
            if (!content.EndsWith('\n'))
            {
                Console.WriteLine();
                _lastRenderedLines++;
            }
        }
        catch (Exception ex)
        {
            // If console manipulation fails, fall back to simple mode
            _logger?.LogDebug(ex, "Console manipulation failed, switching to simple mode");
            _supportsAdvancedConsole = false;
            
            // Just write the content normally
            Console.WriteLine(content);
        }
    }

    private void ClearDisplay()
    {
        try
        {
            if (_lastRenderedLines > 0)
            {
                Console.SetCursorPosition(0, Console.CursorTop - _lastRenderedLines);
                for (int i = 0; i < _lastRenderedLines; i++)
                {
                    var width = Math.Min(Console.WindowWidth - 1, 120);
                    Console.Write(new string(' ', width));
                    Console.WriteLine();
                }
                Console.SetCursorPosition(0, Console.CursorTop - _lastRenderedLines);
            }
        }
        catch {         }
    }

    private static bool IsRunningInDebugConsole()
    {
        // Check for common debug environment indicators
        try
        {
            // VS Code Debug Console often has limited window width
            if (Console.WindowWidth <= 1 || Console.WindowHeight <= 1)
                return true;
                
            // Check for debug-specific environment variables
            var debugVars = new[] { 
                "VSCODE_INSPECTOR_OPTIONS", 
                "DOTNET_RUNNING_IN_CONTAINER",
                "VSCODE_PID",           // VS Code process
                "TERM_PROGRAM",         // Terminal program identifier
                "VSCODE_CWD",           // VS Code working directory
                "VSCODE_GIT_ASKPASS_NODE",
                "VSCODE_GIT_ASKPASS_EXTRA_ARGS"
            };
            foreach (var debugVar in debugVars)
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(debugVar)))
                    return true;
            }
            
            // Check if we're in a debugger
            if (System.Diagnostics.Debugger.IsAttached)
                return true;
                
            // Check if output is redirected (common in debug consoles)
            if (Console.IsOutputRedirected || Console.IsErrorRedirected)
                return true;
                
            // Check for small console window (typical of debug consoles)
            if (Console.WindowWidth < 80 || Console.WindowHeight < 20)
                return true;
                
            return false;
        }
        catch
        {
            return true; // If we can't determine, assume we're in a limited environment
        }
    }

    private static bool IsRunningInCI()
    {
        // Check for common CI environment variables
        var ciVars = new[] 
        { 
            "CI", "CONTINUOUS_INTEGRATION", "GITHUB_ACTIONS", 
            "AZURE_PIPELINES", "JENKINS_URL", "GITLAB_CI",
            "TEAMCITY_VERSION", "BAMBOO_BUILD_NUMBER"
        };
        
        return ciVars.Any(var => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(var)));
    }

    private static string GetDebugEnvironmentReason()
    {
        if (System.Diagnostics.Debugger.IsAttached)
            return "Debugger attached";
            
        if (Console.IsOutputRedirected)
            return "Output redirected";
            
        if (IsRunningInCI())
            return "CI environment detected";
            
        try
        {
            if (Console.WindowWidth <= 1 || Console.WindowHeight <= 1)
                return "Limited console";
        }
        catch { }
        
        return "Simple mode";
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_lock)
        {
            _disposed = true;
            _isActive = false;
            _updateTimer?.Dispose();
            
            if (_supportsAdvancedConsole)
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Console.CursorVisible = true;
                    }
                }
                catch { }
            }
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
