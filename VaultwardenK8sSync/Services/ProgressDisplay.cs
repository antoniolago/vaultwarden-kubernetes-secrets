using System.Text;

namespace VaultwardenK8sSync.Services;

public class ProgressDisplay : IDisposable
{
    private readonly object _lock = new();
    private bool _isActive = false;
    private string _currentLine = "";
    private readonly Timer? _spinnerTimer;
    private int _spinnerIndex = 0;
    private readonly string[] _spinnerChars = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private bool _disposed = false;
    private bool _supportsAdvancedConsole = true;

    public ProgressDisplay()
    {
        // Test if console supports advanced features
        try
        {
            var originalCursor = Console.CursorVisible;
            Console.CursorVisible = false;
            Console.CursorVisible = originalCursor;
            _supportsAdvancedConsole = true;
        }
        catch
        {
            _supportsAdvancedConsole = false;
        }
        
        // Only start spinner if console supports it
        if (_supportsAdvancedConsole)
        {
            try { Console.CursorVisible = false; } catch { }
            _spinnerTimer = new Timer(UpdateSpinner, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(150));
        }
    }

    public void Start(string message)
    {
        lock (_lock)
        {
            _isActive = true;
            _currentLine = message;
            UpdateDisplay();
        }
    }

    public void Update(string message)
    {
        lock (_lock)
        {
            if (_isActive)
            {
                _currentLine = message;
                UpdateDisplay();
            }
        }
    }

    public void Complete(string finalMessage = "")
    {
        lock (_lock)
        {
            if (_isActive)
            {
                _isActive = false;
                
                if (_supportsAdvancedConsole)
                {
                    ClearCurrentLine();
                }
                
                if (!string.IsNullOrEmpty(finalMessage))
                {
                    Console.WriteLine(finalMessage);
                }
            }
        }
    }

    private void UpdateSpinner(object? state)
    {
        lock (_lock)
        {
            if (_isActive && !_disposed)
            {
                _spinnerIndex = (_spinnerIndex + 1) % _spinnerChars.Length;
                UpdateDisplay();
            }
        }
    }

    private void UpdateDisplay()
    {
        if (_disposed || !_isActive) return;
        
        try
        {
            if (_supportsAdvancedConsole)
            {
                ClearCurrentLine();
                var spinner = _isActive ? _spinnerChars[_spinnerIndex] : "✓";
                var displayText = $"{spinner} {_currentLine}";
                Console.Write(displayText);
            }
            else
            {
                // Fallback to simple logging for unsupported consoles
                // Don't spam - only update every few seconds
                if (_spinnerIndex % 20 == 0) // Update every 3 seconds (150ms * 20)
                {
                    Console.WriteLine($"⠿ {_currentLine}");
                }
            }
        }
        catch
        {
            // Disable advanced console if errors occur
            _supportsAdvancedConsole = false;
        }
    }

    private void ClearCurrentLine()
    {
        if (!_supportsAdvancedConsole) return;
        
        try
        {
            Console.Write("\r");
            // Try to get console width, fallback to reasonable default
            int width;
            try
            {
                width = Console.WindowWidth - 1;
            }
            catch
            {
                width = 100;
            }
            Console.Write(new string(' ', width));
            Console.Write("\r");
        }
        catch
        {
            _supportsAdvancedConsole = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_lock)
        {
            _disposed = true;
            _isActive = false;
            _spinnerTimer?.Dispose();
            
            if (_supportsAdvancedConsole)
            {
                try
                {
                    ClearCurrentLine();
                    Console.CursorVisible = true;
                }
                catch { }
            }
        }
    }
}

public class MultiProgressDisplay : IDisposable
{
    private readonly List<ProgressItem> _items = new();
    private readonly object _lock = new();
    private readonly Timer? _updateTimer;
    private bool _disposed = false;
    private int _lastRenderedLines = 0;

    public MultiProgressDisplay()
    {
        try { Console.CursorVisible = false; } catch { }
        _updateTimer = new Timer(UpdateDisplay, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    public ProgressItem AddItem(string id, string initialMessage)
    {
        lock (_lock)
        {
            var item = new ProgressItem(id, initialMessage);
            _items.Add(item);
            return item;
        }
    }

    public void Complete(string? finalSummary = null)
    {
        lock (_lock)
        {
            if (_disposed) return;
            
            // Clear the dynamic display
            ClearDisplay();
            
            // Show final summary if provided
            if (!string.IsNullOrEmpty(finalSummary))
            {
                Console.WriteLine(finalSummary);
            }
        }
    }

    private void UpdateDisplay(object? state)
    {
        lock (_lock)
        {
            if (_disposed) return;
            
            var sb = new StringBuilder();
            
            foreach (var item in _items)
            {
                var icon = item.IsComplete ? "✓" : GetSpinnerChar();
                var status = item.IsComplete ? (item.IsSuccess ? "Done" : "Failed") : "Running";
                var color = item.IsComplete ? (item.IsSuccess ? "✓" : "❌") : "⠿";
                
                sb.AppendLine($"{color} {item.Message} ({status})");
                
                if (!string.IsNullOrEmpty(item.Details))
                {
                    sb.AppendLine($"   └─ {item.Details}");
                }
            }
            
            var content = sb.ToString().TrimEnd();
            if (!string.IsNullOrEmpty(content))
            {
                RenderUpdate(content);
            }
        }
    }

    private void RenderUpdate(string content)
    {
        try
        {
            // Move cursor to start of our display area
            if (_lastRenderedLines > 0)
            {
                Console.SetCursorPosition(0, Console.CursorTop - _lastRenderedLines);
            }
            
            // Clear previous content
            for (int i = 0; i < _lastRenderedLines; i++)
            {
                Console.Write(new string(' ', Console.WindowWidth - 1));
                Console.WriteLine();
            }
            
            // Move cursor back to start
            if (_lastRenderedLines > 0)
            {
                Console.SetCursorPosition(0, Console.CursorTop - _lastRenderedLines);
            }
            
            // Write new content
            Console.Write(content);
            
            // Count lines for next update
            _lastRenderedLines = content.Split('\n').Length;
        }
        catch
        {
            // Fallback to simple output if console manipulation fails
            Console.WriteLine(content);
        }
    }

    private string GetSpinnerChar()
    {
        var chars = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        var index = (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 100) % chars.Length;
        return chars[index];
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
                    Console.Write(new string(' ', Console.WindowWidth - 1));
                    Console.WriteLine();
                }
                Console.SetCursorPosition(0, Console.CursorTop - _lastRenderedLines);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_lock)
        {
            _disposed = true;
            _updateTimer?.Dispose();
            
            try
            {
                Console.CursorVisible = true;
            }
            catch { }
        }
    }
}

public class ProgressItem
{
    public string Id { get; }
    public string Message { get; set; }
    public string Details { get; set; } = "";
    public bool IsComplete { get; private set; }
    public bool IsSuccess { get; private set; }

    public ProgressItem(string id, string message)
    {
        Id = id;
        Message = message;
    }

    public void Update(string message, string details = "")
    {
        Message = message;
        Details = details;
    }

    public void Complete(bool success = true, string? finalMessage = null, string? finalDetails = null)
    {
        IsComplete = true;
        IsSuccess = success;
        
        if (finalMessage != null)
            Message = finalMessage;
            
        if (finalDetails != null)
            Details = finalDetails;
    }
}
