# Production Readiness Checklist — Vaultwarden Kubernetes Secrets

> **Project**: Vaultwarden Kubernetes Secrets  
> **Components**: Sync Service (`VaultwardenK8sSync`), REST API (`VaultwardenK8sSync.Api`), Dashboard (`dashboard/`), Database (`VaultwardenK8sSync.Database`)  
> **Purpose**: Validate all aspects of running the service in a production Kubernetes cluster.

---

## 1. Monitoring

> **Stack**: prometheus-net (`8.2.1`) exposed via embedded MetricsServer on the sync service, plus prometheus-net.AspNetCore on the API.

- [ ] **Metrics endpoint confirmed accessible**
  - Sync service: `http://<pod>:9090/metrics` (configurable via `METRICS__PORT`, default `9090`, `METRICS__ENABLED` defaults `true`)
  - API: `http://<api-pod>:8080/metrics` (provided by `prometheus-net.AspNetCore`)
  - Implementation: `Infrastructure/MetricsServer.cs` line 63 `_app.MapMetrics("/metrics")`; API `Program.cs` line 376-380

- [ ] **Prometheus ServiceMonitor or PodMonitor configured** to scrape both endpoints with appropriate `prometheus.io/scrape: "true"` annotations

- [ ] **Core metrics verified (from `Services/MetricsService.cs`):**
  - `vks_sync_duration_seconds{success}` — sync duration histogram
  - `vks_items_watched_total` — items examined per sync cycle
  - `vks_secrets_synced_total{action="created|updated|deleted|skipped|failed"}` — per-action counter
  - `vks_orphans_deleted_total` — orphan cleanup count
  - `vks_sync_errors_total{error_type}` — error counter by exception type
  - `vks_last_successful_sync_timestamp` — gauge of last successful sync Unix timestamp
  - `vks_vaultwarden_api_calls_total{endpoint,success}` — Vaultwarden API call counter
  - `vks_kubernetes_api_calls_total{operation,success}` — K8s API call counter
  - `vks_hash_comparisons_total{result="match|mismatch"}` — hash comparison result counter
  - `vks_orphan_count` — current orphan gauge

- [ ] **Health check endpoints confirmed:**
  - Sync service: `/health` (implemented via `HealthChecks/SyncHealthCheck.cs` — checks if `LastSuccessfulSync` is recent)  
  - API: `/health` (exposed via `app.MapHealthChecks("/health")` in `Program.cs` line 380, exempted from token auth in `Middleware/TokenAuthenticationMiddleware.cs` line 26)

- [ ] **Grafana dashboard recommendations:**
  - **Sync Overview**: sync duration (p50/p95/p99), items processed per cycle, success/fail ratio
  - **Secret Landscape**: total secrets managed, secrets by namespace, created vs updated vs deleted over time
  - **Health**: last successful sync timestamp, orphan count trend, error rate by type
  - **Latency**: time from Vaultwarden change to K8s Secret update (requires webhook metric or sync interval tracking)
  - **Resource Usage**: CPU/memory of sync and API pods, Go/ASP.NET GC metrics

- [ ] **PrometheusRules / recording rules** defined for key aggregations (e.g., sync success rate over 1h window)

---

## 2. Alerting

- [ ] **Sync stalled alert**: `time() - vks_last_successful_sync_timestamp > 2 * SYNC__SYNCINTERVALSECONDS`
  - Default interval: 300s → alert if no successful sync in >600s

- [ ] **Sync failure rate threshold**: `rate(vks_sync_errors_total[15m]) > 0` for any error type

- [ ] **Orphan count spike**: `vks_orphan_count > (baseline * 2)` — unexpected mass deletion detection

- [ ] **Hash mismatch surge**: `rate(vks_hash_comparisons_total{result="mismatch"}[15m]) > 10` — could indicate drift or data corruption

- [ ] **Vaultwarden connectivity loss**: `rate(vks_vaultwarden_api_calls_total{success="false"}[5m]) > 0` — upstream dependency down

- [ ] **K8s API connectivity loss**: `rate(vks_kubernetes_api_calls_total{success="false"}[5m]) > 0` — cluster access broken

