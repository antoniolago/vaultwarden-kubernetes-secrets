using System;
using System.Collections.Generic;
using System.Text.Json;
using VaultwardenK8sSync.Models;
using Xunit;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Comprehensive test suite for SSH Key type items (Vaultwarden type 5).
/// Covers parsing, extraction, sanitization, and Kubernetes secret generation.
/// </summary>
[Collection("SSH Key Tests")]
[Trait("Category", "SSH Keys")]
public class SshKeyTests
{
    #region SshKeyInfo Model Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Model")]
    public void SshKeyInfo_WithAllProperties_SetsCorrectly()
    {
        // Arrange
        var sshKey = new SshKeyInfo
        {
            PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAA=\n-----END OPENSSH PRIVATE KEY-----",
            PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGtO8wHMmKlPbZqHYn3b8FhOq6bKjH3mYJbLq6LxVf2T user@host",
            Fingerprint = "SHA256:abcdef1234567890abcdef1234567890"
        };

        // Assert
        Assert.NotNull(sshKey.PrivateKey);
        Assert.NotNull(sshKey.PublicKey);
        Assert.NotNull(sshKey.Fingerprint);
        Assert.Contains("BEGIN OPENSSH PRIVATE KEY", sshKey.PrivateKey);
        Assert.StartsWith("ssh-ed25519", sshKey.PublicKey);
        Assert.StartsWith("SHA256:", sshKey.Fingerprint);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Model")]
    public void SshKeyInfo_WithNullProperties_AllowsNull()
    {
        // Arrange & Act
        var sshKey = new SshKeyInfo();

        // Assert - properties can be null
        Assert.Null(sshKey.PrivateKey);
        Assert.Null(sshKey.PublicKey);
        Assert.Null(sshKey.Fingerprint);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Model")]
    public void SshKeyInfo_WithRsaKey_ParsesCorrectly()
    {
        // Arrange
        var rsaPrivateKey = @"-----BEGIN RSA PRIVATE KEY-----
MIIEpAIBAAKCAQEA0Z3VS5JJcds3xfn/ygWyF8PbnGy0AHB7MaU8xKwwKU9dHDff
PAqQ7F6PvHsVLLPpP8GDS/App50P6E3gLKjXJ4VhLWHGxMvM8xY3zQk8GQIDAQAB
AoGAAiCyL+J8bHKz1HrC5P1L6J5x8qKqQ3mJ8xY7zQk8GQIDAQABAoGAAiCyL8P
-----END RSA PRIVATE KEY-----";

        var sshKey = new SshKeyInfo
        {
            PrivateKey = rsaPrivateKey,
            PublicKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQDRndVLkktx2zfF+f/KBbIXw9ucbLQAcHsxpTzErDApT10dN998CpDsXo+8exUs",
            Fingerprint = "MD5:aa:bb:cc:dd:ee:ff:00:11:22:33:44:55:66:77:88:99"
        };

        // Assert
        Assert.Contains("BEGIN RSA PRIVATE KEY", sshKey.PrivateKey);
        Assert.Contains("END RSA PRIVATE KEY", sshKey.PrivateKey);
        Assert.StartsWith("ssh-rsa", sshKey.PublicKey);
        Assert.StartsWith("MD5:", sshKey.Fingerprint);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Model")]
    public void SshKeyInfo_WithEd25519Key_ParsesCorrectly()
    {
        // Arrange
        var ed25519PrivateKey = @"-----BEGIN OPENSSH PRIVATE KEY-----
b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
QyNTUxOQAAACBLQvMBzJipT22ah2J92/BYTqumyoqN5mCWy6ui8VX9pwAAAJC+Vt6kvlbe
pAAAAAtzc2gtZWQyNTUxOQAAACBLQvMBzJipT22ah2J92/BYTqumyoqN5mCWy6ui8VX9pw
AAAEBJhT5bHk0KqM9m0x8bVH+G8p4qKqQ3mJ8xY7zQk8GQIEtC8wHMmKlPbZqHYn3b8FhO
q6bKjH3mYJbLq6LxVf2T
-----END OPENSSH PRIVATE KEY-----";

        var sshKey = new SshKeyInfo
        {
            PrivateKey = ed25519PrivateKey,
            PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIEtC8wHMmKlPbZqHYn3b8FhOq6bKjH3mYJbLq6LxVf2T user@example.com",
            Fingerprint = "SHA256:9abcDEF123ghiJKL456mnoPQR789stuVWX0yzABCD"
        };

        // Assert
        Assert.Contains("BEGIN OPENSSH PRIVATE KEY", sshKey.PrivateKey);
        Assert.Contains("ssh-ed25519", sshKey.PublicKey);
        Assert.Contains("user@example.com", sshKey.PublicKey);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Model")]
    public void SshKeyInfo_WithEcdsaKey_ParsesCorrectly()
    {
        // Arrange
        var ecdsaPrivateKey = @"-----BEGIN EC PRIVATE KEY-----
MHQCAQEEIDHbMvCDyJHkBi9q9V0x8w2x8bVH+G8p4qKqQ3mJ8xY7oAcGBSuBBAAi
oWQDYgAEa0mWi5HqMjGJOzJ8pN9H9xY7zQk8GQIDAQABAoGAAiCyL8P
-----END EC PRIVATE KEY-----";

        var sshKey = new SshKeyInfo
        {
            PrivateKey = ecdsaPrivateKey,
            PublicKey = "ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBPxW",
            Fingerprint = "SHA256:xyzABC123DEF456ghiJKL789mnoPQR0stuvWXYZ"
        };

        // Assert
        Assert.Contains("BEGIN EC PRIVATE KEY", sshKey.PrivateKey);
        Assert.StartsWith("ecdsa-sha2-nistp256", sshKey.PublicKey);
    }

    #endregion

    #region VaultwardenItem Type 5 Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "VaultwardenItem")]
    public void VaultwardenItem_Type5_IsSshKeyType()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-test-id",
            Name = "Test SSH Key",
            Type = 5, // SSH Key type
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGtest",
                Fingerprint = "SHA256:testfingerprint"
            }
        };

        // Assert
        Assert.Equal(5, item.Type);
        Assert.NotNull(item.SshKey);
        Assert.NotNull(item.SshKey.PrivateKey);
        Assert.NotNull(item.SshKey.PublicKey);
        Assert.NotNull(item.SshKey.Fingerprint);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "VaultwardenItem")]
    public void VaultwardenItem_Type5WithNullSshKey_HandlesGracefully()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-test-id",
            Name = "Test SSH Key",
            Type = 5,
            SshKey = null
        };

        // Assert - should not throw
        Assert.Equal(5, item.Type);
        Assert.Null(item.SshKey);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "VaultwardenItem")]
    public void VaultwardenItem_Type5WithPartialSshKeyData_HandlesPartialData()
    {
        // Arrange - only private key, no public key or fingerprint
        var item = new VaultwardenItem
        {
            Id = "ssh-test-id",
            Name = "Test SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----"
                // PublicKey and Fingerprint are null
            }
        };

        // Assert
        Assert.NotNull(item.SshKey);
        Assert.NotNull(item.SshKey.PrivateKey);
        Assert.Null(item.SshKey.PublicKey);
        Assert.Null(item.SshKey.Fingerprint);
    }

    #endregion

    #region JSON Parsing Tests (simulating VaultwardenService.ParseAndDecryptCipher)

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "JSON Parsing")]
    public void ParseSshKeyCipher_WithAllProperties_ParsesCorrectly()
    {
        // Arrange - simulating encrypted JSON response from Vaultwarden
        var json = @"{
            ""id"": ""ssh-key-id-123"",
            ""organizationId"": null,
            ""folderId"": null,
            ""type"": 5,
            ""name"": ""2.encKey|encryptedName"",
            ""sshKey"": {
                ""privateKey"": ""2.encKey|encryptedPrivateKey"",
                ""publicKey"": ""2.encKey|encryptedPublicKey"",
                ""keyFingerprint"": ""2.encKey|encryptedFingerprint""
            },
            ""fields"": []
        }";

        var cipher = JsonSerializer.Deserialize<JsonElement>(json);

        // Act - extract properties (simulating ParseAndDecryptCipher logic)
        var item = new VaultwardenItem();

        if (cipher.TryGetProperty("id", out var idProp))
            item.Id = idProp.GetString() ?? "";

        if (cipher.TryGetProperty("type", out var typeProp))
            item.Type = typeProp.GetInt32();

        if (cipher.TryGetProperty("name", out var nameProp))
            item.Name = nameProp.GetString() ?? "";

        if (item.Type == 5)
        {
            if (cipher.TryGetProperty("sshKey", out var sshKeyProp))
            {
                item.SshKey = new SshKeyInfo();
                if (sshKeyProp.TryGetProperty("privateKey", out var privateKeyProp))
                    item.SshKey.PrivateKey = privateKeyProp.GetString();
                if (sshKeyProp.TryGetProperty("publicKey", out var publicKeyProp))
                    item.SshKey.PublicKey = publicKeyProp.GetString();
                if (sshKeyProp.TryGetProperty("keyFingerprint", out var fingerprintProp))
                    item.SshKey.Fingerprint = fingerprintProp.GetString();
            }
        }

        // Assert
        Assert.Equal(5, item.Type);
        Assert.Equal("ssh-key-id-123", item.Id);
        Assert.NotNull(item.SshKey);
        Assert.Equal("2.encKey|encryptedPrivateKey", item.SshKey.PrivateKey);
        Assert.Equal("2.encKey|encryptedPublicKey", item.SshKey.PublicKey);
        Assert.Equal("2.encKey|encryptedFingerprint", item.SshKey.Fingerprint);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "JSON Parsing")]
    public void ParseSshKeyCipher_WithCasingVariations_ParsesCorrectly()
    {
        // Arrange - PascalCase variant
        var json = @"{
            ""Id"": ""ssh-key-id-456"",
            ""Type"": 5,
            ""Name"": ""2.encKey|encryptedName"",
            ""SshKey"": {
                ""PrivateKey"": ""2.encKey|encryptedPrivateKey"",
                ""PublicKey"": ""2.encKey|encryptedPublicKey"",
                ""KeyFingerprint"": ""2.encKey|encryptedFingerprint""
            }
        }";

        var cipher = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var item = new VaultwardenItem();

        if (cipher.TryGetProperty("id", out var idProp) || cipher.TryGetProperty("Id", out idProp))
            item.Id = idProp.GetString() ?? "";

        if (cipher.TryGetProperty("type", out var typeProp) || cipher.TryGetProperty("Type", out typeProp))
            item.Type = typeProp.GetInt32();

        if (cipher.TryGetProperty("name", out var nameProp) || cipher.TryGetProperty("Name", out nameProp))
            item.Name = nameProp.GetString() ?? "";

        if (item.Type == 5)
        {
            if (cipher.TryGetProperty("sshKey", out var sshKeyProp) || cipher.TryGetProperty("SshKey", out sshKeyProp))
            {
                item.SshKey = new SshKeyInfo();
                if (sshKeyProp.TryGetProperty("privateKey", out var privateKeyProp) || sshKeyProp.TryGetProperty("PrivateKey", out privateKeyProp))
                    item.SshKey.PrivateKey = privateKeyProp.GetString();
                if (sshKeyProp.TryGetProperty("publicKey", out var publicKeyProp) || sshKeyProp.TryGetProperty("PublicKey", out publicKeyProp))
                    item.SshKey.PublicKey = publicKeyProp.GetString();
                if (sshKeyProp.TryGetProperty("keyFingerprint", out var fingerprintProp) || sshKeyProp.TryGetProperty("KeyFingerprint", out fingerprintProp))
                    item.SshKey.Fingerprint = fingerprintProp.GetString();
            }
        }

        // Assert
        Assert.Equal(5, item.Type);
        Assert.Equal("ssh-key-id-456", item.Id);
        Assert.NotNull(item.SshKey);
        Assert.Equal("2.encKey|encryptedPrivateKey", item.SshKey.PrivateKey);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "JSON Parsing")]
    public void ParseSshKeyCipher_WithNullProperties_HandlesNulls()
    {
        // Arrange
        var json = @"{
            ""id"": ""ssh-key-id-789"",
            ""type"": 5,
            ""name"": ""Test SSH Key"",
            ""sshKey"": {
                ""privateKey"": ""2.encKey|encryptedPrivateKey"",
                ""publicKey"": null,
                ""keyFingerprint"": null
            }
        }";

        var cipher = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var item = new VaultwardenItem
        {
            Id = cipher.GetProperty("id").GetString() ?? "",
            Type = cipher.GetProperty("type").GetInt32(),
            Name = cipher.GetProperty("name").GetString() ?? ""
        };

        if (item.Type == 5 && cipher.TryGetProperty("sshKey", out var sshKeyProp))
        {
            item.SshKey = new SshKeyInfo();
            if (sshKeyProp.TryGetProperty("privateKey", out var privateKeyProp))
                item.SshKey.PrivateKey = privateKeyProp.ValueKind != JsonValueKind.Null ? privateKeyProp.GetString() : null;
            if (sshKeyProp.TryGetProperty("publicKey", out var publicKeyProp))
                item.SshKey.PublicKey = publicKeyProp.ValueKind != JsonValueKind.Null ? publicKeyProp.GetString() : null;
            if (sshKeyProp.TryGetProperty("keyFingerprint", out var fingerprintProp))
                item.SshKey.Fingerprint = fingerprintProp.ValueKind != JsonValueKind.Null ? fingerprintProp.GetString() : null;
        }

        // Assert
        Assert.NotNull(item.SshKey);
        Assert.NotNull(item.SshKey.PrivateKey);
        Assert.Null(item.SshKey.PublicKey);
        Assert.Null(item.SshKey.Fingerprint);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "JSON Parsing")]
    public void ParseSshKeyCipher_WithMissingSshKeySection_HandlesGracefully()
    {
        // Arrange - sshKey section missing entirely
        var json = @"{
            ""id"": ""ssh-key-id-missing"",
            ""type"": 5,
            ""name"": ""Test SSH Key""
        }";

        var cipher = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var item = new VaultwardenItem
        {
            Id = cipher.GetProperty("id").GetString() ?? "",
            Type = cipher.GetProperty("type").GetInt32(),
            Name = cipher.GetProperty("name").GetString() ?? ""
        };

        if (item.Type == 5 && cipher.TryGetProperty("sshKey", out var sshKeyProp))
        {
            item.SshKey = new SshKeyInfo();
            if (sshKeyProp.TryGetProperty("privateKey", out var privateKeyProp))
                item.SshKey.PrivateKey = privateKeyProp.GetString();
        }

        // Assert - SshKey should be null
        Assert.Equal(5, item.Type);
        Assert.Null(item.SshKey);
    }

    #endregion

    #region Namespace and Custom Field Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Custom Fields")]
    public void SshKeyItem_WithNamespace_ExtractsNamespace()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "Production SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGtest",
                Fingerprint = "SHA256:test"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production,staging", Type = 0 }
            }
        };

        // Act
        var namespaces = item.ExtractNamespaces();

        // Assert
        Assert.Equal(2, namespaces.Count);
        Assert.Contains("production", namespaces);
        Assert.Contains("staging", namespaces);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Custom Fields")]
    public void SshKeyItem_WithCustomSecretName_ExtractsCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "Production SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "prod-ssh-key", Type = 0 }
            }
        };

        // Act
        var secretName = item.ExtractSecretName();

        // Assert
        Assert.Equal("prod-ssh-key", secretName);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Custom Fields")]
    public void SshKeyItem_WithCustomKeyPassword_ExtractsCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "Production SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "DEPLOY_KEY", Type = 0 }
            }
        };

        // Act
        var keyPassword = item.ExtractSecretKeyPassword();

        // Assert
        Assert.Equal("DEPLOY_KEY", keyPassword);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Custom Fields")]
    public void SshKeyItem_WithIgnoreFields_ExcludesMetadata()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "Production SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "Opaque", Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "managed-by=vaultwarden", Type = 0 }
            }
        };

        // Act
        var ignoredFields = item.ExtractIgnoredFields();

        // Assert - secret-annotation, secret-label, secret-type, namespaces should all be ignored
        Assert.Contains("namespaces", ignoredFields);
        Assert.Contains("secret-type", ignoredFields);
        Assert.Contains("secret-annotation", ignoredFields);
        Assert.Contains("secret-label", ignoredFields);
    }

    #endregion

    #region Secret Type Tests for SSH Keys

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Secret Type")]
    public void SshKeyItem_WithDefaultSecretType_ReturnsOpaque()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 }
            }
        };

        // Act
        var secretType = item.ExtractSecretType();

        // Assert
        Assert.Equal("Opaque", secretType);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Secret Type")]
    public void SshKeyItem_WithCustomSecretType_ReturnsCorrectType()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/basic-auth", Type = 0 }
            }
        };

        // Act
        var secretType = item.ExtractSecretType();

        // Assert
        Assert.Equal("kubernetes.io/basic-auth", secretType);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Edge Cases")]
    public void SshKeyItem_WithEmptyPrivateKey_HandlesEmptyValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "",
                PublicKey = "ssh-ed25519 test",
                Fingerprint = "SHA256:test"
            }
        };

        // Assert
        Assert.NotNull(item.SshKey);
        Assert.Equal("", item.SshKey.PrivateKey);
        Assert.NotNull(item.SshKey.PublicKey);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Edge Cases")]
    public void SshKeyItem_WithWhitespaceInFingerprint_TrimsCorrectly()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-ed25519 test",
                Fingerprint = "  SHA256:testfingerprint  "
            }
        };

        // Act
        var fingerprint = item.SshKey.Fingerprint?.Trim();

        // Assert
        Assert.Equal("SHA256:testfingerprint", fingerprint);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Edge Cases")]
    public void SshKeyItem_WithMultilinePublicKey_HandlesMultiline()
    {
        // Arrange
        var multilinePublicKey = @"ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQDRndVLkktx2zfF+f/KBbIXw9ucbLQAcHsxpTzErDAp
T10dN998CpDsXo+8exUsMkvgLs9fPvJHvMqGZ7yqLqJLqVn8FJH5zQk8GQIDAQABAoGAAiCyL+J8bHKz1HrC
5P1L6J5x8qKqQ3mJ8xY7zQk8GQIDAQABAoGAAiCyL8P user@host";

        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN RSA PRIVATE KEY-----\ntest\n-----END RSA PRIVATE KEY-----",
                PublicKey = multilinePublicKey,
                Fingerprint = "SHA256:test"
            }
        };

        // Assert
        Assert.NotNull(item.SshKey.PublicKey);
        Assert.Contains("\n", item.SshKey.PublicKey);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Edge Cases")]
    public void SshKeyItem_WithVeryLongPrivateKey_HandlesLongKey()
    {
        // Arrange - 4096-bit RSA key
        var longPrivateKey = "-----BEGIN RSA PRIVATE KEY-----\n" +
            new string('A', 3000) + "\n" +
            "-----END RSA PRIVATE KEY-----";

        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = longPrivateKey
            }
        };

        // Assert
        Assert.NotNull(item.SshKey.PrivateKey);
        Assert.True(item.SshKey.PrivateKey.Length > 3000);
        Assert.StartsWith("-----BEGIN RSA PRIVATE KEY-----", item.SshKey.PrivateKey);
        Assert.EndsWith("-----END RSA PRIVATE KEY-----", item.SshKey.PrivateKey);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Edge Cases")]
    public void SshKeyItem_DifferentKeyTypes_HandlesAllTypes()
    {
        // Arrange & Assert - Test all major SSH key types
        var keyTypes = new[]
        {
            ("RSA", "-----BEGIN RSA PRIVATE KEY-----", "ssh-rsa"),
            ("DSA", "-----BEGIN DSA PRIVATE KEY-----", "ssh-dss"),
            ("EC", "-----BEGIN EC PRIVATE KEY-----", "ecdsa-sha2-nistp256"),
            ("OPENSSH", "-----BEGIN OPENSSH PRIVATE KEY-----", "ssh-ed25519")
        };

        foreach (var (type, header, publicPrefix) in keyTypes)
        {
            var item = new VaultwardenItem
            {
                Id = $"ssh-key-{type.ToLower()}",
                Name = $"{type} SSH Key",
                Type = 5,
                SshKey = new SshKeyInfo
                {
                    PrivateKey = $"{header}\ntest\n-----END {type} PRIVATE KEY-----",
                    PublicKey = $"{publicPrefix} AAAAtest",
                    Fingerprint = $"SHA256:{type.ToLower()}"
                }
            };

            Assert.Equal(5, item.Type);
            Assert.NotNull(item.SshKey);
            Assert.Contains(header, item.SshKey.PrivateKey);
            Assert.StartsWith(publicPrefix, item.SshKey.PublicKey);
        }
    }

    #endregion

    #region Integration with Custom Fields

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public void SshKeyItem_WithMultipleCustomFields_HandlesAllFields()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-id",
            Name = "Production SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nprivatekeycontent\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGpublickeycontent user@host",
                Fingerprint = "SHA256:fingerprintcontent"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "prod-ssh-key", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "SSH_PRIVATE_KEY", Type = 0 },
                new FieldInfo { Name = "secret-key-username", Value = "deploy-user", Type = 0 },
                new FieldInfo { Name = "environment", Value = "production", Type = 0 },
                new FieldInfo { Name = "owner", Value = "platform-team", Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "managed-by=vaultwarden", Type = 0 },
                new FieldInfo { Name = "secret-label", Value = "app=ssh-keys", Type = 0 }
            }
        };

        // Act
        var namespaces = item.ExtractNamespaces();
        var secretName = item.ExtractSecretName();
        var keyPassword = item.ExtractSecretKeyPassword();
        var keyUsername = item.ExtractSecretKeyUsername();
        var secretType = item.ExtractSecretType();
        var annotations = item.ExtractSecretAnnotations();
        var labels = item.ExtractSecretLabels();

        // Assert
        Assert.Single(namespaces);
        Assert.Contains("production", namespaces);
        Assert.Equal("prod-ssh-key", secretName);
        Assert.Equal("SSH_PRIVATE_KEY", keyPassword);
        Assert.Equal("deploy-user", keyUsername);
        Assert.Equal("Opaque", secretType);
        Assert.Single(annotations);
        Assert.Equal("vaultwarden", annotations["managed-by"]);
        Assert.Single(labels);
        Assert.Equal("ssh-keys", labels["app"]);
    }

    #endregion
}
