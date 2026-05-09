# Enhancement Suggestions — vaultwarden-kubernetes-secrets

> **Status:** Draft / Brainstorming  
> **Date:** 2026-04-28  
> **Scope:** Ideas for improving the Vaultwarden ↔ Kubernetes secrets sync ecosystem

---

## 1. Webhook Receiver Endpoint in API

**Summary:**  
The sync service's `WebhookService` already processes Vaultwarden webhook events with HMAC signature validation and selective per-item/per-namespace sync. However, there is no REST endpoint in the API project to actually **receive** webhook calls from the Vaultwarden server. Currently, webhooks can only reach the sync service internally. Adding a controller endpoint (`POST /api/webhooks/vaultwarden`) would allow Vaultwarden to push change notifications directly, reducing reliance on polling.

**Benefit:**  
- Enables instant sync on item changes (Vaultwarden pushes → API receives → triggers sync)  
- Reduces the `SYNC__SYNCINTERVALSECONDS` polling cadence, saving API calls and CPU  
- Polling remains as a fallback for missed webhooks  

**Effort:** Medium  
- New `WebhooksController.cs` in `VaultwardenK8sSync.Api/Controllers/` (~80 lines)  
- Needs to delegate to `WebhookService.ProcessWebhookAsync()` via Redis pub/sub or direct gRPC call  
- Requires configuring the webhook URL on the Vaultwarden side  

**Risk:** Low  
- Signature validation already implemented — forged requests would be rejected  
- Polling fallback ensures no missed updates  
- Additive — no existing behavior changes  

**Implementation Sketch:**  
```
VaultwardenK8sSync.Api/Controllers/WebhooksController.cs
├── POST /api/webhooks/vaultwarden
│   ├── Validate HMAC-SHA256 signature header
│   ├── Deserialize WebhookEvent payload
│   ├── Publish to Redis channel "webhook:events"
│   └── Return 202 Accepted
└── Sync service subscribes to "webhook:events" via Redis pub/sub
    └── Calls WebhookService.ProcessWebhookAsync()
```
- Reuse `WebhookSettings` configuration section with `Secret` for HMAC  
- Use existing StackExchange.Redis `ConnectionMultiplexer` already present in API project  
- Dashboard should show webhook activity in the recent activity feed  

---

## 2. Secret Staleness Dashboard & Alerts

**Summary:**  
The `SecretState` model already tracks `LastSynced` per individual secret, but the dashboard does not surface staleness information. Operators have no way to see which secrets haven't been refreshed recently. Adding visual indication (color-coded "stale" badges, age sorting, staleness heatmap) and optional Prometheus alerts would give operators confidence that synced secrets are current.

**Benefit:**  
- Quick visual identification of secrets that haven't been updated  
- Operators can spot when a Vaultwarden item change didn't propagate  
- Prometheus alert (`vaultwarden_secret_staleness_seconds`) can page before stale secrets cause outages  

**Effort:** Small  
- ~15 lines in dashboard Secrets page (computed staleness badge)  
- ~5 lines in DashboardController or a new metric exposure  
- Frontend already has date-fns for relative time formatting  

**Risk:** Very low — purely additive UI/metrics change  

**Implementation Sketch:**  
```
VaultwardenK8sSync/Services/MetricsService.cs (+3 lines)
└── New Gauge: vaultwarden_secret_staleness_seconds{namespace, secret_name}

dashboard/src/pages/Secrets.tsx
├── Computed: ageHours = (now - secret.lastSynced) / 3600
├── Color: green (< 1 sync interval), yellow (< 2x), red (> 2x)
└── Sortable column "Last Synced"

charts/vaultwarden-kubernetes-secrets/
└── Optional: PrometheusRule resource with alert on staleness > 1h
```

---

## 3. Tag-Based Selective Sync Filtering

