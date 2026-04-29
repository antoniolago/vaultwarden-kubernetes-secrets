using System.Net;
using FluentAssertions;
using VaultwardenK8sSync.E2ETests.Infrastructure;
using Xunit;

namespace VaultwardenK8sSync.E2ETests.Tests;

[Collection("E2E")]
public class ApiHealthTest
{
    private readonly E2ETestFixture _fixture;

    public ApiHealthTest(E2ETestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApiHealthEndpoint_ShouldReturnOk()
    {
        // Arrange
        using var client = new HttpClient();

        // Act
        var response = await client.GetAsync($"{_fixture.ApiUrl}/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