- [ ] **ProcessLock contention**: if multiple sync instances attempt to run simultaneously (detected via log error rate)

- [ ] **Dashboard/Api endpoint down**: Blackbox probe on `/health` for both API and sync service

---

## 3. Backup and Disaster Recovery

- [ ] **SQLite database backup strategy defined:**
  - Database location: `VaultwardenK8sSync.Database` via EF Core SQLite provider
  - WAL mode enabled by default (`Microsoft.EntityFrameworkCore.Sqlite`)
  - Daily snapshot command: `sqlite3 /data/vaultwarden-k8s-sync.db ".backup /backups/db-$(date +%Y%m%d).sqlite"`
  - Retention: keep last 30 daily + 12 monthly backups

- [ ] **Recovery from Vaultwarden resync tested:**
  - If DB is lost, the service will re-fetch all items from Vaultwarden and reconcile with K8s
  - **Risk**: orphans may not be detected without DB (no record of previously managed secrets)
  - Mitigation: orphans are detected by scanning K8s secrets with the managed-by annotation and cross-referencing with DB

- [ ] **Recovery procedure documented:**
  1. Stop the sync service pod (scale to 0)
  2. Restore SQLite DB from latest backup
  3. Restart sync service
  4. Verify first sync shows no unexpected updates (expected: hash comparison from restored DB matches current K8s state)

- [ ] **Backup testing scheduled**: quarterly restore-to-drill verifying hash integrity post-restore

- [ ] **PersistentVolumeClaim configured** for SQLite data directory to survive pod restarts
  - Must use `ReadWriteOnce` access mode
  - Storage class with snapshots support recommended

- [ ] **Export/import capability**: `ExportSecretAsYamlAsync()` in `KubernetesService.cs` line 637 can export secret state as YAML for manual backup

---

## 4. Security

### 4.1 RBAC Analysis — YAML Manifest Attachment Permission Gap

> **Risk Level**: HIGH — Current RBAC does not cover resources that YAML attachments can create.

#### How YAML Attachments Work

The attachment processing pipeline is in `SyncService.cs` and `KubernetesService.cs`:

1. **Detection** (`SyncService.cs` lines 748-750): During `ExtractSecretDataAsync()`, each attachment is downloaded and checked with `IsKubernetesYaml()` (line 1148). If it parses as valid K8s YAML (any `apiVersion`/`Kind`), the raw content is added to a `yamlManifests` list.

2. **Collection** (`SyncService.cs` line 591): `SyncItemAsync()` passes a shared `yamlManifests` list through the pipeline.

3. **Application** (`SyncService.cs` lines 550-552, 566-589): After all secrets are synced for a namespace, `ApplyCollectedYamlManifestsAsync()` calls `KubernetesService.ApplyYamlAsync()` for each manifest.

4. **Apply Logic** (`KubernetesService.cs` lines 990-1031): `ApplyYamlAsync()` uses `KubernetesYaml.LoadAllFromString()` to deserialize objects, then dispatches by type:
   ```csharp
   var result = obj switch
   {
       V1Secret secret => await ApplySecretAsync(secret),
       V1ConfigMap cm => await ApplyConfigMapAsync(cm),
       V1Namespace ns => await ApplyNamespaceAsync(ns),
       _ => OperationResult.Failed($"Unsupported resource type: {obj.GetType().Name}")
   };
   ```

5. **Notes Scanning** (`SyncService.cs` lines 786-788): Item notes are also scanned for valid K8s YAML and added to `yamlManifests`.

#### Current RBAC (from `charts/.../templates/rbac.yaml`)

**ClusterRole** (or Role if not cluster-wide):
```yaml
rules:
  - apiGroups: [""]
    resources: ["secrets"]
    verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
  - apiGroups: [""]
    resources: ["namespaces"]
    verbs: ["get", "list", "watch"]
```

#### The Gap

| Resource Type | Needed For | Currently Permitted? |
|---|---|---|
| `secrets` | Core sync | ✅ Yes |
| `namespaces` | Namespace discovery | ✅ Yes |
| `configmaps` | YAML attachments (V1ConfigMap) | ❌ **No** |
| *(any other resource)* | Future YAML attachments | ❌ **No** |