**Summary:**  
Currently, items can be filtered by `OrganizationId`, `FolderId`, or `CollectionId` — coarse-grained Vaultwarden-native groupings. Internally, the custom fields system (`namespaces`, `context-name`) provides per-item routing. Adding a `sync-tags` custom field would allow operators to include/exclude items based on arbitrary labels (e.g., `sync-tags: production, critical`), with corresponding `SYNC__INCLUDETAGS` / `SYNC__EXCLUDETAGS` config options.

**Benefit:**  
- Fine-grained control without reorganizing the Vaultwarden vault  
- Teams can mark items for "staging-only" without duplicating folders/collections  
- Enables gradual rollout (sync a tag to one cluster first, then promote)  

**Effort:** Medium  
- Add `IncludeTags`/`ExcludeTags` to `SyncSettings` configuration  
- Parse comma-separated `sync-tags` custom field from each item  
- Filter logic in `SyncService.SyncAsync()` before namespace grouping  
- Dashboard sync-config endpoint should report active tag filters  

**Risk:** Low  
- Opt-in: no behavior change unless tags are configured  
- Items without the `sync-tags` field are unaffected  
- Easily testable with unit tests on the filter function  

**Implementation Sketch:**  
```
VaultwardenK8sSync/Configuration/SyncSettings.cs
├── IncludeTags: string (comma-separated)
├── ExcludeTags: string (comma-separated)

VaultwardenK8sSync/Services/SyncService.cs
└── SyncAsync() — after applying org/collection/folder filters:
    └── if IncludeTags: item.CustomFields["sync-tags"] must contain at least one
    └── if ExcludeTags: item.CustomFields["sync-tags"] must NOT contain any

charts/vaultwarden-kubernetes-secrets/values.yaml
└── env.config.SYNC__INCLUDETAGS / env.config.SYNC__EXCLUDETAGS
```

---

## 4. Aggregated Health-Endpoint for K8s Probes

**Summary:**  
The sync service registers three separate health checks (`VaultwardenHealthCheck`, `SyncHealthCheck`, `KubernetesHealthCheck`), but there is no single aggregated `/healthz` endpoint that combines all three into a unified result. The API project has no health endpoint at all. Both the sync service and API should expose a well-known `GET /healthz` that returns a JSON summary of all dependency statuses, suitable for Kubernetes `livenessProbe` and `readinessProbe`.

**Benefit:**  
- Kubernetes probes can use a single endpoint that reflects true application health  
- API health checks can include sync service reachability (via Redis/DB)  
- Simplifies monitoring: one endpoint reports Vaultwarden, K8s, DB, and Redis status  
- The API currently cannot report whether the sync service is alive  

**Effort:** Small  
- ~60 lines: new `HealthController` in API project  
- Register `HealthCheckService` from `Microsoft.Extensions.Diagnostics.HealthChecks`  
- Wire up existing `IHealthCheck` implementations  

**Risk:** Very low — additive endpoint, no existing behavior changes  

**Implementation Sketch:**  
```
VaultwardenK8sSync.Api/Controllers/HealthController.cs
├── GET /healthz → aggregate all registered IHealthCheck
├── GET /healthz/ready → readiness (DB accessible, Redis connected)
├── GET /healthz/live → liveness (process is running)
└── Returns:
    {
      "status": "Healthy" | "Degraded" | "Unhealthy",
      "checks": [
        { "name": "vaultwarden", "status": "Healthy" },
        { "name": "kubernetes",  "status": "Healthy" },
        { "name": "sync",        "status": "Degraded", "description": "Last sync 15m ago" },
        { "name": "database",    "status": "Healthy" },
        { "name": "redis",       "status": "Healthy" }
      ]
    }
```

---

## 5. Dynamic Log Level at Runtime

**Summary:**  
The sync service uses Serilog with configuration-driven log levels (`LOGGING__LOGLEVEL__*`). Changing the log level currently requires a Pod restart (or Helm upgrade). Adding a runtime log-level endpoint would allow operators to increase verbosity temporarily for debugging without service disruption — critical for diagnosing intermittent sync issues in production.

