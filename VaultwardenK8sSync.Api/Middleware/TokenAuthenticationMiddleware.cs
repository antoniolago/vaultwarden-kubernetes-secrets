using System.Security.Cryptography;
using System.Text;

namespace VaultwardenK8sSync.Database;

public class AuthenticationConfig
{
    public string Token { get; set; } = string.Empty;
    public bool LoginlessMode { get; set; } = false;
}

public class TokenAuthenticationMiddleware : IMiddleware
{
    private readonly AuthenticationConfig _config;
    private readonly ILogger<TokenAuthenticationMiddleware> _logger;

    public TokenAuthenticationMiddleware(AuthenticationConfig config, ILogger<TokenAuthenticationMiddleware> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Skip auth for health checks
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        // Skip auth if loginless mode is enabled
        if (_config.LoginlessMode)
        {
            _logger.LogDebug("Loginless mode enabled - skipping authentication");
            await next(context);
            return;
        }

        // Skip auth if no token is configured
        if (string.IsNullOrEmpty(_config.Token))
        {
            await next(context);
            return;
        }

        // Check for token in Authorization header
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            _logger.LogWarning("Authentication failed - missing header from {IP}", context.Connection.RemoteIpAddress);
            await Task.Delay(10); // Constant delay to prevent timing attacks
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication failed" });
            return;
        }

        var token = authHeader.ToString().Replace("Bearer ", "");
        
        // Use constant-time comparison to prevent timing attacks
        if (!SecureCompare(token, _config.Token))
        {
            _logger.LogWarning("Authentication failed - invalid token from {IP}", context.Connection.RemoteIpAddress);
            await Task.Delay(10); // Constant delay to prevent timing attacks
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication failed" });
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks.
    /// Uses CryptographicOperations.FixedTimeEquals for secure token comparison.
    /// </summary>
    private static bool SecureCompare(string provided, string expected)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
            return false;

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        // Constant-time comparison - prevents timing attacks
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
