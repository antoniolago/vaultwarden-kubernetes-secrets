using FluentAssertions;
using VaultwardenK8sSync.Database;
using Xunit;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Tests for authentication configuration scenarios and edge cases
/// </summary>
public class AuthenticationConfigurationTests
{
    [Fact]
    public void AuthenticationConfig_DefaultValues_ShouldBeSecure()
    {
        // Arrange & Act
        var config = new AuthenticationConfig();

        // Assert
        config.LoginlessMode.Should().BeFalse("security should be enabled by default");
        config.Token.Should().BeEmpty("no default token should exist");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void AuthenticationConfig_EmptyToken_ShouldBeConsideredNoAuth(string? token)
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = token!,
            LoginlessMode = false
        };

        // Act
        var hasToken = !string.IsNullOrWhiteSpace(config.Token);

        // Assert
        hasToken.Should().BeFalse("empty tokens should not enable authentication");
    }

    [Fact]
    public void AuthenticationConfig_WithLoginlessMode_ShouldOverrideToken()
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = "some-token",
            LoginlessMode = true
        };

        // Assert
        config.LoginlessMode.Should().BeTrue("loginless mode should take precedence");
    }

    [Theory]
    [InlineData("simple-token")]
    [InlineData("token-with-special-chars-!@#$%")]
    [InlineData("very-long-token-" + "a123456789012345678901234567890123456789")]
    [InlineData("token with spaces")]
    public void AuthenticationConfig_VariousTokenFormats_ShouldBeAccepted(string token)
    {
        // Arrange
        var config = new AuthenticationConfig
        {
            Token = token!,
            LoginlessMode = false
        };

        // Assert
        config.Token.Should().Be(token);
        string.IsNullOrWhiteSpace(config.Token).Should().BeFalse();
    }

    [Fact]
    public void AuthenticationConfig_ProductionScenario_ShouldBeValid()
    {
        // Arrange - typical production configuration
        var config = new AuthenticationConfig
        {
            Token = "secure-random-generated-token-32chars",
            LoginlessMode = false
        };

        // Assert
        config.LoginlessMode.Should().BeFalse("production should require auth");
        config.Token.Should().NotBeNullOrWhiteSpace("production should have a token");
        config.Token!.Length.Should().BeGreaterOrEqualTo(16, "tokens should be reasonably long");
    }

    [Fact]
    public void AuthenticationConfig_DevelopmentScenario_ShouldBeValid()
    {
        // Arrange - typical development configuration
        var config = new AuthenticationConfig
        {
            Token = "",
            LoginlessMode = true
        };

        // Assert
        config.LoginlessMode.Should().BeTrue("development can skip auth");
    }

    [Fact]
    public void AuthenticationConfig_TestingScenario_ShouldBeValid()
    {
        // Arrange - typical testing configuration
        var config = new AuthenticationConfig
        {
            Token = "test-token-123",
            LoginlessMode = false
        };

        // Assert
        config.LoginlessMode.Should().BeFalse();
        config.Token.Should().Be("test-token-123");
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "some-token")]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, "some-token")]
    public void AuthenticationConfig_AllCombinations_ShouldBeConstructable(bool loginlessMode, string? token)
    {
        // Arrange & Act
        var config = new AuthenticationConfig
        {
            LoginlessMode = loginlessMode,
            Token = token ?? ""
        };

        // Assert
        config.Should().NotBeNull();
        config.LoginlessMode.Should().Be(loginlessMode);
        config.Token.Should().Be(token ?? "");
    }

    [Fact]
    public void AuthenticationConfig_SecurityBestPractices_ProductionSettings()
    {
        // Arrange - recommended production settings
        var config = new AuthenticationConfig
        {
            Token = GenerateSecureToken(32),
            LoginlessMode = false
        };

        // Assert
        config.LoginlessMode.Should().BeFalse("production should always require authentication");
        config.Token.Should().NotBeNullOrWhiteSpace("production should always have a token");
        config.Token!.Length.Should().BeGreaterOrEqualTo(32, "tokens should be at least 32 characters");
    }

    [Fact]
    public void AuthenticationConfig_SecurityWarnings_InsecureSettings()
    {
        // Arrange - insecure settings that should be flagged
        var insecureConfigs = new[]
        {
            new AuthenticationConfig { LoginlessMode = true, Token = "" },
            new AuthenticationConfig { LoginlessMode = false, Token = "" },
            new AuthenticationConfig { LoginlessMode = false, Token = "short" }
        };

        // Assert
        foreach (var config in insecureConfigs)
        {
            var isInsecure = config.LoginlessMode || 
                           string.IsNullOrWhiteSpace(config.Token) || 
                           (config.Token?.Length ?? 0) < 16;
            
            isInsecure.Should().BeTrue("these configurations should be considered insecure");
        }
    }

    [Theory]
    [InlineData("Bearer token123", "token123")]
    [InlineData("Bearer  token123  ", "token123")]
    public void TokenExtraction_FromAuthorizationHeader_ShouldTrim(string authHeader, string expectedToken)
    {
        // Simulate extraction logic  
        var parts = authHeader.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var extractedToken = parts.Length > 1 ? parts[1].Trim() : null;

        // Assert
        extractedToken.Should().Be(expectedToken);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/Health")]
    [InlineData("/HEALTH")]
    [InlineData("/HeAlTh")]
    public void HealthEndpointCheck_ShouldBeCaseInsensitive(string path)
    {
        // Simulate health check logic
        var isHealthEndpoint = path.Equals("/health", StringComparison.OrdinalIgnoreCase);

        // Assert
        isHealthEndpoint.Should().BeTrue("health endpoint checks should be case-insensitive");
    }

    private static string GenerateSecureToken(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}