**Benefit:**  
- Debug production sync issues without restarting the Pod (losing in-memory state)  
- Reduce disk usage by keeping default level at Warning, increasing only when needed  
- Level reverts to configured default after a configurable timeout (auto-reset)  

**Effort:** Medium  
- Requires Serilog `LoggingLevelSwitch` in the application startup  
- New endpoint in API or sync service to change the minimum level  
- Authentication required (reuse existing token auth)  
- Frontend toggle in dashboard Debug page  

**Risk:** Low  
- Authenticated endpoint prevents abuse  
- Auto-revert timeout ensures no forgotten verbose logging  
- Additive — doesn't affect existing log pipeline  

**Implementation Sketch:**  
```
VaultwardenK8sSync/Application/Program.cs
└── var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
    └── Pass via DI to controller

VaultwardenK8sSync.Api/Controllers/SystemController.cs
├── GET  /system/loglevel → current minimum level
├── PUT  /system/loglevel → { "level": "Debug", "timeoutMinutes": 30 }
└── Background timer reverts to configured default after timeout

dashboard/src/pages/Resources.tsx (or new Debug page)
└── Dropdown: Information / Debug / Warning
    └── "Revert in 30m" countdown
```

---

## 6. Secret Drift Detection & Remediation

**Summary:**  
The `SecretState` model already has a `ContentHash` column, but it is never populated — it's always passed as `null` in the `UpsertSecretStateAsync` call. A periodic hash comparison between the actual Kubernetes Secret data and what Vaultwarden expects would detect manual edits, corruption, or unauthorized changes. When drift is detected, the system can optionally auto-remediate (restore from Vaultwarden) or fire an alert.

**Benefit:**  
- Detect and report manual `kubectl edit secret` changes  
- Identify bit-rot or cluster migration issues where secrets got out of sync  
- Optionally auto-remediate, restoring secrets to their Vaultwarden-defined state  
- Audit trail: all drift events logged in database  

**Effort:** Medium  
- Compute SHA-256 of each K8s Secret's data after every sync  
- Store in `SecretState.ContentHash`  
- New optional sync phase: verify all hashes, report/remediate mismatches  
- Dashboard should surface drift count and per-secret drift indicators  

**Risk:** Low  
- Read-only by default; auto-remediation requires explicit `SYNC__FIXDRIFT: true`  
- Hash computation is cheap (sub-millisecond per secret)  
- Backward compatible — ContentHash column already exists  

**Implementation Sketch:**  
```
VaultwardenK8sSync/Services/SyncService.cs
├── After each secret upsert:
│   └── Compute SHA-256 of combined secret data values
│   └── Store in SecretState.ContentHash (currently null)
├── New optional phase after reconciliation:
│   └── For each Active SecretState:
│       ├── Fetch current K8s secret data
│       ├── Compute hash
│       └── If hash ≠ ContentHash:
│           ├── Log drift warning
│           ├── Increment metrics counter vaultwarden_drift_detected
│           └── If FixDrift=true: re-apply from Vaultwarden data

VaultwardenK8sSync/Services/MetricsService.cs (+3 lines)
└── vaultwarden_drift_detected_total{namespace, secret_name}

charts/vaultwarden-kubernetes-secrets/values.yaml
└── env.config.SYNC__FIXDRIFT: "false"
```

---

## 7. Read-Only API Mode

**Summary:**  
Currently, the API exposes endpoints that trigger database reset and could trigger resyncs. For dashboard-only deployments where users should monitor but not control synchronization, a read-only mode would prevent accidental or unauthorized operations. The dashboard should gracefully degrade (hide action buttons) when the API is in read-only mode.

**Benefit:**  
- Security: dashboard users cannot accidentally trigger full resync or database reset  
- Compliance: read-only access for audit viewers  
- Clear separation: sync operations require direct access to the sync service  

**Effort:** Small  
- Add `API__READONLY: false` configuration setting  
- Middleware or attribute that rejects PUT/POST/DELETE when read-only  
- Dashboard reads the setting from `/dashboard/auth-info` or a new endpoint  

