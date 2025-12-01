using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace VaultwardenK8sSync.Services;

public interface IValkeySyncOutputPublisher
{
    Task PublishAsync(string message);
    Task ClearAsync();
}

public class ValkeySyncOutputPublisher : IValkeySyncOutputPublisher, IDisposable
{
    private readonly IConnectionMultiplexer? _valkey;
    private readonly ILogger<ValkeySyncOutputPublisher> _logger;
    private readonly string _channel = "sync:output";
    private readonly bool _enabled;

    public ValkeySyncOutputPublisher(ILogger<ValkeySyncOutputPublisher> logger)
    {
        _logger = logger;
        
        // Valkey is a Redis-compatible fork (https://valkey.io)
        var connectionString = Environment.GetEnvironmentVariable("VALKEY_CONNECTION");
        
        if (!string.IsNullOrEmpty(connectionString))
        {
            try
            {
                _valkey = ConnectionMultiplexer.Connect(connectionString);
                _enabled = true;
                _logger.LogInformation("Valkey connection established for sync output publishing");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to Valkey. Sync output publishing disabled.");
                _enabled = false;
            }
        }
        else
        {
            _logger.LogInformation("Valkey not configured. Sync output publishing disabled.");
            _enabled = false;
        }
    }

    public async Task PublishAsync(string message)
    {
        if (!_enabled || _valkey == null) return;

        try
        {
            var db = _valkey.GetDatabase();
            var subscriber = _valkey.GetSubscriber();
            
            // Publish to channel for real-time streaming
            await subscriber.PublishAsync(RedisChannel.Literal(_channel), message);
            
            // Also append to a list for history (keep last 1000 lines)
            await db.ListRightPushAsync($"{_channel}:history", message);
            await db.ListTrimAsync($"{_channel}:history", -1000, -1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish sync output to Valkey");
        }
    }

    public async Task ClearAsync()
    {
        if (!_enabled || _valkey == null) return;

        try
        {
            var db = _valkey.GetDatabase();
            await db.KeyDeleteAsync($"{_channel}:history");
            
            // Publish clear signal
            var subscriber = _valkey.GetSubscriber();
            await subscriber.PublishAsync(RedisChannel.Literal(_channel), "__CLEAR__");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear sync output history");
        }
    }

    public void Dispose()
    {
        _valkey?.Dispose();
    }
}

