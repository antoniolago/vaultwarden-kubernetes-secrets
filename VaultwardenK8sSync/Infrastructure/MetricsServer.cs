using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Prometheus;
using System.Text.Json;
using VaultwardenK8sSync.HealthChecks;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.Infrastructure;

public class MetricsServer : IDisposable
{
    private readonly WebApplication _app;
    private readonly ILogger<MetricsServer> _logger;
    private readonly int _port;
    private readonly WebhookSettings _webhookSettings;
    private Task? _runTask;

    public MetricsServer(
        ILogger<MetricsServer> logger,
        IVaultwardenService vaultwardenService,
        IKubernetesService kubernetesService,
        IMetricsService metricsService,
        IWebhookService? webhookService,
        WebhookSettings webhookSettings,
        int port = 9090)
    {
        _logger = logger;
        _port = port;
        _webhookSettings = webhookSettings;

        var builder = WebApplication.CreateBuilder();
        
        // Configure services
        builder.Services.AddSingleton(vaultwardenService);
        builder.Services.AddSingleton(kubernetesService);
        builder.Services.AddSingleton(metricsService);
        if (webhookService != null)
        {
            builder.Services.AddSingleton(webhookService);
        }
        
        // Add health checks
        builder.Services.AddHealthChecks()
            .AddCheck<VaultwardenHealthCheck>("vaultwarden", tags: new[] { "ready" })
            .AddCheck<KubernetesHealthCheck>("kubernetes", tags: new[] { "ready" })
            .AddCheck<SyncHealthCheck>("sync", tags: new[] { "ready" })
            .ForwardToPrometheus();

        _app = builder.Build();

        // Configure endpoints
        ConfigureEndpoints();
    }

    private void ConfigureEndpoints()
    {
        // Metrics endpoint
        _app.MapMetrics("/metrics");

        // Liveness probe - always returns healthy if the process is running
        _app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

        // Readiness probe - checks if all dependencies are available
        _app.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthCheckResponse
        });

        // Startup probe - checks if initial sync is complete
        _app.MapHealthChecks("/startupz", new HealthCheckOptions
        {
            Predicate = check => check.Name == "sync",
            ResponseWriter = WriteHealthCheckResponse
        });

        // Detailed health endpoint
        _app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteDetailedHealthCheckResponse
        });

        // Webhook endpoint (if enabled)
        if (_webhookSettings.Enabled)
        {
            _app.MapPost(_webhookSettings.Path, HandleWebhookAsync);
            _logger.LogInformation("Webhook endpoint enabled at {Path}", _webhookSettings.Path);
        }
    }

    private async Task<IResult> HandleWebhookAsync(HttpContext context)
    {
        try
        {
            // Read the request body
            using var reader = new StreamReader(context.Request.Body);
            var payload = await reader.ReadToEndAsync();

            // Get webhook service
            var webhookService = context.RequestServices.GetService<IWebhookService>();
            if (webhookService == null)
            {
                _logger.LogError("Webhook service not available");
                return Results.StatusCode(500);
            }

            // Validate signature if required
            if (_webhookSettings.RequireSignature)
            {
                var signature = context.Request.Headers["X-Webhook-Signature"].FirstOrDefault()
                    ?? context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

                if (!webhookService.ValidateSignature(payload, signature ?? ""))
                {
                    _logger.LogWarning("Webhook signature validation failed");
                    return Results.Unauthorized();
                }
            }

            // Parse webhook event
            var webhookEvent = JsonSerializer.Deserialize<WebhookEvent>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (webhookEvent == null)
            {
                _logger.LogWarning("Failed to parse webhook event");
                return Results.BadRequest(new { error = "Invalid webhook payload" });
            }

            // Process webhook asynchronously (don't wait for completion)
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await webhookService.ProcessWebhookAsync(webhookEvent);
                    _logger.LogInformation(
                        "Webhook processed: EventType={EventType}, Success={Success}, Duration={Duration}ms",
                        webhookEvent.EventType,
                        result.Success,
                        result.ProcessingDuration.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing webhook in background");
                }
            });

            // Return immediately to Vaultwarden
            return Results.Ok(new { status = "accepted", message = "Webhook received and queued for processing" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling webhook request");
            return Results.StatusCode(500);
        }
    }

    private static Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        
        var result = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds
        };

        return context.Response.WriteAsJsonAsync(result);
    }

    private static Task WriteDetailedHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var result = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds,
                exception = e.Value.Exception?.Message,
                data = e.Value.Data
            })
        };

        return context.Response.WriteAsJsonAsync(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting metrics server on port {Port}", _port);
        
        try
        {
            _runTask = _app.RunAsync($"http://0.0.0.0:{_port}");
            
            // Give it a moment to start
            await Task.Delay(500, cancellationToken);
            
            _logger.LogInformation("Metrics server started successfully");
            _logger.LogInformation("  - Metrics: http://localhost:{Port}/metrics", _port);
            _logger.LogInformation("  - Health: http://localhost:{Port}/health", _port);
            _logger.LogInformation("  - Liveness: http://localhost:{Port}/healthz", _port);
            _logger.LogInformation("  - Readiness: http://localhost:{Port}/readyz", _port);
            _logger.LogInformation("  - Startup: http://localhost:{Port}/startupz", _port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start metrics server");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping metrics server...");
        
        try
        {
            await _app.StopAsync(cancellationToken);
            
            if (_runTask != null)
            {
                await _runTask;
            }
            
            _logger.LogInformation("Metrics server stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping metrics server");
        }
    }

    public void Dispose()
    {
        _app?.DisposeAsync().AsTask().Wait();
    }
}
