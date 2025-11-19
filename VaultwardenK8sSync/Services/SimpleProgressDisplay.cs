using System.Text;

namespace VaultwardenK8sSync.Services;

/// <summary>
/// A simple, reliable progress display that works in all console environments
/// </summary>
public class SimpleProgressDisplay : IDisposable
{
    private readonly object _lock = new();
    private bool _isActive = false;
    private string _currentOperation = "";
    private DateTime _startTime;
    private bool _disposed = false;

    public void Start(string message)
    {
        lock (_lock)
        {
            _isActive = true;
            _currentOperation = CleanMessage(message);
            _startTime = DateTime.UtcNow;
            Console.Write($"{_currentOperation}");
        }
    }

    public void Update(string message)
    {
        lock (_lock)
        {
            if (_isActive)
            {
                _currentOperation = CleanMessage(message);
                // For updates, just print dots to show progress without spam
                Console.Write(".");
            }
        }
    }

    public void Complete(string? finalMessage = null)
    {
        lock (_lock)
        {
            if (_isActive)
            {
                _isActive = false;
                var duration = DateTime.UtcNow - _startTime;
                
                Console.WriteLine(); // New line to complete the current line
                
                if (!string.IsNullOrEmpty(finalMessage))
                {
                    var cleanFinal = CleanMessage(finalMessage);
                    var durationText = duration.TotalSeconds > 1 ? $" ({duration.TotalSeconds:F1}s)" : "";
                    Console.WriteLine($"{cleanFinal}{durationText}");
                }
            }
        }
    }

    private static string CleanMessage(string message)
    {
        // Remove problematic Unicode characters that might not render properly
        return message
            .Replace("⠋", "")
            .Replace("⠙", "")
            .Replace("⠹", "")
            .Replace("⠸", "")
            .Replace("⠼", "")
            .Replace("⠴", "")
            .Replace("⠦", "")
            .Replace("⠧", "")
            .Replace("⠇", "")
            .Replace("⠏", "")
            .Replace("⠿", "•")
            .Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_lock)
        {
            _disposed = true;
            if (_isActive)
            {
                Console.WriteLine(); // Ensure we end on a new line
                _isActive = false;
            }
        }
    }
}

/// <summary>
/// An even simpler progress display that just shows start/end messages
/// </summary>
public class StaticProgressDisplay : IDisposable
{
    private readonly object _lock = new();
    private bool _isActive = false;
    private DateTime _startTime;
    private string _operation = "";
    private bool _disposed = false;
    private readonly IValkeySyncOutputPublisher? _valkeyPublisher;

    public StaticProgressDisplay(IValkeySyncOutputPublisher? valkeyPublisher = null)
    {
        _valkeyPublisher = valkeyPublisher;
    }

    public void Start(string message)
    {
        lock (_lock)
        {
            _isActive = true;
            _operation = message;
            _startTime = DateTime.UtcNow;
            
            // Clean the message of spinner characters
            var cleanMessage = message
                .Replace("🔄", "")
                .Replace("🔐", "")
                .Replace("⠋", "")
                .Replace("⠙", "")
                .Replace("⠹", "")
                .Replace("⠸", "")
                .Replace("⠼", "")
                .Replace("⠴", "")
                .Replace("⠦", "")
                .Replace("⠧", "")
                .Replace("⠇", "")
                .Replace("⠏", "")
                .Trim();
                
            Console.WriteLine($"🔄 {cleanMessage}");
            _valkeyPublisher?.PublishAsync($"[{DateTime.UtcNow:HH:mm:ss}] 🔄 {cleanMessage}");
        }
    }

    public void Update(string message)
    {
        // For static display, we don't spam updates
        // This prevents the infinite logging issue
    }

    public void Complete(string? finalMessage = null)
    {
        lock (_lock)
        {
            if (_isActive)
            {
                _isActive = false;
                var duration = DateTime.UtcNow - _startTime;
                
                if (!string.IsNullOrEmpty(finalMessage))
                {
                    var durationText = duration.TotalSeconds > 0.5 ? $" ({duration.TotalSeconds:F1}s)" : "";
                    var fullMessage = $"{finalMessage}{durationText}";
                    Console.WriteLine(fullMessage);
                    _valkeyPublisher?.PublishAsync($"[{DateTime.UtcNow:HH:mm:ss}] {fullMessage}");
                }
                else
                {
                    var durationText = duration.TotalSeconds > 0.5 ? $" ({duration.TotalSeconds:F1}s)" : "";
                    var fullMessage = $"✅ Completed{durationText}";
                    Console.WriteLine(fullMessage);
                    _valkeyPublisher?.PublishAsync($"[{DateTime.UtcNow:HH:mm:ss}] {fullMessage}");
                }
            }
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


