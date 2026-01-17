using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using Xunit;
using FluentAssertions;

namespace VaultwardenK8sSync.Tests.Integration;

public class ApiAuthenticationIntegrationTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly WebApplicationFactory<global::Program> _factory;

    public ApiAuthenticationIntegrationTests(WebApplicationFactory<global::Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_WithoutAuthentication_ShouldReturn200()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiEndpoint_WithoutAuthentication_WhenNoTokenConfigured_ShouldSucceed()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthToken", "");
            builder.UseSetting("LoginlessMode", "false");
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/discovery");

        // Assert
        // If no token configured, authentication should be bypassed
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiEndpoint_WithLoginlessMode_ShouldSucceedWithoutToken()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("LoginlessMode", "true");
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/discovery");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiEndpoint_WithValidToken_ShouldSucceed()
    {
        // Arrange
        var testToken = "test-integration-token-123";
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthToken", testToken);
            builder.UseSetting("LoginlessMode", "false");
        }).CreateClient();

        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", testToken);

        // Act
        var response = await client.GetAsync("/api/discovery");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiEndpoint_WithInvalidToken_ShouldReturn401()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthToken", "correct-token");
            builder.UseSetting("LoginlessMode", "false");
        }).CreateClient();

        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", "wrong-token");

        // Act
        var response = await client.GetAsync("/api/discovery");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiEndpoint_WithoutTokenHeader_WhenTokenRequired_ShouldReturn401()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthToken", "required-token");
            builder.UseSetting("LoginlessMode", "false");
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/discovery");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/secrets")]
    [InlineData("/api/dashboard/summary")]
    [InlineData("/api/discovery")]
    public async Task ProtectedEndpoints_WithLoginlessMode_ShouldAllowAccess(string endpoint)
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("LoginlessMode", "true");
            builder.UseSetting("DatabasePath", ":memory:");
        }).CreateClient();

        // Act
        var response = await client.GetAsync(endpoint);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/secrets")]
    [InlineData("/api/dashboard/summary")]
    [InlineData("/api/discovery")]
    public async Task ProtectedEndpoints_WithoutAuth_WhenRequired_ShouldReturn401(string endpoint)
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthToken", "required-token");
            builder.UseSetting("LoginlessMode", "false");
        }).CreateClient();

        // Act
        var response = await client.GetAsync(endpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiEndpoint_TokenInQuery_ShouldNotAuthenticate()
    {
        // Arrange - tokens should only be in Authorization header, not query params
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthToken", "test-token");
            builder.UseSetting("LoginlessMode", "false");
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/discovery?token=test-token");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConcurrentRequests_WithSameToken_ShouldAllSucceed()
    {
        // Arrange
        var testToken = "concurrent-test-token";
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthToken", testToken);
            builder.UseSetting("LoginlessMode", "false");
        }).CreateClient();

        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", testToken);

        // Act - send 10 concurrent requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => client.GetAsync("/api/discovery"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(response => 
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Authentication_SwitchingBetweenValidAndInvalidTokens_ShouldBehaveCorrectly()
    {
        // Arrange
        var correctToken = "correct-token";
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AuthToken", correctToken);
            builder.UseSetting("LoginlessMode", "false");
        }).CreateClient();

        // Act & Assert - valid token
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", correctToken);
        var validResponse = await client.GetAsync("/api/discovery");
        validResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);

        // Act & Assert - invalid token
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", "wrong-token");
        var invalidResponse = await client.GetAsync("/api/discovery");
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Act & Assert - valid token again
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", correctToken);
        var validResponse2 = await client.GetAsync("/api/discovery");
        validResponse2.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