The `ApplyYamlAsync()` method currently only handles `Secret`, `ConfigMap`, and `Namespace`. However, `IsKubernetesYaml()` accepts **any** valid K8s manifest — a user could attach a `Deployment`, `Service`, `Ingress`, or any object YAML. If the type isn't handled, it fails with `"Unsupported resource type: V1Deployment"` at runtime, but the RBAC **also** doesn't grant permissions for these types.

#### Proposed Solutions

##### Option A (Recommended): Explicit YAML Manifest Allowlist

Add a configuration list for allowed YAML resource types:

```
YAML__ALLOWED_RESOURCE_TYPES=Secret,ConfigMap,Namespace
```

In `ApplyYamlAsync()`, reject any object whose `Kind` is not in the allowlist before attempting dispatch. This prevents accidentally creating risky resources even if the code is later extended.

- ✅ No RBAC changes needed (if only Secret/ConfigMap/Namespace are allowed)
- ✅ Defense-in-depth — configurable without code changes
- ✅ Users opt-in to additional resource types deliberately
- ⚠️ Still needs RBAC extension if ConfigMap is to be supported

##### Option B: Extended ClusterRole with Specific Resources

If ConfigMap support is desired, extend the ClusterRole:

```yaml
rules:
  - apiGroups: [""]
    resources: ["secrets", "configmaps"]
    verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
  - apiGroups: [""]
    resources: ["namespaces"]
    verbs: ["get", "list", "watch"]
```

For broader YAML support, a more general rule:

```yaml
- apiGroups: ["*"]
  resources: ["*"]
  verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
```

- ✅ Maximum flexibility
- ❌ **Extreme security risk** — defeats principle of least privilege
- ❌ Any Vaultwarden user with attachment write access can create arbitrary cluster resources

##### Option C: Separate Service/Deployment for YAML Objects

Deploy a separate `vaultwarden-yaml-applier` component with its own elevated ClusterRole. The sync service communicates approved manifests to it via a sidecar pattern, shared volume, or API call.

- ✅ Clean separation of concerns — core sync needs minimal RBAC
- ✅ YAML applier can have tightly-scoped elevated permissions
- ✅ Independent scaling and failure isolation
- ⚠️ Increased operational complexity
- ⚠️ Requires IPC mechanism (gRPC, shared filesystem, or HTTP API)

##### Option D: Webhook Validation / Admission Control

Keep RBAC minimal and rely on a `ValidatingAdmissionWebhook` or `OPA/Gatekeeper` constraint to restrict what YAML attachments can create:

```yaml
# Gatekeeper constraint: only allow specific kinds from the sync service
apiGroups: ["*"]
kinds: ["Secret", "ConfigMap"]
```

- ✅ Defense-in-depth — RBAC + admission control
- ✅ No code changes needed in the sync service
- ⚠️ Requires Gatekeeper/OPA installed in the cluster
- ⚠️ Validation failure produces opaque errors in sync logs

#### Permission Matrix

| Component | Resources Required | Verbs | Scope |
|---|---|---|---|
| **Sync Service (core)** | `secrets` | get, list, watch, create, update, patch, delete | Cluster or Namespace |
| **Sync Service (core)** | `namespaces` | get, list, watch | Cluster (for multi-ns) |
| **Sync Service (YAML attachments)** | `configmaps` | get, list, watch, create, update, patch, delete | Cluster or Namespace |
| **Sync Service (YAML — future)** | *(depends on allowlist)* | *(depends on resource)* | *(depends on resource)* |
| **API** | `secrets` | get, list, watch (read-only) | Cluster or Namespace |
| **API** | `pods` | get, list, watch (for status endpoint) | Namespace |
| **Dashboard** | (none direct — proxies through API) | — | — |

#### Recommendation

**Adopt Option A + Option B minimally**: Add an allowlist configuration and extend RBAC to include `configmaps` (since `ApplyYamlAsync` already supports it). Deny all other resource types by default in code. This gives a safe path forward:

