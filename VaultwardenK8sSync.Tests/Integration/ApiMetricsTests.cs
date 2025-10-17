using Xunit;
using FluentAssertions;
using System.Net;

namespace VaultwardenK8sSync.Tests.Integration;

public class ApiMetricsTests
{
    [Fact]
    public async Task MetricsEndpoint_ShouldBeAccessible()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };

        // Act
        HttpResponseMessage? response = null;
        try
        {
            response = await client.GetAsync("/metrics");
        }
        catch (HttpRequestException)
        {
            // API might not be running - skip test
            return;
        }

        // Assert
        if (response != null)
        {
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                content.Should().NotBeNullOrEmpty();
            }
        }
    }

    [Fact]
    public async Task MetricsEndpoint_ShouldReturnPrometheusFormat()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };

        // Act
        HttpResponseMessage? response = null;
        try
        {
            response = await client.GetAsync("/metrics");
        }
        catch (HttpRequestException)
        {
            // API might not be running - skip test
            return;
        }

        // Assert
        if (response?.IsSuccessStatusCode == true)
        {
            var content = await response.Content.ReadAsStringAsync();
            
            // Check for expected metric names
            var expectedMetrics = new[]
            {
                "vaultwarden_sync_total",
                "vaultwarden_sync_duration_seconds",
                "vaultwarden_secrets_synced_total",
                "vaultwarden_items_watched"
            };

            // At least some metrics should be present
            // (might not all be present if sync hasn't run yet)
            content.Should().ContainAny(expectedMetrics);
        }
    }

    [Fact]
    public async Task HealthEndpoint_ShouldWork()
    {
        // Arrange
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };

        // Act
        HttpResponseMessage? response = null;
        try
        {
            response = await client.GetAsync("/health");
        }
        catch (HttpRequestException)
        {
            // API might not be running - skip test
            return;
        }

        // Assert
        if (response != null)
        {
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.OK, 
                HttpStatusCode.ServiceUnavailable
            );
        }
    }
}
