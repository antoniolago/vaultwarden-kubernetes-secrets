# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-05-23

### Added
- **Kubernetes YAML from Secure Notes**: Secure Notes with valid K8s YAML are automatically applied via `kubectl apply`, even without the `namespaces` custom field. Uses `metadata.namespace` from the manifest when no explicit namespace is set.
- **`context-name` custom field**: Multi-cluster filtering with automatic kubeconfig context detection.
- **`stringData` mode**: Extract `stringData:` blocks from notes as secret key-value pairs, including multiline YAML values.
- **SSH key support**: Parse SSH keys (`private-key`, `public-key`, `fingerprint`) from cipher text
- **Drift detection**: Detect and reconcile Kubernetes Secrets that are modified or deleted externally between sync cycles.
- **API key authentication**: Authenticate via OAuth2 `client_credentials` flow with API key instead of raw password hash. Uses `api` scope for user API key login.
- **Memory instrumentation**: Log memory usage (GC heap size) at key sync choke points for performance monitoring.
- **Individual secret names in sync summary**: Sync summary now lists individual secret names for created/updated secrets.
- **Configurable database path**: `databasePath` is now configurable via Helm values instead of being hardcoded to `/data/sync.db`.
- **Configurable nginx API upstream**: Dashboard nginx template supports configurable API upstream via `API_HOST`/`API_PORT` envsubst.
- **`imagePullSecrets` support**: Helm chart supports private registry image pulls via `imagePullSecrets`.
- **Dashboard authentication**: Token-based auth for dashboard with loginless mode for E2E testing.
- **Dashboard E2E test suite**: Full Playwright E2E test suite running in CI, covering secrets sync, SSH keys, stringData, context filtering, and dashboard views.
- **E2E test infrastructure**:
  - `docker-compose.e2e.yml` with profiles for sync, API, and dashboard services.
  - `e2e-helper.py` for test data seeding and orchestration.
  - `mock-k8s-server.py` and `mock-kubeconfig.yaml` for isolated K8s API testing.
  - `register-user.py` script for test user registration.
  - Vaultwarden v1.35+ support (registration endpoint removed).
  - SQLite seed archives with properly checkpointed vaultwarden databases.
- **Bitwarden-Client-Version header**: Added required header for Vaultwarden API compatibility.
- **Stress test script**: `scripts/stress-test-memory.sh` for memory profiling under load.
- **Diagnostics script**: `scripts/diagnose-sync-failures.sh` for troubleshooting sync issues.

### Changed
- **Breaking**: Default secret key names changed from item-derived names to fixed names. Use `secret-key-username` and `secret-key-password` custom fields to override.
  - `password` (was derived from item name)
  - `username` (was derived from item name)
  - `private-key`, `public-key`, `fingerprint` for SSH key material.
- README restructured: split into separate `README.md` and `EXAMPLES.md` files.
- Logging: demoted `[MEMORY]` and `[YAML]` log messages from `Information` to `Debug` level.
- Helm chart: `api-deployment.yaml` and `dashboard-deployment.yaml` templates updated for configurable image registries.
- `SyncSummary` model extended with detailed per-secret tracking (created, updated, skipped, deleted).
- `GlobalSyncLock` uses configurable lock file names per SyncService instance.
- Dockerfiles updated for sync service, API, and dashboard (build args, env vars).

### Performance
- Reduced memory footprint at key sync choke points (GC heap allocation optimization).
- `SecretExistsCache` with configurable size (max 10,000 entries) and eviction policy.

### Removed
- `ProcessFactory` and `ProcessRunner` infrastructure (unreferenced dead code).
- `Serilog.Sinks.File` dependency.
- `register-user.py` replaced by `e2e-helper.py register`.
- `settings.json.orig` (stale file).

### Fixed
- SSH key data now correctly parsed from cipher text in Vaultwarden responses.
- SSH keys properly hydrated in `VerifyK8sStateAsync` to match `SyncSecretAsync` hash comparison.
- YAML-in-notes processing runs before namespace sync loop, preventing false-positive drift detection.
- `GetContentHash` normalizes notes to prevent `QuickHash` alternation on unchanged content.
- YAML manifests from items with `namespaces` field are applied before the sync loop starts.
- Helm chart template bugs resolved.
- Integration tests stabilized (DB isolation between test runs, metrics registration cleanup).
- Dashboard E2E tests hardened: timeouts increased, `waitForLoadState('networkidle')` replaced with targeted selectors, sync-polling beforeAll hooks added.
- API rate limit increased from 20 to 5000 for E2E tests with Vaultwarden.
- Flaky Playwright tests: `waitForSyncComplete` retry reduced to 1s, modal timeouts increased to 15s, secrets spec timeouts increased.
- Dashboard E2E heading selector fixed (emoji removed from actual component).
- E2E image detection bug fixed; API image pre-built in CI.
- Vaultwarden TLS proxy connection failures handled with retry logic.
- E2E SSL certificate generation fixed for TLS proxy healthcheck.
- `docker-compose.e2e.yml` dependency ordering fixed (api should not depend_on sync).
- SQLite3 installation fixed: apk → static binary → apt-get for Debian-based vaultwarden images.

## [1.4.0] - 2026-04-12

### Added
- Support for `kubernetes.io/dockerconfigjson` secret type via the `secret-type` custom field.

### Fixed
- Deleted Vaultwarden items are now excluded from sync. Previously, deleted items caused orphan cleanup issues.
- Fallback secret key is no longer created when data is already present in custom fields.

## [1.3.0] - 2026-03-15

### Added
- `secret-type` custom field for specifying the Kubernetes Secret type. Supports `Opaque`, `kubernetes.io/tls`, and `kubernetes.io/basic-auth`.

## [1.2.8] - 2026-02-05

### Added
- Persistent device ID to prevent repeated "New device logged in" email notifications from Vaultwarden.

## [1.2.0] - 2026-02-02

### Changed
- Removed Bitwarden CLI (`bw`) dependency. Switched to direct API calls via the VwConnector package, eliminating the need for a separate CLI binary.

### Added
- End-to-end tests covering the full sync workflow.

### Performance
- Significant performance improvements from removing the subprocess-based Bitwarden CLI interface.

[unreleased]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v1.4.0...v2.0.0
[1.4.0]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v1.2.0...v1.3.0
[1.2.8]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/compare/v1.2.0...v1.2.8
[1.2.0]: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/releases/tag/v1.2.0