1. Accept that `IsKubernetesYaml()` already accepts any valid manifest
2. Tighten `ApplyYamlAsync()` to reject non-allowlisted types at the code level
3. Extend the ClusterRole (cluster-wide mode) or Role (namespace-scoped mode) to include `configmaps`
4. Document the allowlist for operators

### 4.2 Other Security Items

- [ ] **Network policies applied** to restrict egress/ingress:
  - Sync pod: egress only to Vaultwarden server + K8s API server
  - API pod: ingress only from dashboard + Prometheus
  - Dashboard: ingress only from ingress controller

- [ ] **Secret encryption at rest** — K8s `EncryptionConfiguration` with AES-CBC or KMS provider for `Secret` resources

- [ ] **TLS for API and dashboard** — ingress configuration with valid TLS certificate (cert-manager recommended)

- [ ] **Authentication on dashboard** — token-based auth already implemented (`Middleware/TokenAuthenticationMiddleware.cs`), token stored in K8s Secret created during Helm install

- [ ] **Non-root user in containers** — verify Dockerfiles do not use `USER root`

- [ ] **No privileged containers** — `securityContext.privileged: false` in Helm chart

- [ ] **Secret scanning in CI/CD** — GitHub secret scanning or similar for leaked credentials in repo

- [ ] **Vaultwarden credentials lifecycle** — rotate `VAULTWARDEN__MASTERPASSWORD`, `BW_CLIENTID`, `BW_CLIENTSECRET` periodically

- [ ] **K8s ServiceAccount** with minimal permissions — not using `default` namespace SA

---

## 5. Scaling and Performance

- [ ] **Single-instance sync enforcement confirmed** — `Infrastructure/ProcessLock.cs` prevents concurrent sync processes on the same pod

- [ ] **Read-heavy API scaling** — API is stateless and can scale horizontally. Uses `GlobalSyncLock` (`Infrastructure/GlobalSyncLock.cs`) backed by Redis/Valkey (`StackExchange.Redis`) to coordinate with sync service

- [ ] **SQLite concurrent reader support** — WAL mode (write-ahead logging) allows concurrent reads while a writer is active, critical for API availability during sync

- [ ] **Sync interval tuning evaluated:**
  - Default `SYNC__SYNCINTERVALSECONDS`: 300 (5 minutes)
  - For most deployments: 60-300s is reasonable depending on Vaultwarden server load
  - Below 30s: risk of sync overlap (ProcessLock prevents, but back-pressure builds)

- [ ] **Item count limitations tested:**
  - Test with 100, 500, 1000 items to establish baseline
  - Monitor `vks_sync_duration_seconds` to detect non-linear scaling
  - Document: estimated sync duration = `items * ~100ms` (network + K8s API latency)

- [ ] **Secret size limits considered** — K8s etcd has a ~1MB object limit; large attachments or many custom fields could approach this

- [ ] **ProcessLock timeout/failover tested** — if sync crashes while holding lock, ensure stale lock is cleaned up

- [ ] **Redis/Valkey HA for GlobalSyncLock** — if Redis is a single point of failure, consider Redis Sentinel or Cluster

---

## 6. Logging and Observability

> **Stack**: Serilog (structured logging)

- [ ] **Log levels appropriately configured:**
  - `Information`: sync cycle start/end, secrets created/updated/deleted, YAML manifest applied
  - `Debug`: hash comparisons, skipped items, individual field processing, attachment download details, decryption status
  - `Warning`: failed YAML manifest apply, orphan cleanup errors, non-critical Vaultwarden API errors
  - `Error`: sync failures, K8s API exceptions, ProcessLock acquisition failures

- [ ] **Structured logging verified** — log events include:
  - `ItemId`, `SecretName`, `Namespace` for secret operations
  - `SyncDuration`, `ItemsProcessed`, `Errors` for sync summaries
  - `Exception` details with stack traces

- [ ] **Log retention policy configured:**
  - Container stdout/stderr: captured by K8s log rotation (default 10MB per file, 10 files)
  - Sidecar or daemonset (Fluentd/Vector) shipping to central storage
  - Retention: 30 days hot, 90 days cold

