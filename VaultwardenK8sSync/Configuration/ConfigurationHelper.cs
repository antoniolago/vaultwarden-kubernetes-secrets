using Microsoft.Extensions.Configuration;

namespace VaultwardenK8sSync.Configuration;

public static class ConfigurationHelper
{
    /// <summary>
    /// Gets a configuration value with fallback support
    /// </summary>
    /// <param name="configuration">The configuration instance</param>
    /// <param name="key">The configuration key</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>The configuration value or default</returns>
    public static string GetValue(this IConfiguration configuration, string key, string defaultValue = "")
    {
        return configuration[key] ?? defaultValue;
    }

    /// <summary>
    /// Gets a boolean configuration value
    /// </summary>
    /// <param name="configuration">The configuration instance</param>
    /// <param name="key">The configuration key</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>The boolean configuration value or default</returns>
    public static bool GetBoolValue(this IConfiguration configuration, string key, bool defaultValue = false)
    {
        var value = configuration[key];
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Gets an integer configuration value
    /// </summary>
    /// <param name="configuration">The configuration instance</param>
    /// <param name="key">The configuration key</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>The integer configuration value or default</returns>
    public static int GetIntValue(this IConfiguration configuration, string key, int defaultValue = 0)
    {
        var value = configuration[key];
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Validates that required configuration values are present
    /// </summary>
    /// <param name="configuration">The configuration instance</param>
    /// <param name="requiredKeys">List of required configuration keys</param>
    /// <returns>True if all required keys are present, false otherwise</returns>
    public static bool ValidateRequiredConfiguration(this IConfiguration configuration, params string[] requiredKeys)
    {
        foreach (var key in requiredKeys)
        {
            if (string.IsNullOrEmpty(configuration[key]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Gets missing required configuration keys
    /// </summary>
    /// <param name="configuration">The configuration instance</param>
    /// <param name="requiredKeys">List of required configuration keys</param>
    /// <returns>List of missing configuration keys</returns>
    public static List<string> GetMissingConfigurationKeys(this IConfiguration configuration, params string[] requiredKeys)
    {
        var missingKeys = new List<string>();
        foreach (var key in requiredKeys)
        {
            if (string.IsNullOrEmpty(configuration[key]))
            {
                missingKeys.Add(key);
            }
        }
        return missingKeys;
    }
} 