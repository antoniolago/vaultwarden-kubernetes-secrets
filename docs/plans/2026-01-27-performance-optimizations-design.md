# Performance Optimizations Design

**Date:** 2026-01-27
**Focus:** Memory, CPU, and database query optimizations
**Approach:** Backend-first with minimal dashboard fixes
**Delivery:** Single PR with benchmarks

## Scope

**In Scope:**
- All C# performance fixes (memory, CPU)
- All database query optimizations
- Benchmarks proving improvements
- Basic dashboard memoization (reduces API load on backend)

**Out of Scope:**
- Dashboard virtualization
- Server-side search endpoints
- Batch API for data keys modal

## Priority Order

| Priority | Issue | Resource Impact | Location |
|----------|-------|-----------------|----------|
| P1 | Batch 7 statistics queries → 1 | DB connections, CPU | SyncLogRepository.cs:52-63 |
| P2 | Chain filter predicates (remove intermediate ToList()) | Memory | SyncService.cs:481-495 |
| P3 | Use HashSet for managed keys lookup | CPU | KubernetesService.cs:882 |
| P4 | Fix N+1 in DashboardController | DB connections, Memory | DashboardController.cs:42-43 |
| P5 | Replace Thread.Sleep with Task.Delay | CPU (thread pool) | GlobalSyncLock.cs:100 |
| P6 | Fix blocking async dispose | CPU (thread pool) | MetricsServer.cs:251 |
| P7 | Add useMemo/useCallback to dashboard | Indirect (fewer API calls) | Dashboard.tsx |

## Implementation Details

### P1: Batch Statistics Queries

**Location:** `VaultwardenK8sSync.Database/Repositories/SyncLogRepository.cs:52-63`

**Current:** 7 separate queries for statistics

```csharp
var totalSyncs = await _context.SyncLogs.CountAsync();
var successfulSyncs = await _context.SyncLogs.CountAsync(...);
var failedSyncs = await _context.SyncLogs.CountAsync(...);
// ... 4 more queries
```

**After:** Single aggregated query

```csharp
var stats = await _context.SyncLogs
    .GroupBy(_ => 1)
    .Select(g => new {
        TotalSyncs = g.Count(),
        SuccessfulSyncs = g.Count(s => s.Status == "Success"),
        FailedSyncs = g.Count(s => s.Status == "Failed"),
        TotalSecretsCreated = g.Sum(s => s.CreatedSecrets),
        TotalSecretsUpdated = g.Sum(s => s.UpdatedSecrets),
        AvgDuration = g.Average(s => s.DurationMs)
    })
    .FirstOrDefaultAsync();
```

### P2: Chain Filter Predicates

**Location:** `VaultwardenK8sSync/Services/SyncService.cs:481-495`

**Current:** Three separate materializations

```csharp
items = items.Where(...).ToList();  // Allocates full list
items = items.Where(...).ToList();  // Allocates another full list
items = items.Where(...).ToList();  // Allocates third full list
```

**After:** Single materialization at the end

```csharp
var query = items.AsEnumerable();

if (!string.IsNullOrEmpty(resolvedOrgId))
    query = query.Where(i => string.Equals(i.OrganizationId, resolvedOrgId, StringComparison.OrdinalIgnoreCase));

if (!string.IsNullOrEmpty(resolvedFolderId))
    query = query.Where(i => string.Equals(i.FolderId, resolvedFolderId, StringComparison.OrdinalIgnoreCase));

if (!string.IsNullOrEmpty(resolvedCollectionId))
    query = query.Where(i => i.CollectionIds?.Contains(resolvedCollectionId, StringComparer.OrdinalIgnoreCase) == true);

items = query.ToList();  // Single allocation
```

### P3: HashSet for Managed Keys

**Location:** `VaultwardenK8sSync/Services/KubernetesService.cs:882`

**Current:** O(n×m) with List.Contains

```csharp
return secret.Data.All(kvp => managedKeys.Contains(kvp.Key));
```

**After:** O(n) with HashSet

```csharp
var managedKeysSet = new HashSet<string>(managedKeys, StringComparer.OrdinalIgnoreCase);
return secret.Data.All(kvp => managedKeysSet.Contains(kvp.Key));
```

### P4: Fix N+1 in DashboardController

**Location:** `VaultwardenK8sSync.Api/Controllers/DashboardController.cs:42-43`

**Current:** Two separate fetches, then in-memory processing

```csharp
var activeSecrets = await _secretStateRepository.GetActiveSecretsAsync();
var allSecrets = await _secretStateRepository.GetAllAsync();
```

