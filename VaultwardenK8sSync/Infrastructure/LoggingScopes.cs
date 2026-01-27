using Serilog.Context;

namespace VaultwardenK8sSync.Infrastructure;

/// <summary>
/// Provides structured logging context scopes for correlation across log entries.
/// Uses Serilog's LogContext to push properties that appear in all nested log entries.
/// </summary>
public static class LoggingScopes
{
    /// <summary>
    /// Begins a sync-level scope with a unique SyncId and sync count.
    /// All logs within this scope will include SyncId and SyncNumber properties.
    /// </summary>
    /// <param name="syncNumber">The sequential sync number</param>
    /// <returns>A disposable that removes the scope when disposed</returns>
    public static IDisposable BeginSyncScope(int syncNumber)
    {
        var syncId = Guid.NewGuid().ToString("N")[..8];
        return new CompositeDisposable(
            LogContext.PushProperty("SyncId", syncId),
            LogContext.PushProperty("SyncNumber", syncNumber)
        );
    }

    /// <summary>
    /// Begins a namespace-level scope.
    /// All logs within this scope will include the Namespace property.
    /// </summary>
    /// <param name="ns">The Kubernetes namespace being processed</param>
    /// <returns>A disposable that removes the scope when disposed</returns>
    public static IDisposable BeginNamespaceScope(string ns)
        => LogContext.PushProperty("Namespace", ns);

    /// <summary>
    /// Begins a secret-level scope.
    /// All logs within this scope will include SecretName and optionally ItemId properties.
    /// </summary>
    /// <param name="secretName">The name of the Kubernetes secret</param>
    /// <param name="itemId">Optional Vaultwarden item ID</param>
    /// <returns>A disposable that removes the scope when disposed</returns>
    public static IDisposable BeginSecretScope(string secretName, string? itemId = null)
    {
        var disposables = new List<IDisposable> { LogContext.PushProperty("SecretName", secretName) };
        if (itemId != null)
        {
            disposables.Add(LogContext.PushProperty("ItemId", itemId));
        }
        return new CompositeDisposable(disposables);
    }
}
