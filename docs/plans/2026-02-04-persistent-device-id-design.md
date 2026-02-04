# Persistent Device ID Design

**Date:** 2026-02-04
**Issue:** Each sync triggers "New device logged in" notifications in Vaultwarden
**Reference:** https://github.com/dani-garcia/vaultwarden/discussions/6014

## Problem

When running in Kubernetes, every authentication generates a new random GUID as the device identifier (`Guid.NewGuid()`), causing Vaultwarden to treat each login as a new device and send notification emails.

## Solution

Use a persistent device identifier instead of generating a new one each time.

## Configuration

**New environment variable:**
- `VAULTWARDEN__DEVICEID` - Optional. Allows explicit control over the device identifier sent to Vaultwarden during authentication.

**Fallback behavior when not set:**
Generate a deterministic device ID by hashing `{ServerUrl}:{ClientId}` using SHA256, then formatting as a GUID-like string. This ensures:
- Same deployment always uses the same device ID
- Different deployments (different servers/accounts) get unique IDs
- No storage or additional configuration required

**Example usage:**
```bash
# Explicit (optional)
VAULTWARDEN__DEVICEID=550e8400-e29b-41d4-a716-446655440000

# Or let it auto-generate from your existing config (recommended)
# No action needed - deterministic ID derived from VAULTWARDEN__SERVERURL + BW_CLIENTID
```

## Implementation

### Changes to VaultwardenSettings (AppSettings.cs)

Add new optional property:
```csharp
public string? DeviceId { get; set; }
```

### Changes to VaultwardenService.cs

Replace line 113:
```csharp
formParams["deviceIdentifier"] = Guid.NewGuid().ToString();
```

With:
```csharp
formParams["deviceIdentifier"] = GetOrGenerateDeviceId();
```

Add new private method:
```csharp
private string GetOrGenerateDeviceId()
{
    // Use explicit config if provided
    if (!string.IsNullOrWhiteSpace(_config.DeviceId))
        return _config.DeviceId;

    // Generate deterministic ID from server URL + client ID
    var input = $"{_config.ServerUrl}:{_config.ClientId}";
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

    // Format as GUID (use first 16 bytes of hash)
    return new Guid(hash.Take(16).ToArray()).ToString();
}
```

**Logging:** Log the device ID (first 8 chars only) at startup so users can verify consistency across restarts.

## Documentation Updates

**README.md:**
- Add `VAULTWARDEN__DEVICEID` to the optional environment variables table
- Add a brief note explaining the "New device logged in" notification fix

**Helm chart (values.yaml):**
- Add `env.config.deviceId` as an optional value (commented out by default)

## Testing

1. **Unit test**: Verify `GetOrGenerateDeviceId()` returns consistent results for same input
2. **Unit test**: Verify explicit `DeviceId` config takes precedence over auto-generation
3. **Manual verification**: Deploy, restart pod multiple times, confirm no new device notifications

## Migration

- No breaking changes - existing deployments will automatically get a deterministic device ID
- First sync after upgrade will trigger one final "new device" notification (unavoidable), then notifications stop
