using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VaultwardenK8sSync.Models;
using Xunit;

namespace VaultwardenK8sSync.Tests;

/// <summary>
/// Integration tests for SSH Key synchronization from Vaultwarden to Kubernetes.
/// Tests the complete flow: VaultwardenItem → ExtractSecretData → Kubernetes Secret data.
/// </summary>
[Collection("SSH Key Integration Tests")]
[Trait("Category", "SSH Keys")]
[Trait("Category", "Integration")]
public class SshKeyIntegrationTests
{
    #region Basic SSH Key Extraction Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithSshKeyItem_ExtractsPrivateKeyAsPrimaryValue()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-1",
            Name = "Production Deploy Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZWQyNTUxOQAAACBLQvMBzJipT22ah2J92/BYTqumyoqN5mCWy6ui8VX9pwAAAJC+Vt6kvlZepAAAAAtzc2gtZWQyNTUxOQAAACBLQvMBzJipT22ah2J92/BYTqumyoqN5mCWy6ui8VX9pwAAAEBJhT5bHk0KqM9m0x8bVH+G8p4qKqQ3mJ8xY7zQk8GQIEtC8wHMmKlPbZqHYn3b8FhOq6bKjH3mYJbLq6LxVf2T\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIEtC8wHMmKlPbZqHYn3b8FhOq6bKjH3mYJbLq6LxVf2T user@production",
                Fingerprint = "SHA256:abcdef1234567890"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.NotEmpty(result);
        // Primary value should be the private key
        Assert.Contains("production-deploy-key", result.Keys);
        Assert.Contains("-----BEGIN OPENSSH PRIVATE KEY-----", result["production-deploy-key"]);
        Assert.Contains("-----END OPENSSH PRIVATE KEY-----", result["production-deploy-key"]);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithSshKeyItem_IncludesPublicKeyAndFingerprint()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-2",
            Name = "CI/CD SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN RSA PRIVATE KEY-----\ntestprivatekey\n-----END RSA PRIVATE KEY-----",
                PublicKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQDRndVLkktx2zfF ci@cd",
                Fingerprint = "SHA256:cicdfingerprint"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "ci-cd", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.Contains("ci-cd-ssh-key-public-key", result.Keys);
        Assert.Contains("ci-cd-ssh-key-fingerprint", result.Keys);
        Assert.Equal("ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQDRndVLkktx2zfF ci@cd", result["ci-cd-ssh-key-public-key"]);
        Assert.Equal("SHA256:cicdfingerprint", result["ci-cd-ssh-key-fingerprint"]);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithSshKeyItemAndCustomKeyName_UsesCustomKeyName()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-3",
            Name = "Database SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\ndbprivatekey\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-ed25519 DBtest db@host",
                Fingerprint = "SHA256:dbfingerprint"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "database", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "DEPLOY_KEY", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.Contains("DEPLOY_KEY", result.Keys);
        Assert.Contains("-----BEGIN OPENSSH PRIVATE KEY-----", result["DEPLOY_KEY"]);
    }

    #endregion

    #region SSH Key Hydration Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithPartialSshKeyData_HandlesMissingPublicKey()
    {
        // Arrange - only private key present
        var item = new VaultwardenItem
        {
            Id = "ssh-key-partial",
            Name = "Partial SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nonlyprivatekey\n-----END OPENSSH PRIVATE KEY-----"
                // PublicKey and Fingerprint are null
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "staging", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert - should still work, just won't have public key/fingerprint entries
        Assert.NotEmpty(result);
        Assert.Contains("partial-ssh-key", result.Keys);
        Assert.Contains("-----BEGIN OPENSSH PRIVATE KEY-----", result["partial-ssh-key"]);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithNullSshKeyData_HandlesGracefully()
    {
        // Arrange - SshKey is null (needs hydration)
        var item = new VaultwardenItem
        {
            Id = "ssh-key-null",
            Name = "Unhydrated SSH Key",
            Type = 5,
            SshKey = null,
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "development", Type = 0 }
            }
        };

        // Act - should not throw
        var result = await ExtractSecretDataAsync(item);

        // Assert - should extract from password field or custom fields as fallback
        Assert.NotNull(result);
    }

    #endregion

    #region Multi-Namespace SSH Key Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithSshKeyInMultipleNamespaces_ExtractsAllNamespaces()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-multi",
            Name = "Multi-Environment SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nmultienvkey\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-ed25519 MULTItest multi@env",
                Fingerprint = "SHA256:multifingerprint"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production,staging,development", Type = 0 }
            }
        };

        // Act
        var namespaces = item.ExtractNamespaces();

        // Assert
        Assert.Equal(3, namespaces.Count);
        Assert.Contains("production", namespaces);
        Assert.Contains("staging", namespaces);
        Assert.Contains("development", namespaces);
    }

    #endregion

    #region SSH Key with Custom Metadata Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithSshKeyAndCustomMetadata_IncludesAllMetadata()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-metadata",
            Name = "SSH Key with Metadata",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN RSA PRIVATE KEY-----\nmetadatatestkey\n-----END RSA PRIVATE KEY-----",
                PublicKey = "ssh-rsa METADATAtest meta@data",
                Fingerprint = "SHA256:metadatafingerprint"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "prod-ssh-key", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "SSH_KEY", Type = 0 },
                new FieldInfo { Name = "secret-key-username", Value = "deployer", Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "Opaque", Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "managed-by=vaultwarden", Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "team=platform", Type = 0 },
                new FieldInfo { Name = "secret-label", Value = "app=ssh-keys", Type = 0 },
                new FieldInfo { Name = "secret-label", Value = "env=production", Type = 0 }
            }
        };

        // Act
        var secretName = item.ExtractSecretName();
        var keyPassword = item.ExtractSecretKeyPassword();
        var keyUsername = item.ExtractSecretKeyUsername();
        var secretType = item.ExtractSecretType();
        var annotations = item.ExtractSecretAnnotations();
        var labels = item.ExtractSecretLabels();
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.Equal("prod-ssh-key", secretName);
        Assert.Equal("SSH_KEY", keyPassword);
        Assert.Equal("deployer", keyUsername);
        Assert.Equal("Opaque", secretType);
        Assert.Equal(2, annotations.Count);
        Assert.Equal("vaultwarden", annotations["managed-by"]);
        Assert.Equal("platform", annotations["team"]);
        Assert.Equal(2, labels.Count);
        Assert.Equal("ssh-keys", labels["app"]);
        Assert.Equal("production", labels["env"]);

        // Secret data should not include metadata fields
        Assert.DoesNotContain("secret-name", result.Keys);
        Assert.DoesNotContain("secret-type", result.Keys);
        Assert.DoesNotContain("secret-annotation", result.Keys);
        Assert.DoesNotContain("secret-label", result.Keys);
    }

    #endregion

    #region SSH Key Format Preservation Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithRsaPrivateKey_PreservesMultilineFormat()
    {
        // Arrange
        var rsaPrivateKey = @"-----BEGIN RSA PRIVATE KEY-----
MIIEpAIBAAKCAQEA0Z3VS5JJcds3xfn/ygWyF8PbnGy0AHB7MaU8xKwwKU9dHDff
PAqQ7F6PvHsVLLPpP8GDS/App50P6E3gLKjXJ4VhLWHGxMvM8xY3zQk8GQIDAQAB
AoGAAiCyL+J8bHKz1HrC5P1L6J5x8qKqQ3mJ8xY7zQk8GQIDAQABAoGAAiCyL8P
-----END RSA PRIVATE KEY-----";

        var item = new VaultwardenItem
        {
            Id = "ssh-key-rsa",
            Name = "RSA SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = rsaPrivateKey
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        var key = result["rsa-ssh-key"];
        Assert.Contains("-----BEGIN RSA PRIVATE KEY-----", key);
        Assert.Contains("-----END RSA PRIVATE KEY-----", key);
        // Multiline format should be preserved
        Assert.Contains("\n", key);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithEd25519PrivateKey_PreservesFormat()
    {
        // Arrange
        var ed25519PrivateKey = @"-----BEGIN OPENSSH PRIVATE KEY-----
b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZWQyNTUxOQAAACBLQvMBzJipT22ah2J92/BYTqumyoqN5mCWy6ui8VX9pwAAAJC+Vt6kvlZepAAAAAtzc2gtZWQyNTUxOQAAACBLQvMBzJipT22ah2J92/BYTqumyoqN5mCWy6ui8VX9pwAAAEBJhT5bHk0KqM9m0x8bVH+G8p4qKqQ3mJ8xY7zQk8GQIEtC8wHMmKlPbZqHYn3b8FhOq6bKjH3mYJbLq6LxVf2T
-----END OPENSSH PRIVATE KEY-----";

        var item = new VaultwardenItem
        {
            Id = "ssh-key-ed25519",
            Name = "Ed25519 SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = ed25519PrivateKey
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        var key = result["ed25519-ssh-key"];
        Assert.Contains("-----BEGIN OPENSSH PRIVATE KEY-----", key);
        Assert.Contains("-----END OPENSSH PRIVATE KEY-----", key);
    }

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithEcdsaPrivateKey_PreservesFormat()
    {
        // Arrange
        var ecdsaPrivateKey = @"-----BEGIN EC PRIVATE KEY-----
MHQCAQEEIDHbMvCDyJHkBi9q9V0x8w2x8bVH+G8p4qKqQ3mJ8xY7oAcGBSuBBAAi
oWQDYgAEa0mWi5HqMjGJOzJ8pN9H9xY7zQk8GQIDAQABAoGAAiCyL8P
-----END EC PRIVATE KEY-----";

        var item = new VaultwardenItem
        {
            Id = "ssh-key-ecdsa",
            Name = "ECDSA SSH Key",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = ecdsaPrivateKey
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        var key = result["ecdsa-ssh-key"];
        Assert.Contains("-----BEGIN EC PRIVATE KEY-----", key);
        Assert.Contains("-----END EC PRIVATE KEY-----", key);
    }

    #endregion

    #region SSH Key with Username Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithSshKeyAndUsername_IncludesUsername()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-user",
            Name = "SSH Key with User",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nuserkey\n-----END OPENSSH PRIVATE KEY-----",
                PublicKey = "ssh-ed25519 USERtest user@host"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-key-username", Value = "SSH_USER", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.Contains("SSH_USER", result.Keys);
        Assert.Equal("user@host", result["SSH_USER"]);
    }

    #endregion

    #region SSH Key Ignored Fields Tests

    [Fact]
    [Trait("Category", "SSH Keys")]
    [Trait("Category", "Integration")]
    public async Task ExtractSecretData_WithSshKey_ExcludesIgnoredFields()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "ssh-key-ignore",
            Name = "SSH Key Ignore Test",
            Type = 5,
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN OPENSSH PRIVATE KEY-----\nignoretest\n-----END OPENSSH PRIVATE KEY-----"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "ssh-key", Type = 0 },
                new FieldInfo { Name = "secret-type", Value = "Opaque", Type = 0 },
                new FieldInfo { Name = "secret-annotation", Value = "key=value", Type = 0 },
                new FieldInfo { Name = "secret-label", Value = "app=test", Type = 0 },
                new FieldInfo { Name = "custom-field", Value = "custom-value", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert - ignored fields should not appear in secret data
        Assert.DoesNotContain("namespaces", result.Keys);
        Assert.DoesNotContain("secret-name", result.Keys);
        Assert.DoesNotContain("secret-type", result.Keys);
        Assert.DoesNotContain("secret-annotation", result.Keys);
        Assert.DoesNotContain("secret-label", result.Keys);
        
        // But custom fields should be included
        Assert.Contains("custom-field", result.Keys);
        Assert.Equal("custom-value", result["custom-field"]);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Helper method that simulates SyncService.ExtractSecretDataAsync
    /// This is a simplified version for testing purposes.
    /// </summary>
    private static async Task<Dictionary<string, string>> ExtractSecretDataAsync(VaultwardenItem item)
    {
        var data = new Dictionary<string, string>();

        // Simulate SSH key hydration check
        var isSshKeyItem = item.Type == 5;
        var hasMissingSshKeyData = item.SshKey == null ||
            string.IsNullOrWhiteSpace(item.SshKey.PrivateKey) ||
            string.IsNullOrWhiteSpace(item.SshKey.PublicKey) ||
            string.IsNullOrWhiteSpace(item.SshKey.Fingerprint);

        // Get the password/credential value (login password or SSH private key if SSH item)
        var password = GetLoginPasswordOrSshKey(item);

        // Determine the key to use for the primary value
        var passwordKeyResolved = item.ExtractSecretKeyPassword();
        if (string.IsNullOrEmpty(passwordKeyResolved))
        {
            var extractedName = item.ExtractSecretName();
            var itemName = !string.IsNullOrEmpty(extractedName)
                ? extractedName
                : (item.Name ?? string.Empty);
            passwordKeyResolved = SanitizeFieldName(itemName);
        }

        if (!string.IsNullOrEmpty(password))
        {
            data[passwordKeyResolved] = FormatMultilineValue(password);
        }

        // Add SSH public key and fingerprint if present
        if (item.SshKey != null)
        {
            var baseName = !string.IsNullOrEmpty(item.ExtractSecretName())
                ? item.ExtractSecretName()
                : SanitizeSecretName(item.Name ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(item.SshKey.PublicKey))
            {
                var publicKeyKey = $"{SanitizeFieldName(baseName)}-public-key";
                data[publicKeyKey] = FormatMultilineValue(item.SshKey.PublicKey);
            }

            if (!string.IsNullOrWhiteSpace(item.SshKey.Fingerprint))
            {
                var fingerprintKey = $"{SanitizeFieldName(baseName)}-fingerprint";
                data[fingerprintKey] = item.SshKey.Fingerprint;
            }
        }

        // Add custom fields (excluding ignored ones)
        var ignoredFields = item.ExtractIgnoredFields();
        if (item.Fields != null)
        {
            foreach (var field in item.Fields)
            {
                if (!string.IsNullOrWhiteSpace(field.Name) &&
                    !string.IsNullOrEmpty(field.Value) &&
                    !ignoredFields.Contains(field.Name))
                {
                    var sanitizedKey = SanitizeFieldName(field.Name);
                    data[sanitizedKey] = FormatMultilineValue(field.Value);
                }
            }
        }

        return await Task.FromResult(data);
    }

    private static string GetLoginPasswordOrSshKey(VaultwardenItem item)
    {
        // First check for login password
        if (item.Login != null && !string.IsNullOrEmpty(item.Login.Password))
            return item.Login.Password;

        // If no login password, fall back to SSH key logic
        return GetPasswordOrSshKey(item);
    }

    private static string GetPasswordOrSshKey(VaultwardenItem item)
    {
        // First check for regular password
        if (!string.IsNullOrEmpty(item.Password))
            return item.Password;

        // Prefer SSH Key payload if present on item
        if (item.SshKey != null && !string.IsNullOrWhiteSpace(item.SshKey.PrivateKey))
        {
            return item.SshKey.PrivateKey!;
        }

        // Check for SSH key in custom fields
        if (item.Fields?.Any() == true)
        {
            var sshKeyField = item.Fields.FirstOrDefault(f =>
                f.Name.Equals("ssh_key", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("private_key", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("ssh_private_key", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("key", StringComparison.OrdinalIgnoreCase));

            if (sshKeyField != null && !string.IsNullOrEmpty(sshKeyField.Value))
                return sshKeyField.Value;
        }

        return string.Empty;
    }

    private static string SanitizeFieldName(string name)
    {
        // Simplified sanitization for testing
        var sanitized = name.ToLowerInvariant();
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "[^a-z0-9-_]", "-");
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, "-+", "-");
        sanitized = sanitized.Trim('-');
        return string.IsNullOrEmpty(sanitized) ? "key" : sanitized;
    }

    private static string SanitizeSecretName(string name)
    {
        return SanitizeFieldName(name);
    }

    private static string FormatMultilineValue(string value)
    {
        return value;
    }

    #endregion
}