- [ ] **External logging integration:**
  - Loki (via Promtail or Grafana Alloy)
  - ELK stack (via Filebeat or Logstash)
  - Structured JSON format for easier parsing

- [ ] **Request tracing / correlation IDs confirmed:**
  - Sync cycle: `SyncLogId` (long) tracked through the entire sync pipeline
  - API requests: ASP.NET `HttpContext.TraceIdentifier` or custom correlation middleware

- [ ] **Audit log for sensitive operations** — consider logging who triggered manual syncs (via API/webhook) for compliance

---

## 7. Upgrades and Deployment

- [ ] **Zero-downtime deployment strategy confirmed:**
  - **API**: Stateless, can run multiple replicas. Rolling update with `maxSurge=1`, `maxUnavailable=0`
  - **Sync service**: Only one instance can run at a time (ProcessLock). Use `Recreate` update strategy or preStop hook to wait for sync completion

- [ ] **Helm chart upgrade path tested:**
  - `helm upgrade -i` with `--version` pinning
  - Chart published to GHCR: `oci://ghcr.io/antoniolago/charts/vaultwarden-kubernetes-secrets`
  - `values.yaml` schema validated for breaking changes

- [ ] **Database schema changes assessed:**
  - Uses `EnsureCreated()`, not EF Core migrations → no automatic schema evolution
  - Schema changes require manual migration script or DB rebuild from Vaultwarden
  - Document: major version upgrades should include DB migration instructions

- [ ] **Rollback procedure documented:**
  1. `helm rollback vaultwarden-kubernetes-secrets <revision>`
  2. If DB schema changed: restore SQLite from backup before rolling back
  3. Verify first sync post-rollback doesn't produce spurious updates

- [ ] **Canary testing recommendations:**
  - Deploy new version in a non-production namespace first (use separate Vaultwarden collection)
  - Run `SYNC__DRYRUN=true` to validate behavior without modifying K8s secrets
  - Monitor `vks_sync_errors_total` and `vks_orphans_deleted_total` for anomalies

- [ ] **Image tagging strategy** — use semantic versioning (not `latest`) for all container images

- [ ] **Pre-upgrade hook** — Helm pre-upgrade hook to backup SQLite DB before deployment

---

## 8. SLO / SLI

| Indicator | Target | Measurement Method |
|---|---|---|
| Sync latency p99 | < 30s for typical item counts | `vks_sync_duration_seconds` histogram |
| API availability (30d) | 99.9% | Prometheus `up` metric + `/health` probe |
| Dashboard availability (30d) | 99.9% | Prometheus `up` metric + `/health` probe |
| Sync success rate (30d) | > 99.5% | `vks_secrets_synced_total{action="failed"}` / `vks_items_watched_total` |
| Maximum data staleness | < 2x `SYNC__SYNCINTERVALSECONDS` | `time() - vks_last_successful_sync_timestamp` |
| Secrets hash consistency | 100% (no drift) | `vks_hash_comparisons_total{result="mismatch"}` should be 0 in steady state |
| Orphan cleanup accuracy | 100% (only true orphans deleted) | Review of `vks_orphans_deleted_total` + audit log |
| Recovery time (from DB loss) | < 30 min | Time to restore SQLite + verify full sync |

- [ ] **Error budget defined** — 0.5% failure budget per month for sync operations
- [ ] **Burn rate alerts configured** — 2x, 5x, 10x error budget consumption rate
- [ ] **SLI dashboard created** — showing real-time and monthly adherence to all targets

---

## 9. Configuration Management

### Environment Variables Reference

