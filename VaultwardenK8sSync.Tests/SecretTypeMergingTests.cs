using VaultwardenK8sSync.Models;
using Xunit;

namespace VaultwardenK8sSync.Tests;

[Collection("Secret Type Merging Tests")]
[Trait("Category", "Unit")]
public class SecretTypeMergingTests
{
    #region ExtractSecretType Tests

    [Fact]
    [Trait("Category", "SecretType")]
    public void ExtractSecretType_NoField_ReturnsOpaque()
    {
        var item = new VaultwardenItem
        {
            Name = "test-secret",
            Fields = new List<FieldInfo>()
        };

        var result = item.ExtractSecretType();

        Assert.Equal("Opaque", result);
    }

    [Fact]
    [Trait("Category", "SecretType")]
    public void ExtractSecretType_Opaque_ReturnsOpaque()
    {
        var item = new VaultwardenItem
        {
            Name = "test-secret",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-type", Value = "Opaque", Type = 0 }
            }
        };

        var result = item.ExtractSecretType();

        Assert.Equal("Opaque", result);
    }

    [Fact]
    [Trait("Category", "SecretType")]
    public void ExtractSecretType_DockerConfigJson_ReturnsType()
    {
        var item = new VaultwardenItem
        {
            Name = "test-secret",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
            }
        };

        var result = item.ExtractSecretType();

        Assert.Equal("kubernetes.io/dockerconfigjson", result);
    }

    [Fact]
    [Trait("Category", "SecretType")]
    public void ExtractSecretType_TLS_ReturnsType()
    {
        var item = new VaultwardenItem
        {
            Name = "test-secret",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/tls", Type = 0 }
            }
        };

        var result = item.ExtractSecretType();

        Assert.Equal("kubernetes.io/tls", result);
    }

    #endregion

    #region Secret Type Merging Logic Tests

    /// <summary>
    /// When all items are Opaque/default, the merged type should be Opaque.
    /// </summary>
    [Fact]
    [Trait("Category", "SecretType")]
    public void Merging_AllOpaqueItems_ResultIsOpaque()
    {
        // This tests the semantic: no non-default type -> secretType stays Opaque
        var items = new[]
        {
            new VaultwardenItem { Name = "item1", Fields = new List<FieldInfo>() },
            new VaultwardenItem { Name = "item2", Fields = new List<FieldInfo>() }
        };

        string? mergedType = null;
        foreach (var item in items)
        {
            var itemType = item.ExtractSecretType();
            if (itemType != FieldNameConfig.DefaultSecretType)
            {
                mergedType = itemType;
            }
        }

        Assert.Null(mergedType); // Should stay null = will resolve to Opaque at creation time
    }

    /// <summary>
    /// When one item is dockerconfigjson and others are Opaque, merged type should be dockerconfigjson.
    /// </summary>
    [Fact]
    [Trait("Category", "SecretType")]
    public void Merging_DockerConfigJsonWithOpaque_ResultIsDockerConfigJson()
    {
        var items = new[]
        {
            new VaultwardenItem { Name = "item1", Fields = new List<FieldInfo>() }, // Opaque
            new VaultwardenItem
            {
                Name = "item2",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
                }
            }
        };

        string? mergedType = null;
        foreach (var item in items)
        {
            var itemType = item.ExtractSecretType();
            if (itemType != FieldNameConfig.DefaultSecretType)
            {
                mergedType = itemType;
            }
        }

        Assert.Equal("kubernetes.io/dockerconfigjson", mergedType);
    }

    /// <summary>
    /// When items have conflicting non-default types, should reject/throw.
    /// </summary>
    [Fact]
    [Trait("Category", "SecretType")]
    public void Merging_ConflictingNonDefaultTypes_ShouldReject()
    {
        var items = new[]
        {
            new VaultwardenItem
            {
                Name = "item1",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
                }
            },
            new VaultwardenItem
            {
                Name = "item2",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "secret-type", Value = "kubernetes.io/tls", Type = 0 }
                }
            }
        };

        // Simulate the merging logic from SyncService
        string? mergedType = null;
        bool hasConflict = false;
        string? conflictMessage = null;

        foreach (var item in items)
        {
            var itemType = item.ExtractSecretType();
            if (itemType != FieldNameConfig.DefaultSecretType)
            {
                // This matches the SyncService logic: reject if mergedType is already
                // a non-default type AND it differs from the new item's type
                if (mergedType != null && mergedType != FieldNameConfig.DefaultSecretType && itemType != mergedType)
                {
                    hasConflict = true;
                    conflictMessage = $"Mixed non-default secret types are not allowed: existing type '{mergedType}' conflicts with item type '{itemType}'";
                    break;
                }
                mergedType = itemType;
            }
        }

        Assert.True(hasConflict);
        Assert.Contains("kubernetes.io/dockerconfigjson", conflictMessage!);
        Assert.Contains("kubernetes.io/tls", conflictMessage!);
    }

    /// <summary>
    /// Multiple items with the same non-default type should merge successfully.
    /// </summary>
    [Fact]
    [Trait("Category", "SecretType")]
    public void Merging_SameNonDefaultTypes_ShouldSucceed()
    {
        var items = new[]
        {
            new VaultwardenItem
            {
                Name = "item1",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
                }
            },
            new VaultwardenItem
            {
                Name = "item2",
                Fields = new List<FieldInfo>
                {
                    new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
                }
            }
        };

        string? mergedType = null;
        bool hasConflict = false;

        foreach (var item in items)
        {
            var itemType = item.ExtractSecretType();
            if (itemType != FieldNameConfig.DefaultSecretType)
            {
                // This matches the SyncService logic
                if (mergedType != null && mergedType != FieldNameConfig.DefaultSecretType && itemType != mergedType)
                {
                    hasConflict = true;
                    break;
                }
                mergedType = itemType;
            }
        }

        Assert.False(hasConflict);
        Assert.Equal("kubernetes.io/dockerconfigjson", mergedType);
    }

    #endregion

    #region Item-Level Parsing Tests

    /// <summary>
    /// Verifies that each item's ExtractSecretType is called individually,
    /// not using a previously merged type.
    /// </summary>
    [Fact]
    [Trait("Category", "SecretType")]
    public void EachItem_UsesItsOwnSecretType_NotMerged()
    {
        // Item 1 has dockerconfigjson, Item 2 has no type (Opaque)
        // Item 2 should be parsed as Opaque, NOT as dockerconfigjson
        var item1 = new VaultwardenItem
        {
            Name = "registry-creds",
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 },
                new FieldInfo { Name = "docker-config-json-server", Value = "ghcr.io", Type = 0 }
            }
        };

        var item2 = new VaultwardenItem
        {
            Name = "generic-creds",
            Login = new LoginInfo { Username = "user", Password = "pass" },
            Fields = new List<FieldInfo>
            {
                // No secret-type field - should be Opaque
                new FieldInfo { Name = "some-key", Value = "some-value", Type = 0 }
            }
        };

        // Verify each item returns its own type
        var type1 = item1.ExtractSecretType();
        var type2 = item2.ExtractSecretType();

        Assert.Equal("kubernetes.io/dockerconfigjson", type1);
        Assert.Equal("Opaque", type2); // Should be "Opaque" (default), NOT "kubernetes.io/dockerconfigjson"
    }

    #endregion
}
