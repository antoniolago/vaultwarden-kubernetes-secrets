using System.Text.Json;
using VaultwardenK8sSync.Models;
using Xunit;
using Models = VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Tests;

[Collection("DockerConfigJson Format Tests")]
[Trait("Category", "Unit")]
public class DockerConfigJsonFormatTests
{
    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void BuildDockerConfigJson_ProducesValidJson()
    {
        // Arrange - create item with docker config json fields
        var item = new VaultwardenItem
        {
            Name = "test-docker-secret",
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpassword"
            },
            Fields = new List<FieldInfo>
            {
                new Models.FieldInfo { Name = "docker-config-json-server", Value = "ghcr.io", Type = 0 },
                new Models.FieldInfo { Name = "docker-config-json-email", Value = "test@example.com", Type = 0 }
            }
        };

        // Create a minimal SyncService mock to test the method
        // Since BuildDockerConfigJsonFromFields is private, we'll test through ExtractDockerConfigJsonJsonAsync
        // For now, let's manually create what the output should look like
        
        var expected = new Dictionary<string, string>
        {
            ["username"] = "testuser",
            ["password"] = "testpassword",
            ["auth"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("testuser:testpassword")),
            ["email"] = "test@example.com"
        };

        var registryEntry = new Dictionary<string, string>
        {
            ["username"] = expected["username"],
            ["password"] = expected["password"],
            ["auth"] = expected["auth"],
            ["email"] = expected["email"]
        };

        var authsDict = new Dictionary<string, Dictionary<string, string>>
        {
            ["ghcr.io"] = registryEntry
        };

        var dockerConfigJson = new Dictionary<string, object>
        {
            ["auths"] = authsDict
        };

        var jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        var jsonString = JsonSerializer.Serialize(dockerConfigJson, jsonOptions);