**After:** Single query with projection for namespace distribution

```csharp
var namespaceStats = await _secretStateRepository.GetNamespaceDistributionAsync();
```

New repository method handles aggregation at DB level.

### P5: Replace Thread.Sleep with Task.Delay

**Location:** `VaultwardenK8sSync/Infrastructure/GlobalSyncLock.cs:100`

**Current:** Blocks thread during retry loop

```csharp
Thread.Sleep(10);
```

**After:** Async-friendly wait

```csharp
await Task.Delay(10);
```

### P6: Fix Blocking Async Dispose

**Location:** `VaultwardenK8sSync/Infrastructure/MetricsServer.cs:251`

**Current:** Synchronously blocks on async disposal

```csharp
_app?.DisposeAsync().AsTask().Wait();
```

**After:** Implement IAsyncDisposable properly

```csharp
public async ValueTask DisposeAsync()
{
    if (_app != null)
    {
        await _app.DisposeAsync();
    }
    GC.SuppressFinalize(this);
}
```

The class will implement both IDisposable (for sync contexts) and IAsyncDisposable (for async contexts).

### P7: Dashboard Memoization

**Location:** `dashboard/src/pages/Dashboard.tsx`

**Memoize computed values:**

```typescript
const activeNamespacesCount = useMemo(
  () => namespaces?.filter(ns => ns.activeSecrets > 0).length || 0,
  [namespaces]
);

const totalSecretsCount = useMemo(
  () => namespaces?.reduce((sum, ns) => sum + ns.secretCount, 0) || 0,
  [namespaces]
);
```

**Memoize event handlers:**

```typescript
const handleShowSecrets = useCallback(
  async (namespace: string, status: 'Active' | 'Failed', count?: number) => {
    // ... existing logic
  },
  []
);
```

**Add staleTime to queries:**

```typescript
const { data: overview } = useQuery({
  queryKey: ['dashboard-overview'],
  queryFn: api.getDashboardOverview,
  refetchInterval: 30000,
  staleTime: 30000,
});
```

## Benchmarking Strategy

**Benchmark Project:** `VaultwardenK8sSync.Benchmarks/`

Uses BenchmarkDotNet with `[MemoryDiagnoser]` to measure:

| Metric | Tool | Target |
|--------|------|--------|
| Memory allocations | BenchmarkDotNet | 50%+ reduction in filter chain |
| Query count | EF Core logging | 7 → 1 for statistics |
| HashSet vs List lookup | BenchmarkDotNet | O(n) → O(1) demonstrated |

Benchmark results will be included in PR description.

## Files Changed

**Modified:**

| File | Changes |
|------|---------|
| `VaultwardenK8sSync.Database/Repositories/SyncLogRepository.cs` | Batch 7 queries → 1 |
| `VaultwardenK8sSync.Database/Repositories/SecretStateRepository.cs` | Add GetNamespaceDistributionAsync() |
| `VaultwardenK8sSync.Api/Controllers/DashboardController.cs` | Use new repository method, remove N+1 |
| `VaultwardenK8sSync/Services/SyncService.cs` | Chain filter predicates |
| `VaultwardenK8sSync/Services/KubernetesService.cs` | HashSet for managed keys |
| `VaultwardenK8sSync/Infrastructure/GlobalSyncLock.cs` | Task.Delay instead of Thread.Sleep |
| `VaultwardenK8sSync/Infrastructure/MetricsServer.cs` | Proper IAsyncDisposable |
| `dashboard/src/pages/Dashboard.tsx` | useMemo, useCallback, staleTime |

**New:**

| File | Purpose |
|------|---------|
| `VaultwardenK8sSync.Benchmarks/VaultwardenK8sSync.Benchmarks.csproj` | Benchmark project |
| `VaultwardenK8sSync.Benchmarks/FilteringBenchmarks.cs` | Memory allocation benchmarks |
| `VaultwardenK8sSync.Benchmarks/DatabaseBenchmarks.cs` | Query benchmarks |

## PR Details

**Title:** `perf: optimize memory, CPU, and database usage`

**Description template:**
```
## Summary
- Batch 7 statistics queries into 1
- Chain filter predicates to reduce memory allocations
- Use HashSet for O(1) managed keys lookup
- Fix N+1 query pattern in dashboard controller
- Replace blocking Thread.Sleep with Task.Delay
- Implement proper IAsyncDisposable for MetricsServer
- Add React memoization to reduce re-renders

## Benchmarks
[Include BenchmarkDotNet results table here]

## Testing
- All existing tests pass
- New benchmarks validate performance improvements
```
