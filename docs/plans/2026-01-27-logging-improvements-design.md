# Logging Improvements Design

**Date:** 2026-01-27
**Focus:** Console formatting, log levels, structured logging
**Approach:** Serilog with environment-aware output
**Delivery:** Single PR

## Goals

1. **Console output formatting** - Human-readable for development, JSON for production
2. **Log levels/verbosity** - Component-based control for granular debugging
3. **Structured logging** - Context hierarchy (Sync → Namespace → Secret) for tracing operations

## Scope

**In Scope:**
- Replace built-in logging with Serilog
- Environment-aware output (JSON in K8s, readable in dev)
- Component-based log level configuration
- Context scopes for sync/namespace/secret correlation
- Cleanup inconsistent Console.WriteLine and emoji usage

**Out of Scope:**
- External log shipping (Loki, ELK integration)
- Log file output
- Metrics/tracing integration (OpenTelemetry)

## Implementation Details

### Serilog Packages

Add to `VaultwardenK8sSync.csproj` and `VaultwardenK8sSync.Api.csproj`:

```xml
<PackageReference Include="Serilog" Version="4.*" />
<PackageReference Include="Serilog.Extensions.Logging" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.*" />
<PackageReference Include="Serilog.Expressions" Version="5.*" />
```

### Environment Detection

```csharp
var isProduction = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Production"
    || Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") != null;

if (isProduction)
{
    // Compact JSON for log aggregators
    logger.WriteTo.Console(new RenderedCompactJsonFormatter());
}
else
{
    // Human-readable with colors and structure
    logger.WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}");
}
```

### Context Hierarchy

Three-level context structure:

```
Sync Level      → SyncId (GUID), SyncNumber (int)
  Namespace Level → Namespace (string)
    Secret Level    → SecretName (string), ItemId (string)
```

**Helper class:**

```csharp
// Infrastructure/LoggingScopes.cs
public static class LoggingScopes
{
    public static IDisposable BeginSyncScope(int syncNumber)
    {
        var syncId = Guid.NewGuid().ToString("N")[..8];
        return new CompositeDisposable(
            LogContext.PushProperty("SyncId", syncId),
            LogContext.PushProperty("SyncNumber", syncNumber)
        );
    }

    public static IDisposable BeginNamespaceScope(string ns)
        => LogContext.PushProperty("Namespace", ns);

    public static IDisposable BeginSecretScope(string secretName, string? itemId = null)
    {
        var disposables = new List<IDisposable> { LogContext.PushProperty("SecretName", secretName) };
        if (itemId != null) disposables.Add(LogContext.PushProperty("ItemId", itemId));
        return new CompositeDisposable(disposables);
    }
}
```

**Usage in SyncService:**

```csharp
public async Task<SyncSummary> SyncAsync()
{
    using var _ = LoggingScopes.BeginSyncScope(GetSyncCount());
    _logger.LogInformation("Starting sync");

    // In namespace loop:
    using var __ = LoggingScopes.BeginNamespaceScope(namespaceName);
    _logger.LogDebug("Processing namespace");
}
```

**JSON output example:**

```json
{"@t":"2026-01-27T10:30:00Z","@mt":"Created secret","SyncId":"a1b2c3d4","SyncNumber":5,"Namespace":"default","SecretName":"my-app-credentials"}
```

### Component-Based Log Levels

| Variable | Controls | Default |
|----------|----------|---------|
| `LOG_LEVEL` | Global default | `Information` |
| `LOG_LEVEL_SYNC` | SyncService | inherits global |
| `LOG_LEVEL_KUBERNETES` | KubernetesService | inherits global |
| `LOG_LEVEL_VAULTWARDEN` | VaultwardenService | inherits global |
| `LOG_LEVEL_DATABASE` | DatabaseLoggerService, Repositories | inherits global |
| `LOG_LEVEL_WEBHOOK` | WebhookService | inherits global |
| `LOG_LEVEL_METRICS` | MetricsServer, MetricsService | inherits global |

**Serilog configuration:**

```csharp
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Is(GetLogLevel("LOG_LEVEL", LogEventLevel.Information))
    .MinimumLevel.Override("VaultwardenK8sSync.Services.SyncService",
        GetLogLevel("LOG_LEVEL_SYNC"))
    .MinimumLevel.Override("VaultwardenK8sSync.Services.KubernetesService",
        GetLogLevel("LOG_LEVEL_KUBERNETES"))
    .MinimumLevel.Override("VaultwardenK8sSync.Services.VaultwardenService",
        GetLogLevel("LOG_LEVEL_VAULTWARDEN"))
    .MinimumLevel.Override("VaultwardenK8sSync.Services.DatabaseLoggerService",
        GetLogLevel("LOG_LEVEL_DATABASE"))
    .MinimumLevel.Override("VaultwardenK8sSync.Services.WebhookService",
        GetLogLevel("LOG_LEVEL_WEBHOOK"))
    .MinimumLevel.Override("VaultwardenK8sSync.Infrastructure.MetricsServer",
        GetLogLevel("LOG_LEVEL_METRICS"))
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext();
```