**Risk:** Very low  
- Simple config flag — easy to toggle  
- All existing tests continue to pass  
- No data loss risk  

**Implementation Sketch:**  
```
VaultwardenK8sSync.Api/
├── Configuration/AppSettings.cs → Api.ReadOnly
├── Middleware/ReadOnlyMiddleware.cs
│   └── If Api.ReadOnly && method in [POST, PUT, PATCH, DELETE]:
│       └── Return 405 Method Not Allowed
├── Or per-controller:
│   └── [ServiceFilter(typeof(ReadOnlyFilter))] on SystemController.ResetDatabase

dashboard/src/pages/
├── Get readonly status from /dashboard/auth-info (extend response)
├── Hide "Reset Database" button
├── Disable any "Force Sync" controls
└── Show lock icon in header indicating read-only mode

charts/vaultwarden-kubernetes-secrets/values.yaml
└── api.readOnly: false
```

---

## 8. Audit Log Export (SIEM / Webhook Sink)

**Summary:**  
Sync logs are stored in SQLite and accessible via the API, but there is no mechanism to export them to external SIEM systems (Splunk, Elastic, Datadog) or forward them as webhooks. A structured export endpoint (JSON Lines, NDJSON) and/or a webhook sink would enable compliance monitoring, centralized logging, and alerting on sync failures.

**Benefit:**  
- Compliance: export sync activity to external audit systems  
- Centralized monitoring: integrate with existing SIEM pipelines  
- Real-time alerting: webhook sink notifies external systems of failures  
- `GET /api/synclogs/export?since=ISO&format=jsonlines` for batch exports  

**Effort:** Medium  
- New API endpoint for batch export with cursor-based pagination  
- Optional webhook sink: configure a URL where each sync summary is POSTed  
- No schema changes — all data already exists in SyncLogs/SecretStates  

**Risk:** Low  
- Read-only export — no modification of existing data  
- Webhook sink is opt-in and configurable  
- Pagination prevents memory issues with large datasets  

**Implementation Sketch:**  
```
VaultwardenK8sSync.Api/Controllers/SyncLogsController.cs
├── GET /synclogs/export
│   ├── ?since=ISO8601&until=ISO8601&format=jsonlines|csv
│   ├── Streamed response (not buffered in memory)
│   └── Cursor-based: ?cursor=abc123&limit=100

VaultwardenK8sSync/Services/WebhookSinkService.cs (new)
├── If SYNC__WEBHOOKSINK__URL configured:
│   └── After each SyncAsync(): POST SyncSummary JSON to URL
│   └── Retry with Polly on failure
│   └── Log delivery failures but don't block sync

charts/vaultwarden-kubernetes-secrets/values.yaml
├── env.config.SYNC__WEBHOOKSINK__URL: ""
├── env.config.SYNC__WEBHOOKSINK__RETRIES: "3"
└── env.config.SYNC__WEBHOOKSINK__TIMEOUTSECONDS: "10"
```

---

## 9. Leader Election / HA Mode with Passive Standby

**Summary:**  
The sync service enforces single-instance via `ProcessLock` (file-based) and `GlobalSyncLock` (Redis-based), which prevents concurrent syncs but doesn't provide high availability. If the sync Pod dies, no secrets are synced until it restarts. A leader-election pattern using Redis would allow a passive standby replica to take over immediately on failure, reducing downtime from minutes to seconds.

**Benefit:**  
- High availability: standby replica takes over within seconds of leader failure  
- Zero-downtime updates: during rolling deployments, standby becomes leader  
- Leverages existing Redis/Valkey dependency already used for `GlobalSyncLock`  

**Effort:** High  
- Leader election using Redis `SET NX EX` with TTL (heartbeat) or `RedLock`  
- Passive replica must stay warm (authenticated with Vaultwarden, connected to K8s API)  
- Coordination: both instances share the same SQLite DB (requires NFS or shared PVC)  
- Alternatively: use separate SQLite DBs per replica and sync via Redis  

