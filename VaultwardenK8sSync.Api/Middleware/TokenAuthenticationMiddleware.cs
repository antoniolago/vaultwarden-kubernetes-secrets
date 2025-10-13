namespace VaultwardenK8sSync.Database;

public class AuthenticationConfig
{
    public string Token { get; set; } = string.Empty;
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

        // Skip auth if no token is configured
        if (string.IsNullOrEmpty(_config.Token))
        {
            await next(context);
            return;
        }

        // Check for token in Authorization header
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing authorization header" });
            return;
        }

        var token = authHeader.ToString().Replace("Bearer ", "");
        if (token != _config.Token)
        {
            _logger.LogWarning("Invalid token attempt from {IP}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid token" });
            return;
        }

        await next(context);
    }
}
