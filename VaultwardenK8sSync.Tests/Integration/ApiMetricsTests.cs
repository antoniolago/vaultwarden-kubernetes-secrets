using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using FluentAssertions;
using System.Net;

namespace VaultwardenK8sSync.Tests.Integration;

public class ApiMetricsTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly WebApplicationFactory<global::Program> _factory;

    public ApiMetricsTests(WebApplicationFactory<global::Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MetricsEndpoint_ShouldBeAccessible()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/metrics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MetricsEndpoint_ShouldReturnPrometheusFormat()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/metrics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();

        // Check for expected metric names
        var expectedMetrics = new[]
        {
            "vaultwarden_sync_total",
            "vaultwarden_sync_duration_seconds",
            "vaultwarden_secrets_synced_total",
            "vaultwarden_items_watched"
        };

        content.Should().ContainAny(expectedMetrics);
    }

    [Fact]
    public async Task HealthEndpoint_ShouldWork()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.ServiceUnavailable
        );
    }
}
