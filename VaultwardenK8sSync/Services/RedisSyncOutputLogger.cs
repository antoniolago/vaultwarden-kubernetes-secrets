using Microsoft.Extensions.Logging;

namespace VaultwardenK8sSync.Services;

public class RedisSyncOutputLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IRedisSyncOutputPublisher _publisher;

    public RedisSyncOutputLogger(string categoryName, IRedisSyncOutputPublisher publisher)
    {
        _categoryName = categoryName;
        _publisher = publisher;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message)) return;

        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        var level = logLevel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => "INFO"
        };

        var formattedMessage = $"[{timestamp}] [{level}] {message}";
        
        if (exception != null)
        {
            formattedMessage += $"\n{exception}";
        }

        // Fire and forget - don't wait for Redis
        _ = _publisher.PublishAsync(formattedMessage);
    }
}

public class RedisSyncOutputLoggerProvider : ILoggerProvider
{
    private readonly IRedisSyncOutputPublisher _publisher;

    public RedisSyncOutputLoggerProvider(IRedisSyncOutputPublisher publisher)
    {
        _publisher = publisher;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new RedisSyncOutputLogger(categoryName, _publisher);
    }

    public void Dispose() { }
}