### Development Console Format

**Output example:**

```
[10:30:45 INF] SyncService
  Starting sync #5 (SyncId: a1b2c3d4)

[10:30:46 DBG] KubernetesService [default/my-app-creds]
  Checking if secret exists

[10:30:46 INF] KubernetesService [default/my-app-creds]
  Created secret

[10:30:47 WRN] KubernetesService [staging/api-keys]
  Secret exists but has unexpected owner annotation

[10:30:48 ERR] SyncService
  Sync completed with errors
  System.Exception: Connection refused
    at VaultwardenK8sSync...
```

**Formatting decisions:**
- Timestamp: `HH:mm:ss` (no date)
- Level: 3-char abbreviated (`INF`, `DBG`, `WRN`, `ERR`)
- Source: Class name only (not full namespace)
- Context: `[Namespace/SecretName]` inline when present
- Message: Indented on new line for readability
- Exception: Full stack trace, indented

**Color scheme:**
- Debug: Gray
- Information: White/Default
- Warning: Yellow
- Error: Red

## Files Changed

**Modified:**

| File | Changes |
|------|---------|
| `VaultwardenK8sSync.csproj` | Add Serilog packages |
| `VaultwardenK8sSync.Api.csproj` | Add Serilog packages |
| `Program.cs` | Bootstrap Serilog before host, replace Console.WriteLine |
| `ConfigurationExtensions.cs` | Remove AddCustomLogging, add Serilog configuration |
| `LoggingSettings.cs` | Add component-level log settings |
| `SyncService.cs` | Wrap sync in BeginSyncScope, namespace/secret scopes |
| `KubernetesService.cs` | Add secret scopes around operations |
| `VaultwardenService.cs` | Add context where useful |
| `ApplicationHost.cs` | Replace Console.WriteLine with logger, remove emoji inconsistency |

**New:**

| File | Purpose |
|------|---------|
| `Infrastructure/LoggingScopes.cs` | Scope helpers (BeginSyncScope, etc.) |
| `Infrastructure/CompositeDisposable.cs` | Helper for combining disposables |

**Cleanup:**
- Remove direct Console.WriteLine / Console.Error.WriteLine calls
- Remove inconsistent emoji usage from log messages
- Standardize log message format (no leading emoji, consistent casing)

## Testing & Validation

1. **Development mode test:**
   ```bash
   DOTNET_ENVIRONMENT=Development dotnet run
   # Verify: Colored, human-readable output with context
   ```

2. **Production mode test:**
   ```bash
   DOTNET_ENVIRONMENT=Production dotnet run 2>&1 | jq .
   # Verify: Valid JSON, context properties present
   ```

3. **Component filtering test:**
   ```bash
   LOG_LEVEL=Warning LOG_LEVEL_SYNC=Debug dotnet run
   # Verify: Only sync service shows debug, others are warning+
   ```

4. **Context correlation test:**
   ```bash
   # Run a sync, grep logs by SyncId
   dotnet run 2>&1 | jq 'select(.SyncId == "a1b2c3d4")'
   # Verify: All related logs share same SyncId
   ```

## PR Details

**Title:** `feat: improve logging with Serilog, structured context, and component-based levels`

**Description:**
```
## Summary
- Replace built-in logging with Serilog for structured logging
- JSON output in production (K8s), human-readable in development
- Add context hierarchy (SyncId, Namespace, SecretName) for log correlation
- Component-based log level control via environment variables
- Cleanup inconsistent Console.WriteLine and emoji usage

## Environment Variables
- LOG_LEVEL - Global default (Information)
- LOG_LEVEL_SYNC - SyncService
- LOG_LEVEL_KUBERNETES - KubernetesService
- LOG_LEVEL_VAULTWARDEN - VaultwardenService
- LOG_LEVEL_DATABASE - Database services
- LOG_LEVEL_WEBHOOK - WebhookService
- LOG_LEVEL_METRICS - Metrics services

## Testing
- Development: DOTNET_ENVIRONMENT=Development dotnet run
- Production: DOTNET_ENVIRONMENT=Production dotnet run 2>&1 | jq .
```
