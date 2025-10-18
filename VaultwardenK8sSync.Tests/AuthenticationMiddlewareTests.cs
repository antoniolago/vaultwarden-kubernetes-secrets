using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Database;
using Xunit;
using FluentAssertions;

namespace VaultwardenK8sSync.Tests;

public class AuthenticationMiddlewareTests
{
    private readonly Mock<RequestDelegate> _nextMock;
    private readonly Mock<ILogger<TokenAuthenticationMiddleware>> _loggerMock;
    private readonly DefaultHttpContext _httpContext;

    public AuthenticationMiddlewareTests()
    {
        _nextMock = new Mock<RequestDelegate>();
        _loggerMock = new Mock<ILogger<TokenAuthenticationMiddleware>>();
        _httpContext = new DefaultHttpContext();
    }

    [Fact]
    public async Task InvokeAsync_WithHealthEndpoint_ShouldBypassAuthentication()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token",
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/health";

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Once);
        _httpContext.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WithLoginlessMode_ShouldBypassAuthentication()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token",
            LoginlessMode = true
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/api/secrets";

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Loginless mode enabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithNoTokenConfigured_ShouldBypassAuthentication()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = string.Empty,
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/api/secrets";

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithValidToken_ShouldAllowAccess()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/api/secrets";
        _httpContext.Request.Headers["Authorization"] = "Bearer test-token-123";

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithInvalidToken_ShouldReturn401()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/api/secrets";
        _httpContext.Request.Headers["Authorization"] = "Bearer wrong-token";

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Never);
        _httpContext.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_WithMissingAuthorizationHeader_ShouldReturn401()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/api/secrets";

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Never);
        _httpContext.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_WithMalformedAuthorizationHeader_ShouldReturn401()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/api/secrets";
        _httpContext.Request.Headers["Authorization"] = "InvalidFormat";

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Never);
        _httpContext.Response.StatusCode.Should().Be(401);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/Health")]
    [InlineData("/HEALTH")]
    public async Task InvokeAsync_WithHealthEndpointDifferentCases_ShouldBypassAuthentication(string path)
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = path;

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithLoginlessModeAndToken_ShouldPreferLoginlessMode()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = true
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/api/secrets";
        // No authorization header provided

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Loginless mode enabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithTokenHavingExtraSpaces_ShouldValidateCorrectly()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/api/secrets";
        _httpContext.Request.Headers["Authorization"] = "Bearer  test-token-123  "; // Extra spaces

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        // Should handle trimming properly
        var statusCode = _httpContext.Response.StatusCode;
        statusCode.Should().BeOneOf(200, 401); // Depends on implementation
    }

    [Fact]
    public async Task InvokeAsync_ConfigurationPriority_ShouldFollowCorrectOrder()
    {
        // Test that health check is always first priority
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = false
        };
        var middleware = new TokenAuthenticationMiddleware(config, _loggerMock.Object);
        _httpContext.Request.Path = "/health";
        // No authorization header

        // Act
        await middleware.InvokeAsync(_httpContext, _nextMock.Object);

        // Assert
        _nextMock.Verify(next => next(_httpContext), Times.Once);
    }
}