| Variable | Required | Default | Description |
|---|---|---|---|
| `VAULTWARDEN__SERVERURL` | ✅ Yes | — | Vaultwarden server URL |
| `BW_CLIENTID` | ✅ Yes | — | Bitwarden API client ID |
| `BW_CLIENTSECRET` | ✅ Yes | — | Bitwarden API client secret |
| `VAULTWARDEN__MASTERPASSWORD` | ✅ Yes | — | Master password |
| `VAULTWARDEN__ORGANIZATIONID` | ❌ No | — | Filter to specific org |
| `VAULTWARDEN__COLLECTIONID` | ❌ No | — | Filter to specific collection |
| `VAULTWARDEN__FOLDERID` | ❌ No | — | Filter to specific folder |
| `SYNC__SYNCINTERVALSECONDS` | ❌ No | `300` | Interval in seconds |
| `SYNC__CONTINUOUSSYNC` | ❌ No | `true` | Keep syncing in loop |
| `SYNC__DRYRUN` | ❌ No | `false` | Dry-run mode |
| `METRICS__ENABLED` | ❌ No | `true` | Enable metrics endpoint |
| `METRICS__PORT` | ❌ No | `9090` | Metrics server port |
| `YAML__ALLOWED_RESOURCE_TYPES` | ❌ No | `Secret,ConfigMap,Namespace` | Allowed YAML types |

- [ ] **All required env vars documented** in Helm chart `values.yaml`
- [ ] **Configuration validation on startup** — service should crash-loop with clear message if required vars are missing

- [ ] **Secret management:**
  - `BW_CLIENTID`, `BW_CLIENTSECRET`, `VAULTWARDEN__MASTERPASSWORD` stored as K8s `Secret` (not configmap or env literal)
  - Auth token for dashboard stored in separate K8s Secret
  - External secrets operator (e.g., External Secrets, Sealed Secrets) for GitOps environments

- [ ] **Config immutability review** — which settings require pod restart vs. hot-reload?

- [ ] **Helm values schema** — `values.schema.json` for validation

---

## 10. Compliance & Audit

- [ ] **Immutable infrastructure** — no manual `kubectl exec` to modify configuration at runtime

- [ ] **Change management process defined** for:
  - Sync interval adjustments
  - RBAC permission changes
  - Vaultwarden credential rotation

- [ ] **Audit trail for secret operations:**
  - K8s audit logging enabled for `Secret` reads/writes
  - Sync service logs all create/update/delete operations with item ID and secret name

- [ ] **Data residency considerations** — if Vaultwarden is in a different region, verify network latency is acceptable for sync interval

- [ ] **Dependency vulnerability scanning:**
  - NuGet packages: `dotnet list package --vulnerable`
  - npm packages (dashboard): `npm audit` or `bun audit`
  - Container images: Trivy / Grype scanning in CI/CD

- [ ] **License compliance** — verify all dependencies have compatible licenses (MIT, Apache-2.0, etc.)

---

## Quick Reference: Key File Paths

| Component | File | Purpose |
|---|---|---|
| RBAC template | `charts/.../templates/rbac.yaml` | Helm RBAC configuration |
| YAML attachment processing | `VaultwardenK8sSync/Services/SyncService.cs` (lines 566-589, 725-773) | Extracts and applies K8s YAML from attachments |
| YAML apply logic | `VaultwardenK8sSync/Services/KubernetesService.cs` (lines 990-1031) | Dispatches YAML to typed handlers |
| YAML detection | `VaultwardenK8sSync/Services/SyncService.cs` (lines 1148-1174) | `IsKubernetesYaml()` validator |
| Metrics | `VaultwardenK8sSync/Services/MetricsService.cs` | All metric definitions |
| Metrics server | `VaultwardenK8sSync/Infrastructure/MetricsServer.cs` | HTTP server for `/metrics` |
| Health check | `VaultwardenK8sSync/HealthChecks/SyncHealthCheck.cs` | Sync health probe |
| Process lock | `VaultwardenK8sSync/Infrastructure/ProcessLock.cs` | Single-instance enforcement |
| Global sync lock | `VaultwardenK8sSync/Infrastructure/GlobalSyncLock.cs` | Redis-backed cross-instance coordination |
| API program | `VaultwardenK8sSync.Api/Program.cs` | /health, /metrics, middleware config |
| Auth middleware | `VaultwardenK8sSync.Api/Middleware/TokenAuthenticationMiddleware.cs` | Dashboard auth |
| Helm chart | `charts/vaultwarden-kubernetes-secrets/` | Deployment configuration |

---

> **Review frequency**: This checklist should be reviewed before every production deployment and quarterly thereafter.  
> **Owner**: Platform / Infrastructure team responsible for the cluster where Vaultwarden K8s Secrets runs.
