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
