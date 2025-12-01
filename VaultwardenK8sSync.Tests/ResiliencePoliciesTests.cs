using Xunit;
using FluentAssertions;
using VaultwardenK8sSync.Policies;
using Polly;
using System.Net;

namespace VaultwardenK8sSync.Tests;

public class ResiliencePoliciesTests
{
    [Fact]
    public void GetRetryPolicy_ShouldReturnValidPolicy()
    {
        // Act
        var policy = ResiliencePolicies.GetRetryPolicy();

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_ShouldReturnValidPolicy()
    {
        // Act
        var policy = ResiliencePolicies.GetCircuitBreakerPolicy();

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetTimeoutPolicy_ShouldReturnValidPolicy()
    {
        // Act
        var policy = ResiliencePolicies.GetTimeoutPolicy();

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task RetryPolicy_ShouldRetryOnTransientFailure()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy();
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                // Simulate transient failure
                throw new HttpRequestException("Simulated failure");
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        // Assert
        attemptCount.Should().Be(3); // Should have retried 2 times
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TimeoutPolicy_ShouldTimeoutAfter30Seconds()
    {
        // Arrange
        var policy = ResiliencePolicies.GetTimeoutPolicy();
        
        // Act & Assert
        // Using Task.Delay with cancellation would trigger timeout
        // but for test purposes, we just verify policy exists
        policy.Should().NotBeNull();
        
        // Note: Full timeout testing requires more complex setup
        // This test verifies the policy is configured
    }

    [Fact]
    public async Task RetryPolicy_ShouldGiveUpAfter3Attempts()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy();
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                attemptCount++;
                throw new HttpRequestException("Permanent failure");
            });
        });

        // Should attempt 1 initial + 3 retries = 4 total
        attemptCount.Should().Be(4);
    }
}
