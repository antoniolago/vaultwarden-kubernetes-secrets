using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Net.WebSockets;
using System.Text;

namespace VaultwardenK8sSync.Api.Controllers;

[ApiController]
[Route("api/sync-output")]
public class SyncOutputController : ControllerBase
{
    private readonly ILogger<SyncOutputController> _logger;
    private readonly IConnectionMultiplexer? _redis;
    private readonly bool _redisEnabled;

    public SyncOutputController(ILogger<SyncOutputController> logger)
    {
        _logger = logger;
        
        var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") 
            ?? Environment.GetEnvironmentVariable("REDIS__CONNECTION");
        
        if (!string.IsNullOrEmpty(redisConnection))
        {
            try
            {
                _redis = ConnectionMultiplexer.Connect(redisConnection);
                _redisEnabled = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to Redis for sync output streaming");
                _redisEnabled = false;
            }
        }
    }

    [HttpGet("stream")]
    public async Task Stream()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        _logger.LogInformation("WebSocket connection established for sync output streaming");

        try
        {
            // Check Redis availability after accepting the connection
            if (!_redisEnabled || _redis == null)
            {
                var errorMsg = "__REDIS_NOT_CONFIGURED__\n" +
                    "⚠️ Redis is not configured. Real-time sync output is unavailable.\n" +
                    "Configure REDIS_CONNECTION environment variable to enable this feature.\n" +
                    "See REDIS_SYNC_OUTPUT.md for setup instructions.";
                
                var errorBytes = Encoding.UTF8.GetBytes(errorMsg);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(errorBytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
                
                _logger.LogWarning("WebSocket client connected but Redis is not configured");
                
                // Close connection gracefully after sending error
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    "Redis not configured",
                    CancellationToken.None);
                return;
            }

            var subscriber = _redis.GetSubscriber();
            var channel = "sync:output";

            // Send history first
            var db = _redis.GetDatabase();
            var history = await db.ListRangeAsync($"{channel}:history", 0, -1);
            
            foreach (var item in history)
            {
                if (webSocket.State != WebSocketState.Open) break;
                
                var bytes = Encoding.UTF8.GetBytes(item.ToString() + "\n");
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }

            // Subscribe to real-time updates
            var messageQueue = System.Threading.Channels.Channel.CreateUnbounded<string>();
            
            await subscriber.SubscribeAsync(channel, (ch, message) =>
            {
                messageQueue.Writer.TryWrite(message.ToString());
            });

            // Stream messages to WebSocket
            await foreach (var message in messageQueue.Reader.ReadAllAsync())
            {
                if (webSocket.State != WebSocketState.Open) break;

                var bytes = Encoding.UTF8.GetBytes(message + "\n");
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }

            await subscriber.UnsubscribeAsync(channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in sync output WebSocket stream");
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    CancellationToken.None);
            }
            _logger.LogInformation("WebSocket connection closed for sync output streaming");
        }
    }
}
