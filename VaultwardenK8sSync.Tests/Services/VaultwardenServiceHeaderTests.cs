using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Services;
using Xunit;

namespace VaultwardenK8sSync.Tests.Services;

/// <summary>
/// Tests that VaultwardenService sets the required Bitwarden-Client-Version header.
/// Without this header, Bitwarden-compatible servers may reject API requests.
/// </summary>
[Trait("Category", "Unit")]
public class VaultwardenServiceHeaderTests
{
    [Fact]
    public void HttpClient_ShouldHaveBitwardenClientVersionHeader()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<VaultwardenService>>();
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://test.vaultwarden.local",
            MasterPassword = "testpassword",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };

        // Use a real HttpClient via the mock factory so we can inspect headers
        var httpClient = new HttpClient();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(f => f.CreateClient("Vaultwarden"))
            .Returns(httpClient);

        // Act - The constructor should set the header on the HttpClient
        var service = new VaultwardenService(loggerMock.Object, config, httpClientFactoryMock.Object);

        // Assert - The header must be present
        var hasHeader = httpClient.DefaultRequestHeaders.Contains("Bitwarden-Client-Version");
        Assert.True(hasHeader,
            "VaultwardenService must set the Bitwarden-Client-Version header on its HttpClient. " +
            "Without this header, Vaultwarden >= v1.33.0 and the official Bitwarden server " +
            "may reject API requests with a 400 error.");
    }
}
