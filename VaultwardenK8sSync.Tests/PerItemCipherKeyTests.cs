using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Regression tests for per-item cipher keys (ciphers.key).
///
/// Bitwarden/Vaultwarden web vault 2026.10+ wraps a random 64-byte key per item and encrypts the
/// item contents (name, notes, fields, login, sshKey) with THAT key instead of the org/user key.
/// Without unwrapping ciphers.key the sync fails with "Padding is invalid and cannot be removed"
/// and no secret is created at all.
/// </summary>
public class PerItemCipherKeyTests
{
    private readonly Mock<ILogger<VaultwardenService>> _loggerMock = new();
    private const string OrgId = "11111111-2222-3333-4444-555555555555";

    private static byte[] RandomKey() => RandomNumberGenerator.GetBytes(64);

    /// <summary>Encrypts raw bytes with a 64-byte symmetric key: 2.{iv}|{data}|{mac}</summary>
    private static string EncryptBytes(byte[] plaintext, byte[] symKey)
    {
        var encKey = symKey[..32];
        var macKey = symKey[32..64];
        using var aes = Aes.Create();
        aes.Key = encKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var data = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        using var hmac = new HMACSHA256(macKey);
        var macInput = new byte[aes.IV.Length + data.Length];
        aes.IV.CopyTo(macInput, 0);
        data.CopyTo(macInput, aes.IV.Length);

        return $"2.{Convert.ToBase64String(aes.IV)}|{Convert.ToBase64String(data)}|{Convert.ToBase64String(hmac.ComputeHash(macInput))}";
    }

    private static string Encrypt(string plaintext, byte[] symKey) =>
        EncryptBytes(Encoding.UTF8.GetBytes(plaintext), symKey);

    /// <summary>Creates a service with the org key injected the same way UnlockVaultAsync does.</summary>
    private VaultwardenService CreateService(out byte[] orgKey)
    {
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://test.vaultwarden.local",
            MasterPassword = "testpassword",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        using var httpClient = new HttpClient();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var service = new VaultwardenService(_loggerMock.Object, config, httpClientFactoryMock.Object);

        orgKey = RandomKey();
        var orgKeysField = typeof(VaultwardenService).GetField("_orgKeys", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var orgKeys = (Dictionary<string, (byte[] encKey, byte[] macKey)>)orgKeysField.GetValue(service)!;
        orgKeys[OrgId] = (orgKey[..32], orgKey[32..64]);

        return service;
    }

    private static string BuildCipherJson(string? wrappedItemKey, string name, string[] values, byte[] contentKey)
    {
        var fields = new List<string>();
        for (int i = 0; i < values.Length; i++)
        {
            fields.Add($"{{\"name\":\"{Encrypt($"f{i}", contentKey)}\",\"value\":\"{Encrypt(values[i], contentKey)}\",\"type\":0}}");
        }

        var keyJson = wrappedItemKey == null ? "null" : $"\"{wrappedItemKey}\"";
        return $@"{{
            ""id"": ""cipher-1"",
            ""type"": 2,
            ""organizationId"": ""{OrgId}"",
            ""key"": {keyJson},
            ""name"": ""{Encrypt(name, contentKey)}"",
            ""notes"": null,
            ""fields"": [{string.Join(",", fields)}]
        }}";
    }

    private static VaultwardenItem? Parse(VaultwardenService service, string json)
    {
        var cipher = JsonSerializer.Deserialize<JsonElement>(json);
        var method = typeof(VaultwardenService).GetMethod("ParseAndDecryptCipher", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return method!.Invoke(service, new object[] { cipher }) as VaultwardenItem;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseAndDecryptCipher_WithPerItemKey_DecryptsItemContents()
    {
        var service = CreateService(out var orgKey);
        var itemKey = RandomKey();

        // ciphers.key wraps the raw 64-byte item key with the org key
        var wrappedItemKey = EncryptBytes(itemKey, orgKey);
        var json = BuildCipherJson(wrappedItemKey, "lab-secret", new[] { "lab-ns", "token-value" }, itemKey);

        var result = Parse(service, json);

        Assert.NotNull(result);
        Assert.Equal("lab-secret", result!.Name);
        Assert.NotNull(result.Fields);
        Assert.Equal(2, result.Fields!.Count);
        Assert.Equal("lab-ns", result.Fields[0].Value);
        Assert.Equal("token-value", result.Fields[1].Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseAndDecryptCipher_WithoutPerItemKey_DecryptsWithOrgKey()
    {
        // Legacy items are encrypted with the org key directly: no ciphers.key at all.
        var service = CreateService(out var orgKey);
        var json = BuildCipherJson(null, "legacy-secret", Array.Empty<string>(), orgKey);

        var result = Parse(service, json);

        Assert.NotNull(result);
        Assert.Equal("legacy-secret", result!.Name);
    }
}
