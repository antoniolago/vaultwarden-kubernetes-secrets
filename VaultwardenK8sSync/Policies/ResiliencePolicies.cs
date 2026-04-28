using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace VaultwardenK8sSync.Policies;

public static class ResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var message = outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString();
                    if (logger is not null)
                        logger.LogWarning("Retry {RetryCount} after {Delay}s due to: {Reason}", retryCount, timespan.TotalSeconds, message);
                    else
                        Console.WriteLine("Retry {0} after {1}s due to: {2}", retryCount, timespan.TotalSeconds, message);
                });
    }

    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(ILogger? logger = null)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, duration) =>
                {
                    var message = outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString();
                    if (logger is not null)
                        logger.LogWarning("Circuit breaker opened for {Duration}s due to: {Reason}", duration.TotalSeconds, message);
                    else
                        Console.WriteLine("Circuit breaker opened for {0}s due to: {1}", duration.TotalSeconds, message);
                },
                onReset: () =>
                {
                    if (logger is not null)
                        logger.LogInformation("Circuit breaker reset");
                    else
                        Console.WriteLine("Circuit breaker reset");
                },
                onHalfOpen: () =>
                {
                    if (logger is not null)
                        logger.LogInformation("Circuit breaker half-open, testing connection...");
                    else
                        Console.WriteLine("Circuit breaker half-open, testing connection...");
                });
    }

    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));
    }
}