**Risk:** Medium  
- Concurrency complexity: two instances writing to the same K8s namespace  
- SQLite is not designed for concurrent writes from multiple processes — would need to switch to PostgreSQL or use file locks  
- Leader election logic must be thoroughly tested under network partitions  

**Implementation Sketch:**  
```
VaultwardenK8sSync/Infrastructure/LeaderElector.cs (new)
├── TryAcquireLeadershipAsync():
│   └── Redis: SET leader:sync <pod-id> NX EX 15 (15s TTL)
│       └── Success → we are leader
│       └── Fail → we are standby
├── HeartbeatAsync():
│   └── Every 5s: renew TTL
│   └── On shutdown: release lock (DEL leader:sync)
├── OnLeadershipAcquired():
│   └── Start sync loop
└── OnLeadershipLost():
    └── Stop sync loop, enter standby

└── SyncService uses leader status to gate SyncAsync()

charts/vaultwarden-kubernetes-secrets/
├── values.yaml → replicaCount: 2
├── templates/deployment.yaml → PodAntiAffinity for HA
└── Requires shared storage for SQLite or PostgreSQL adapter
```

---

## 10. Prometheus Alerting Rules (as Code)

**Summary:**  
The service exports comprehensive Prometheus metrics (`vaultwarden_sync_duration_seconds`, `vaultwarden_sync_errors_total`, `vaultwarden_last_successful_sync_timestamp`, etc.) and the Helm chart provisions a `ServiceMonitor` when `monitoring.serviceMonitor.enabled=true`. However, there are no `PrometheusRule` resources shipped with the chart. Adding well-tested alerting rules as part of the Helm chart would give operators production-ready monitoring out of the box.

**Benefit:**  
- Production-ready monitoring: deploy alerts alongside the app  
- No duplicate effort: consistent alerts across deployments  
- Covers common failure modes: stale sync, high error rate, auth failure, drift  
- Customizable: thresholds configurable via values.yaml  

**Effort:** Small  
- New Helm template `templates/prometheusrule.yaml`  
- 4-5 well-defined alert rules  
- Toggle via `monitoring.prometheusRule.enabled`  

**Risk:** Very low — no application code changes, backward-compatible chart addition  

**Implementation Sketch:**  
```
charts/vaultwarden-kubernetes-secrets/templates/prometheusrule.yaml
├── VaultwardenSyncStale
│   └── vaultwarden_last_successful_sync_timestamp < time() - 600
├── VaultwardenSyncErrorsHigh
│   └── rate(vaultwarden_sync_errors_total[15m]) > 0.1
├── VaultwardenSyncDurationSpike
│   └── vaultwarden_sync_duration_seconds{quantile="0.95"} > 30
├── VaultwardenItemsWatchingDrop
│   └── vaultwarden_items_watched < 1
└── VaultwardenSecretsSyncFailure
    └── rate(vaultwarden_secrets_synced_total{operation="failed"}[15m]) > 0

charts/vaultwarden-kubernetes-secrets/values.yaml
└── monitoring:
    └── prometheusRule:
        enabled: false
        namespace: ""  # defaults to release namespace
        labels:
          prometheus: k8s
          role: alert-rules
```

---

## 11. Dashboard Real-Time Updates via Server-Sent Events

**Summary:**  
The API's `SyncOutputController` provides a WebSocket endpoint (`GET /api/sync-output/stream`) for real-time sync progress, but the dashboard does not consume it. The dashboard relies on TanStack Query polling (`refetchInterval`) which adds unnecessary load and latency. Wiring SSE or WebSocket into the dashboard would provide instant UI updates during sync operations without polling overhead.

**Benefit:**  
- Live sync progress: users see items being processed in real time  
- Reduced API load: no polling requests from dashboard  
- Better UX: progress bar advances naturally, no "refreshing..." spinners  

**Effort:** Medium  
- Create a React hook `useSyncStream()` that opens a WebSocket to `/api/sync-output/stream`  
- Parse sync output lines and update TanStack Query cache  
- Dashboard progress bar reacts to live data  
- Graceful fallback to polling when WebSocket isn't available  

