# Persistent Device ID Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Eliminate "New device logged in" notifications by using a persistent device identifier instead of generating a new random GUID on each authentication.

**Architecture:** Add optional `VAULTWARDEN__DEVICEID` environment variable. If not set, generate a deterministic device ID by hashing `{ServerUrl}:{ClientId}` using SHA256. This ensures the same deployment always uses the same device ID without requiring additional configuration.

**Tech Stack:** C# / .NET 10.0, xUnit for testing

---

### Task 1: Add DeviceId Property to VaultwardenSettings

**Files:**
- Modify: `VaultwardenK8sSync/AppSettings.cs:80-110`

**Step 1: Add DeviceId property to VaultwardenSettings class**

In `VaultwardenK8sSync/AppSettings.cs`, add after line 104 (after `CollectionName`):

```csharp
    // Optional: persistent device identifier to prevent "New device logged in" notifications
    public string? DeviceId { get; set; }
```

**Step 2: Add environment variable binding in FromEnvironment()**

In `VaultwardenK8sSync/AppSettings.cs`, add after line 29 (after `CollectionName` binding):

```csharp
                DeviceId = Environment.GetEnvironmentVariable("VAULTWARDEN__DEVICEID"),
```

**Step 3: Run build to verify no syntax errors**

Run: `dotnet build VaultwardenK8sSync/VaultwardenK8sSync.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add VaultwardenK8sSync/AppSettings.cs
git commit -m "feat: add DeviceId property to VaultwardenSettings"
```

---

### Task 2: Write Failing Tests for GetOrGenerateDeviceId

**Files:**
- Create: `VaultwardenK8sSync.Tests/DeviceIdTests.cs`

**Step 1: Create the test file with failing tests**

Create `VaultwardenK8sSync.Tests/DeviceIdTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace VaultwardenK8sSync.Tests;

[Collection("DeviceId Tests")]
[Trait("Category", "DeviceId")]
public class DeviceIdTests
{
    [Fact]
    [Trait("Category", "DeviceId")]
    public void GetOrGenerateDeviceId_WithExplicitDeviceId_ShouldReturnExplicitValue()
    {
        // Arrange
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://vault.example.com",
            ClientId = "user.abc123",
            DeviceId = "explicit-device-id-12345"
        };

        // Act
        var result = DeviceIdGenerator.GetOrGenerateDeviceId(config);

        // Assert
        Assert.Equal("explicit-device-id-12345", result);
    }

    [Theory]
    [Trait("Category", "DeviceId")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOrGenerateDeviceId_WithoutExplicitDeviceId_ShouldGenerateDeterministicId(string? deviceId)
    {
        // Arrange
        var config = new VaultwardenSettings
        {
            ServerUrl = "https://vault.example.com",
            ClientId = "user.abc123",
            DeviceId = deviceId
        };

        // Act
        var result1 = DeviceIdGenerator.GetOrGenerateDeviceId(config);
        var result2 = DeviceIdGenerator.GetOrGenerateDeviceId(config);

        // Assert
        Assert.Equal(result1, result2); // Same input = same output
        Assert.Matches(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", result1); // Valid GUID format
    }

    [Fact]
    [Trait("Category", "DeviceId")]
    public void GetOrGenerateDeviceId_DifferentConfigs_ShouldGenerateDifferentIds()
    {
        // Arrange
        var config1 = new VaultwardenSettings
        {
            ServerUrl = "https://vault1.example.com",
            ClientId = "user.abc123"
        };
        var config2 = new VaultwardenSettings
        {
            ServerUrl = "https://vault2.example.com",
            ClientId = "user.abc123"
        };

        // Act
        var result1 = DeviceIdGenerator.GetOrGenerateDeviceId(config1);
        var result2 = DeviceIdGenerator.GetOrGenerateDeviceId(config2);

        // Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    [Trait("Category", "DeviceId")]
    public void GetOrGenerateDeviceId_SameConfigDifferentInstances_ShouldGenerateSameId()
    {
        // Arrange - simulate two different pod restarts with same config
        var config1 = new VaultwardenSettings
        {
            ServerUrl = "https://vault.example.com",
            ClientId = "user.abc123"
        };
        var config2 = new VaultwardenSettings
        {
            ServerUrl = "https://vault.example.com",
            ClientId = "user.abc123"
        };

        // Act
        var result1 = DeviceIdGenerator.GetOrGenerateDeviceId(config1);
        var result2 = DeviceIdGenerator.GetOrGenerateDeviceId(config2);

        // Assert
        Assert.Equal(result1, result2);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj --filter "Category=DeviceId" -v n`
Expected: FAIL with "DeviceIdGenerator does not exist"

**Step 3: Commit failing tests**

```bash
git add VaultwardenK8sSync.Tests/DeviceIdTests.cs
git commit -m "test: add failing tests for DeviceIdGenerator"
```

---

### Task 3: Implement DeviceIdGenerator

**Files:**
- Create: `VaultwardenK8sSync/Services/DeviceIdGenerator.cs`

**Step 1: Create DeviceIdGenerator class**

