using Microsoft.Extensions.Logging;

namespace VaultwardenK8sSync.Configuration;

public static class LogLevelParser
{
    public static LogLevel Parse(string? configuredLevel)
    {
        if (string.IsNullOrWhiteSpace(configuredLevel))
        {
            return LogLevel.Information;
        }
        
        if (Enum.TryParse<LogLevel>(configuredLevel, ignoreCase: true, out var level))
        {
            return level;
        }
        
        return LogLevel.Information;
    }
}
