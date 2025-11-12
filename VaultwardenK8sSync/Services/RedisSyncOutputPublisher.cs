using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace VaultwardenK8sSync.Services;

public interface IRedisSyncOutputPublisher
{
    Task PublishAsync(string message);
    Task ClearAsync();
}

public class RedisSyncOutputPublisher : IRedisSyncOutputPublisher, IDisposable
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisSyncOutputPublisher> _logger;
    private readonly string _channel = "sync:output";
    private readonly bool _enabled;

    public RedisSyncOutputPublisher(ILogger<RedisSyncOutputPublisher> logger)
    {
        _logger = logger;
        
        // Valkey is a Redis-compatible fork (https://valkey.io)
        var connectionString = Environment.GetEnvironmentVariable("VALKEY_CONNECTION");
        
        if (!string.IsNullOrEmpty(connectionString))
        {
            try
            {
                _redis = ConnectionMultiplexer.Connect(connectionString);
                _enabled = true;
                _logger.LogInformation("Valkey/Redis connection established for sync output publishing");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to Valkey/Redis. Sync output publishing disabled.");
                _enabled = false;
            }
        }
        else
        {
            _logger.LogInformation("Valkey/Redis not configured. Sync output publishing disabled.");
            _enabled = false;
        }
    }

    public async Task PublishAsync(string message)
    {
        if (!_enabled || _redis == null) return;

        try
        {
            var db = _redis.GetDatabase();
            var subscriber = _redis.GetSubscriber();
            
            // Publish to channel for real-time streaming
            await subscriber.PublishAsync(_channel, message);
            
            // Also append to a list for history (keep last 1000 lines)
            await db.ListRightPushAsync($"{_channel}:history", message);
            await db.ListTrimAsync($"{_channel}:history", -1000, -1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish sync output to Redis");
        }
    }

    public async Task ClearAsync()
    {
        if (!_enabled || _redis == null) return;

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync($"{_channel}:history");
            
            // Publish clear signal
            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(_channel, "__CLEAR__");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear sync output history");
        }
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