Create `VaultwardenK8sSync/Services/DeviceIdGenerator.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace VaultwardenK8sSync;

/// <summary>
/// Generates or retrieves a persistent device identifier for Vaultwarden authentication.
/// Using a consistent device ID prevents "New device logged in" notifications.
/// </summary>
public static class DeviceIdGenerator
{
    /// <summary>
    /// Gets the device ID from configuration or generates a deterministic one.
    /// </summary>
    /// <param name="config">Vaultwarden settings containing optional DeviceId and required ServerUrl/ClientId</param>
    /// <returns>A device identifier string</returns>
    public static string GetOrGenerateDeviceId(VaultwardenSettings config)
    {
        // Use explicit config if provided
        if (!string.IsNullOrWhiteSpace(config.DeviceId))
            return config.DeviceId;

        // Generate deterministic ID from server URL + client ID
        var input = $"{config.ServerUrl}:{config.ClientId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        // Format as GUID (use first 16 bytes of hash)
        return new Guid(hash.Take(16).ToArray()).ToString();
    }
}
```

**Step 2: Run tests to verify they pass**

Run: `dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj --filter "Category=DeviceId" -v n`
Expected: PASS (4 tests)

**Step 3: Commit implementation**

```bash
git add VaultwardenK8sSync/Services/DeviceIdGenerator.cs
git commit -m "feat: implement DeviceIdGenerator for persistent device IDs"
```

---

### Task 4: Integrate DeviceIdGenerator into VaultwardenService

**Files:**
- Modify: `VaultwardenK8sSync/Services/VaultwardenService.cs:111-115`

**Step 1: Replace random GUID with DeviceIdGenerator call**

In `VaultwardenK8sSync/Services/VaultwardenService.cs`, replace lines 111-115:

```csharp
            // User API keys need device info, organization API keys don't
            if (!isOrgApiKey)
            {
                formParams["deviceType"] = "6";
                formParams["deviceIdentifier"] = Guid.NewGuid().ToString();
                formParams["deviceName"] = "vaultwarden-k8s-sync";
            }
```

With:

```csharp
            // User API keys need device info, organization API keys don't
            if (!isOrgApiKey)
            {
                var deviceId = DeviceIdGenerator.GetOrGenerateDeviceId(_config);
                _logger.LogDebug("Using device identifier: {DeviceIdPrefix}...", deviceId[..8]);

                formParams["deviceType"] = "6";
                formParams["deviceIdentifier"] = deviceId;
                formParams["deviceName"] = "vaultwarden-k8s-sync";
            }
```

**Step 2: Run full test suite to verify no regressions**

Run: `dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj`
Expected: All tests pass

**Step 3: Run build to verify no errors**

Run: `dotnet build VaultwardenK8sSync.sln`
Expected: Build succeeded

**Step 4: Commit integration**

```bash
git add VaultwardenK8sSync/Services/VaultwardenService.cs
git commit -m "feat: use persistent device ID in VaultwardenService authentication"
```

---

### Task 5: Update Helm Chart values.yaml

**Files:**
- Modify: `charts/vaultwarden-kubernetes-secrets/values.yaml:141-148`

**Step 1: Add deviceId configuration option**

In `charts/vaultwarden-kubernetes-secrets/values.yaml`, add after line 147 (after `VAULTWARDEN__COLLECTIONNAME`):

```yaml
    # Optional: Fixed device identifier to prevent "New device logged in" notifications
    # If not set, a deterministic ID is auto-generated from SERVERURL + BW_CLIENTID
    # VAULTWARDEN__DEVICEID: ""
```

**Step 2: Commit Helm chart update**

```bash
git add charts/vaultwarden-kubernetes-secrets/values.yaml
git commit -m "docs: add DEVICEID option to Helm chart values"
```

---

### Task 6: Update README Documentation

**Files:**
- Modify: `README.md`

**Step 1: Add DEVICEID to environment variables documentation**

Find the environment variables section in README.md and add documentation for the new variable. Add in the optional configuration section:

```markdown
### Preventing "New Device Logged In" Notifications

By default, the sync service generates a deterministic device identifier from your server URL and client ID. This prevents Vaultwarden from sending "New device logged in" notifications on every sync.

If you need to set an explicit device ID (e.g., for migration or specific requirements):

```bash
VAULTWARDEN__DEVICEID="your-fixed-device-id"
```
```

**Step 2: Commit documentation update**

```bash
git add README.md
git commit -m "docs: document persistent device ID feature in README"
```

---

### Task 7: Final Verification

**Step 1: Run full test suite**

Run: `dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj -v n`
Expected: All tests pass

**Step 2: Build all projects**

Run: `dotnet build VaultwardenK8sSync.sln`
Expected: Build succeeded with no warnings

**Step 3: Verify Docker build**

Run: `docker build -f VaultwardenK8sSync/Dockerfile -t vaultwarden-kubernetes-secrets:test .`
Expected: Build completes successfully

**Step 4: Review all commits**

Run: `git log --oneline -10`
Expected: See commits for all tasks in order