**Risk:** Low  
- WebSocket endpoint already exists and works  
- Fallback to polling ensures backward compatibility  
- No changes to API — pure frontend addition  

**Implementation Sketch:**  
```
dashboard/src/lib/useSyncStream.ts (new)
├── Connect to WebSocket: getWebSocketUrl('/api/sync-output/stream')
├── Parse incoming messages (JSON lines with item status)
├── Update TanStack Query cache (queryClient.setQueryData)
├── Expose: { isConnected, items, progress, error }
└── Auto-reconnect with exponential backoff

dashboard/src/pages/Dashboard.tsx
└── useSyncStream() → live progress bar during active sync

dashboard/src/pages/Secrets.tsx
└── useSyncStream() → live status updates on individual secrets
```

---

## 12. Kubernetes Event Publishing for Sync Events

**Summary:**  
Sync operations (created, updated, deleted secrets) are logged to SQLite and visible in the dashboard, but they are not published as Kubernetes Events. Publishing sync events (`kubectl get events -n <namespace>`) would make sync activity visible to cluster operators using standard K8s tooling, and enable event-based monitoring tools like `kube-eventer` or `eventrouter`.

**Benefit:**  
- Sync visibility: `kubectl get events` shows when and why secrets changed  
- Standard K8s tooling: operators don't need to check the dashboard  
- Integration: event exporters can forward to external systems  
- Traceability: K8s Events include the sync Pod as the event source  

**Effort:** Small  
- Use `KubernetesClient` `CoreV1Events.CreateNamespacedEventAsync()`  
- One event per sync cycle: "Synced 5 secrets (3 created, 2 updated)"  
- Optionally per-secret events for important operations  

**Risk:** Very low  
- Events have built-in TTL (default 1h) — no storage concerns  
- No blocking: event publication failures use fire-and-forget logging  
- Additive — existing behavior unchanged  

**Implementation Sketch:**  
```
VaultwardenK8sSync/Services/KubernetesService.cs
└── New method: PublishSyncEventAsync(namespace, SyncSummary)
    ├── Create V1Event with:
    │   ├── Type: Normal (success) or Warning (failure)
    │   ├── Reason: "SecretsSynced" / "SecretsSyncFailed"
    │   ├── Message: synced N secrets (created/updated/skipped/failed)
    │   ├── Source: vaultwarden-kubernetes-secrets
    │   └── InvolvedObject: Deployment in release namespace
    └── Call from SyncService after each sync cycle

└── Does NOT need new metrics or config — always-on with minimal overhead
```

---

## Summary Matrix

| # | Suggestion | Benefit | Effort | Risk |
|---|-----------|---------|--------|------|
| 1 | Webhook Receiver Endpoint | Instant sync on item change | Medium | Low |
| 2 | Secret Staleness Dashboard | Visual freshness indicators | Small | Very Low |
| 3 | Tag-Based Selective Sync | Fine-grained item filtering | Medium | Low |
| 4 | Aggregated Health Endpoint | K8s probe-ready health check | Small | Very Low |
| 5 | Dynamic Log Level | Debug without Pod restart | Medium | Low |
| 6 | Secret Drift Detection | Detect manual/corrupted secrets | Medium | Low |
| 7 | Read-Only API Mode | Dashboard-only monitoring | Small | Very Low |
| 8 | Audit Log Export | SIEM/webhook integration | Medium | Low |
| 9 | Leader Election / HA | High availability sync | High | Medium |
| 10 | Prometheus Alerting Rules | Production-ready alerts | Small | Very Low |
| 11 | Dashboard SSE/WebSocket | Live UI without polling | Medium | Low |
| 12 | K8s Event Publishing | `kubectl get events` integration | Small | Very Low |

---

*Each suggestion is independent and grouped by risk level. Quick wins (Very Low risk) can be implemented immediately; the HA mode requires careful design and testing before production deployment.*