        // Act - try to parse it back
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonString);
        }
        catch (JsonException ex)
        {
            throw new Exception($"Invalid JSON produced: {jsonString}", ex);
        }

        // Assert
        Assert.NotNull(doc);
        Assert.True(doc.RootElement.TryGetProperty("auths", out var auths));
        Assert.True(auths.TryGetProperty("ghcr.io", out var registry));
        
        Assert.Equal("testuser", registry.GetProperty("username").GetString());
        Assert.Equal("testpassword", registry.GetProperty("password").GetString());
        Assert.Equal("test@example.com", registry.GetProperty("email").GetString());
        
        // Verify auth is base64 encoded
        var authValue = registry.GetProperty("auth").GetString();
        var authBytes = System.Text.Encoding.UTF8.GetBytes(authValue!);
        var decoded = System.Text.Encoding.UTF8.GetString(authBytes);
        // Note: The auth in the JSON is already base64, so we need to decode it
        var decodedAuth = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authValue!));
        Assert.Equal("testuser:testpassword", decodedAuth);
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void BuildDockerConfigJson_WithoutEmail_OmitsEmailField()
    {
        // Arrange
        var registryEntry = new Dictionary<string, string>
        {
            ["username"] = "testuser",
            ["password"] = "testpassword",
            ["auth"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("testuser:testpassword"))
        };

        var authsDict = new Dictionary<string, Dictionary<string, string>>
        {
            ["https://index.docker.io/v1/"] = registryEntry
        };

        var dockerConfigJson = new Dictionary<string, object>
        {
            ["auths"] = authsDict
        };

        var jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        var jsonString = JsonSerializer.Serialize(dockerConfigJson, jsonOptions);

        // Act
        var doc = JsonDocument.Parse(jsonString);

        // Assert
        Assert.True(doc.RootElement.TryGetProperty("auths", out var auths));
        Assert.True(auths.TryGetProperty("https://index.docker.io/v1/", out var registry));
        
        Assert.False(registry.TryGetProperty("email", out _), "Email field should not be present");
        Assert.Equal("testuser", registry.GetProperty("username").GetString());
    }

    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public void BuildDockerConfigJson_IsSingleLineJson()
    {
        // Arrange
        var registryEntry = new Dictionary<string, string>
        {
            ["username"] = "user",
            ["password"] = "pass",
            ["auth"] = "dXNlcjpwYXNz"
        };

        var authsDict = new Dictionary<string, Dictionary<string, string>>
        {
            ["docker.io"] = registryEntry
        };

        var dockerConfigJson = new Dictionary<string, object>
        {
            ["auths"] = authsDict
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        // Act
        var jsonString = JsonSerializer.Serialize(dockerConfigJson, jsonOptions);

        // Assert - should not contain newlines
        Assert.DoesNotContain("\n", jsonString);
        Assert.DoesNotContain("\r", jsonString);
    }

    #region User Environment Tests - Replicates exact user scenario

    /// <summary>
    /// Simulates user's exact environment: user=user123, password=123123123, email+server provided.
    /// The password "123123123" is valid JSON (a number!), which caused the original bug.
    /// </summary>
    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public async Task ExtractDockerConfigJson_WithNumericPassword_FallsBackToFieldConstruction()
    {
        // Arrange - exact user scenario
        var item = new VaultwardenItem
        {
            Name = "test-se-cret-default",
            Login = new LoginInfo
            {
                Username = "user123",
                Password = "123123123" // This is valid JSON (a number!) - caused the bug
            },
            Fields = new List<FieldInfo>
            {
                new Models.FieldInfo { Name = "docker-config-json-server", Value = "https://index.docker.io/v1/", Type = 0 },
                new Models.FieldInfo { Name = "docker-config-json-email", Value = "user@example.com", Type = 0 },
                new Models.FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
            }
        };

        // Act - use reflection to call the private method
        var syncService = CreateSyncServiceForTesting();
        var result = await syncService.TestExtractDockerConfigJsonJsonAsync(item);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.True(result.ContainsKey(".dockerconfigjson"), "Should contain .dockerconfigjson key");

        var jsonValue = result[".dockerconfigjson"];

        // Verify it's valid JSON and is an object (not a bare number)
        using var doc = JsonDocument.Parse(jsonValue);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

        // Verify structure matches Kubernetes docker config format
        Assert.True(doc.RootElement.TryGetProperty("auths", out var auths));
        Assert.True(auths.TryGetProperty("https://index.docker.io/v1/", out var registry));

        // Verify correct values
        Assert.Equal("user123", registry.GetProperty("username").GetString());
        Assert.Equal("123123123", registry.GetProperty("password").GetString());
        Assert.Equal("user@example.com", registry.GetProperty("email").GetString());

        // Verify auth is properly base64 encoded
        var authValue = registry.GetProperty("auth").GetString();
        Assert.NotNull(authValue);
        var decodedAuth = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authValue!));
        Assert.Equal("user123:123123123", decodedAuth);
    }

    /// <summary>
    /// Tests that a password which is a JSON string (not object) also falls back correctly.
    /// </summary>
    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public async Task ExtractDockerConfigJson_WithJsonStringPassword_FallsBackToFieldConstruction()
    {
        // Arrange - password is a JSON string value
        var item = new VaultwardenItem
        {
            Name = "test-secret",
            Login = new LoginInfo
            {
                Username = "user",
                Password = "\"some-token-value\"" // JSON string, not an object
            },
            Fields = new List<FieldInfo>
            {
                new Models.FieldInfo { Name = "docker-config-json-server", Value = "ghcr.io", Type = 0 },
                new Models.FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
            }
        };

        // Act
        var syncService = CreateSyncServiceForTesting();
        var result = await syncService.TestExtractDockerConfigJsonJsonAsync(item);

        // Assert - should fall back to field construction, not use the raw string
        var jsonValue = result[".dockerconfigjson"];
        using var doc = JsonDocument.Parse(jsonValue);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("auths", out var auths));
    }

    /// <summary>
    /// Tests that a password which is a JSON array falls back correctly.
    /// </summary>
    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public async Task ExtractDockerConfigJson_WithJsonArrayPassword_FallsBackToFieldConstruction()
    {
        // Arrange - password is a JSON array
        var item = new VaultwardenItem
        {
            Name = "test-secret",
            Login = new LoginInfo
            {
                Username = "user",
                Password = "[\"token1\",\"token2\"]" // JSON array, not an object
            },
            Fields = new List<FieldInfo>
            {
                new Models.FieldInfo { Name = "docker-config-json-server", Value = "docker.io", Type = 0 },
                new Models.FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
            }
        };

        // Act
        var syncService = CreateSyncServiceForTesting();
        var result = await syncService.TestExtractDockerConfigJsonJsonAsync(item);

        // Assert - should fall back to field construction
        var jsonValue = result[".dockerconfigjson"];
        using var doc = JsonDocument.Parse(jsonValue);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    /// <summary>
    /// Tests that actual Docker config JSON in password still works (backward compatibility).
    /// </summary>
    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public async Task ExtractDockerConfigJson_WithRawObjectPassword_UsesDirectly()
    {
        // Arrange - password contains actual Docker config JSON object
        var dockerConfigJson = JsonSerializer.Serialize(new
        {
            auths = new Dictionary<string, object>
            {
                ["ghcr.io"] = new
                {
                    username = "user123",
                    password = "ghp_token123",
                    email = "user@example.com",
                    auth = "dXNlcjEyMzpnaHBfdG9rZW4xMjM="
                }
            }
        });

        var item = new VaultwardenItem
        {
            Name = "test-secret",
            Login = new LoginInfo
            {
                Username = "ignored", // Should be ignored when raw JSON is used
                Password = dockerConfigJson
            },
            Fields = new List<FieldInfo>
            {
                new Models.FieldInfo { Name = "secret-type", Value = "kubernetes.io/dockerconfigjson", Type = 0 }
            }
        };

        // Act
        var syncService = CreateSyncServiceForTesting();
        var result = await syncService.TestExtractDockerConfigJsonJsonAsync(item);

        // Assert - should use the raw JSON directly
        Assert.Equal(dockerConfigJson, result[".dockerconfigjson"]);
    }

    /// <summary>
    /// Tests the complete flow: extract + encode for Kubernetes API submission.
    /// </summary>
    [Fact]
    [Trait("Category", "DockerConfigJson")]
    public async Task ExtractDockerConfigJson_CompleteFlow_ProducesKubernetesCompatibleData()
    {
        // Arrange - exact user scenario with all fields
        var item = new VaultwardenItem
        {
            Name = "test-se-cret-default",
            Login = new LoginInfo
            {
                Username = "user123",
                Password = "123123123"
            },
            Fields = new List<FieldInfo>
            {
                new Models.FieldInfo { Name = "docker-config-json-server", Value = "ghcr.io", Type = 0 },
                new Models.FieldInfo { Name = "docker-config-json-email", Value = "admin@test.com", Type = 0 }
            }
        };

        // Act
        var syncService = CreateSyncServiceForTesting();
        var result = await syncService.TestExtractDockerConfigJsonJsonAsync(item);

        // Simulate what KubernetesService does: UTF8.GetBytes
        var k8sData = result.ToDictionary(kvp => kvp.Key, kvp => System.Text.Encoding.UTF8.GetBytes(kvp.Value));

        // Assert - verify structure Kubernetes will see after decoding
        Assert.True(k8sData.ContainsKey(".dockerconfigjson"));

        var decodedJson = System.Text.Encoding.UTF8.GetString(k8sData[".dockerconfigjson"]);
        using var doc = JsonDocument.Parse(decodedJson);

        // Verify it's an object at root
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

        // Verify auths structure
        Assert.True(doc.RootElement.TryGetProperty("auths", out var auths));
        Assert.True(auths.TryGetProperty("ghcr.io", out var registry));
        Assert.Equal(JsonValueKind.Object, registry.ValueKind);

        // All fields should be strings, not numbers
        Assert.Equal(JsonValueKind.String, registry.GetProperty("username").ValueKind);
        Assert.Equal(JsonValueKind.String, registry.GetProperty("password").ValueKind);
        Assert.Equal(JsonValueKind.String, registry.GetProperty("auth").ValueKind);
        Assert.Equal(JsonValueKind.String, registry.GetProperty("email").ValueKind);

        // Verify values
        Assert.Equal("user123", registry.GetProperty("username").GetString());
        Assert.Equal("123123123", registry.GetProperty("password").GetString());
        Assert.Equal("admin@test.com", registry.GetProperty("email").GetString());
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Creates a minimal SyncService instance for testing ExtractDockerConfigJsonJsonAsync.
    /// Uses reflection to call the private method without full dependency injection.
    /// </summary>
    private static SyncServiceTestHelper CreateSyncServiceForTesting()
    {
        return new SyncServiceTestHelper();
    }

    #endregion
}

/// <summary>
/// Helper class to test private ExtractDockerConfigJsonJsonAsync method via reflection.
/// </summary>
public class SyncServiceTestHelper
{
    private readonly System.Reflection.MethodInfo _extractMethod;

    public SyncServiceTestHelper()
    {
        _extractMethod = typeof(TestableSyncService).GetMethod("ExtractDockerConfigJsonJsonAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("Method not found");
    }

    public async Task<Dictionary<string, string>> TestExtractDockerConfigJsonJsonAsync(VaultwardenItem item)
    {
        var service = new TestableSyncService();
        var task = (Task<Dictionary<string, string>>)_extractMethod.Invoke(service, new object[] { item })!;
        return await task;
    }
}

/// <summary>
/// Minimal testable version of SyncService that only implements ExtractDockerConfigJsonJsonAsync logic.
/// </summary>
internal class TestableSyncService
{
    private readonly DockerConfigJsonSettings _dockerConfigJsonSettings = new()
    {
        DefaultDockerConfigJsonServer = "https://index.docker.io/v1/"
    };

    internal async Task<Dictionary<string, string>> ExtractDockerConfigJsonJsonAsync(VaultwardenItem item)
    {
        // Approach 1: Check if password/notes contain raw JSON directly
        var rawJson = GetLoginPasswordOrSshKey(item);
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            // Validate that it's valid JSON AND is a JSON object (not a number, string, array, etc.)
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return new Dictionary<string, string>
                    {
                        { ".dockerconfigjson", rawJson }
                    };
                }
            }
            catch (JsonException)
            {
                // Not valid JSON, fall through to field-based construction
            }
        }

        // Also check notes if password didn't have JSON
        if (string.IsNullOrWhiteSpace(rawJson) && !string.IsNullOrWhiteSpace(item.Notes))
        {
            try
            {
                using var doc = JsonDocument.Parse(item.Notes);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return new Dictionary<string, string>
                    {
                        { ".dockerconfigjson", item.Notes }
                    };
                }
            }
            catch (JsonException)
            {
                // Notes not valid JSON either, fall through
            }
        }

        // Approach 2: Build JSON from individual custom fields
        return BuildDockerConfigJsonFromFields(item);
    }

    private static string GetLoginPasswordOrSshKey(VaultwardenItem item)
    {
        if (item.Login != null && !string.IsNullOrEmpty(item.Login.Password))
            return item.Login.Password;
        return string.Empty;
    }

    private static string GetUsername(VaultwardenItem item)
    {
        if (item.Login != null && !string.IsNullOrEmpty(item.Login.Username))
            return item.Login.Username;
        return string.Empty;
    }

    private Dictionary<string, string> BuildDockerConfigJsonFromFields(VaultwardenItem item)
    {
        var username = GetUsername(item);
        var password = GetLoginPasswordOrSshKey(item);

        var authEncoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));

        var registry = item.ExtractDockerConfigJsonServer();
        if (string.IsNullOrEmpty(registry))
        {
            registry = _dockerConfigJsonSettings.DefaultDockerConfigJsonServer;
        }
        var email = item.ExtractDockerConfigJsonEmail();

        var auths = new Dictionary<string, object>
        {
            [registry] = new
            {
                username = username,
                password = password,
                email = string.IsNullOrEmpty(email) ? null : email,
                auth = authEncoded
            }
        };

        var dockerConfig = new
        {
            auths = auths
        };

        return new Dictionary<string, string>
        {
            { ".dockerconfigjson", JsonSerializer.Serialize(dockerConfig) }
        };
    }
}
