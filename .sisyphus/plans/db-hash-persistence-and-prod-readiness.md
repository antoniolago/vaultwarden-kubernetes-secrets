# DB Hash Persistence + Prod Readiness + E2E Test Environment

## TL;DR

> **Core Objective**: Fix hash persistence across process restarts by storing content hashes in the SQLite database instead of relying solely on K8s annotations (which an admission controller strips). Then build a comprehensive e2e test environment and create a production-readiness checklist.
>
> **Deliverables**:
> - Complete `ContentHash` column on `SecretStates` table (DB schema change)
> - `GetSecretHashAsync` / `UpdateSecretHashAsync` in repo + service layers (DONE: interfaces + implementation written)
> - `SyncSecretAsync` hash comparison reads from DB instead of K8s annotations (PARTIALLY DONE: read path updated)
> - Hash storage on create/update persisted to DB (PARTIALLY DONE: one caller updated, 9 remaining)
> - Downstream `UpsertSecretStateAsync` callers pass `contentHash` parameter (9 remaining SyncService.cs callers)
> - Integration test: hash survives `dotnet run` restart by writing known hash, disposing DB, re-reading
> - Lite e2e: Docker Compose with Vaultwarden + sync + dashboard + API in isolated network
> - Production readiness checklist document
> - RBAC analysis for YAML K8s object attachments

## Context

### Original Request
A Vaultwarden item name change should only update 1 secret, not all 47. The CommandHandler fix (only overwrite `previousSummary` when HasChanges) solved the delta display for continuous syncs. But on every `dotnet run`, the **first sync shows all 47 secrets as Updated** because:
1. The `_lastItemsHash` field is `null` after restart → reconciliation always runs
2. Within reconciliation, the content hash comparison reads from K8s annotations
3. A **MutatingAdmissionWebhook** strips/overwrites annotations on Update — so the NEW hash (`combinedHash`) that the app writes is **not persisted** to the K8s secret
4. Next restart: annotation still has the OLD hash → `hasHashChanged = true` → secret gets Updated again

### Root Cause Evidence
User confirmed with debug logging:
```
[22:58:20] Hash mismatch. Old: 'zg6y2pv...' (44 chars), New: 'JNWolo...' (44 chars), isNull=False
[23:00:00] Hash mismatch. Old: 'zg6y2pv...' (44 chars), New: 'JNWolo...' (44 chars), isNull=False
```

Same OLD hash across consecutive runs — the annotation is never updated because an external admission controller strips/stomps on the annotation on every K8s Secret Update.

### Decision
**Move hash storage from K8s annotations to the SQLite database.** The `SecretStates` table already has a `(Namespace, SecretName)` unique index — perfect for this. The DB persists across restarts, not subject to admission webhooks.

### What's Already Done
- `SecretState.cs`: Added `ContentHash` column
- `ISecretStateRepository.cs`: Added `GetSecretHashAsync()`, `UpdateSecretHashAsync()`
- `SecretStateRepository.cs`: Implemented both methods
- `IDatabaseLoggerService.cs`: Added `GetSecretHashAsync()`, `UpdateSecretHashAsync()` to interface; updated `UpsertSecretStateAsync` signature with optional `contentHash` parameter
- `DatabaseLoggerService.cs`: Implemented `GetSecretHashAsync()`, `UpdateSecretHashAsync()`; updated `UpsertSecretStateAsync` to pass `ContentHash` to model
- `SyncService.cs` line ~1771: Changed hash read from `GetSecretAnnotationsAsync` to `_dbLogger.GetSecretHashAsync()`
- `SyncService.cs` line ~1882: Added `_dbLogger.UpdateSecretHashAsync()` after successful K8s update
- `SyncService.cs` line ~2010: Updated `UpsertSecretStateAsync` call to pass `contentHash`

---

## Work Objectives

### Core Objective
Make hash change detection survive process restarts by storing hashes in SQLite, then build a production-quality e2e test environment and document the prod-readiness checklist.

### Concrete Deliverables
- [x] DB hash persistence fully implemented and tested
- [x] All 9 remaining `UpsertSecretStateAsync` callers pass `contentHash`
- [x] `dotnet test` — all tests pass
- [x] New integration test: hash persists across simulated restart
- [ ] Lite e2e: Docker Compose with real Vaultwarden
- [ ] Production readiness checklist document (`.sisyphus/drafts/prod-readiness-checklist.md`)
- [ ] RBAC analysis for YAML K8s object attachments
- [ ] Demo environment design with safety guardrails

### Definition of Done
- [x] `dotnet test VaultwardenK8sSync.Tests` passes: 640+ tests, 0 failures (excluding pre-existing Redis flaky test)
- [x] New test "HashFromDb_PersistsAcrossRestarts" reads hash from freshly created DB after disposal
- [ ] Docker Compose e2e: `docker compose up` starts Vaultwarden + sync + dashboard + API → sync runs → item changes propagate correctly
- [ ] `dotnet run` restart shows "Skipped" (not "Updated") for unchanged items on first sync

### Must Have
- DB hash replaces annotation hash as primary comparison source
- Annotations still written (backward compat, won't hurt)
- No new NuGet dependencies
- E2e test is lite (< 5 min), runs locally, cleans up after itself

### Must NOT Have (Guardrails)
- NO changes to `VaultwardenK8sSync.Api` or `dashboard` projects
- NO EF Core migrations — we rely on `EnsureCreated()` which auto-creates new columns
- NO breaking changes to the Helm chart schema
- NO persisting `previousSummary` across restarts (we only persist hash; summary is ephemeral)

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (xUnit + Moq + FluentAssertions)
- **Automated tests**: YES (TDD for new tests, tests-after for regression)
- **Framework**: xUnit / Moq / FluentAssertions

### QA Policy
Every task MUST include agent-executed QA scenarios.

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Complete DB hash — 1 executor sequential):
├── Task 1: Update 9 remaining UpsertSecretStateAsync callers with contentHash
├── Task 2: Build & run all existing tests (verify no regressions)
├── Task 3: Write integration test: HashFromDb_PersistsAcrossRestarts
└── Task 4: Build & run all tests with new test

Wave 2 (Lite e2e test environment — 1 executor sequential):
├── Task 5: Create Docker Compose for Vaultwarden + sync + dashboard + API
├── Task 6: Create test scenario script (create item → sync → verify → rename → sync → verify)
└── Task 7: Verify e2e works: item name change only updates 1 secret on restart

Wave 3 (Prod readiness — parallel documents):
├── Task 8: Production readiness checklist
├── Task 9: RBAC analysis for YAML K8s object attachments
├── Task 10: Demo environment design with safety guardrails
└── Task 11: Enhancement suggestions (creative, from existing codebase)

Wave FINAL (Verification):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review
├── Task F3: Real manual QA (run e2e, verify hash persistence)
└── Task F4: Scope fidelity check
```

### Dependency Matrix
- **1**: - → 2-4
- **2-4**: 1 → 5
- **5-7**: 4 → 8-11
- **8-11**: - → F1-F4

---

## TODOs

- [x] 1. Update 9 remaining `UpsertSecretStateAsync` callers in `SyncService.cs` to pass `contentHash`

  **What to do**:
  - In `SyncService.cs`, find ALL 9 remaining callers of `_dbLogger.UpsertSecretStateAsync(...)` that don't pass contentHash
  - For each caller, determine the right `combinedHash` value to pass:
    - Skip/create paths (lines 457, 517): pass `currentQuickHash` or `null` — these are orphan cleanup paths where there's no combinedHash from SyncSecretAsync
    - YAML manifest paths (lines 1396, 1417): pass `null` — these are post-sync, not part of secret content hash
    - Failed namespace (line 1578): pass `null`
    - Mixed secret type error (line 1619): pass `null`
    - Dry run (line 1684): pass `null` (dry run doesn't write)
    - Skipped success (line 1858): pass `combinedHash` (the hash was computed, just matched existing)
    - Exception handler (line 2034): pass `null` (operation failed, don't store untrusted hash)
  - Add the 8th `contentHash` parameter with appropriate value to each call
  - **Do NOT** change any existing callers outside SyncService.cs (test mocks use optional parameter defaults)

  **Files to modify**:
  - `VaultwardenK8sSync/Services/SyncService.cs` — multiple callers of `_dbLogger.UpsertSecretStateAsync`

  **QA Scenarios**:

  ```
  Scenario: Build passes after updating all callers
    Tool: Bash
    Steps:
      1. dotnet build VaultwardenK8sSync.sln
    Expected Result: Build succeeds (exit code 0), no CS7036 or method-overload errors
    Evidence: .sisyphus/evidence/task-1-build-pass.txt
  ```

- [x] 2. Build and run all existing tests (verify no regressions)

  **What to do**:
  - Run `dotnet build` first to ensure compilation
  - Run `dotnet test VaultwardenK8sSync.Tests` with normal verbosity
  - Check for test failures — specifically the pre-existing `GlobalSyncLockTests.DisposeAsync_AfterAcquire_ReleasesLock` (known Redis flaky)
  - Verify all SecretChangeBehaviorTests, NoChangesSummaryTests, SyncSummaryFormatterTests pass

  **Files to check**:
  - Test output only (no file changes)

  **QA Scenarios**:

  ```
  Scenario: All existing tests pass
    Tool: Bash
    Steps:
      1. dotnet test VaultwardenK8sSync.Tests --verbosity normal 2>&1 | tail -20
    Expected Result: 640+ passed, 0 or 1 failed (pre-existing Redis flaky)
    Evidence: .sisyphus/evidence/task-2-test-results.txt

  Scenario: Hash-related tests specifically pass
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~SecretChangeBehaviorTests|FullyQualifiedName~NoChangesSummaryTests|FullyQualifiedName~SyncSummaryFormatterTests"
    Expected Result: All pass
    Evidence: .sisyphus/evidence/task-2-hash-tests.txt
  ```

- [x] 3. Write integration test: `HashFromDb_PersistsAcrossRestarts`

  **What to do**:
  - Create a test in `DatabaseIntegrationTests.cs` or a new test file in `VaultwardenK8sSync.Tests/`
  - The test should:
    1. Create a real SQLite in-memory database (using EF Core with `UseSqlite("DataSource=:memory:")`)
    2. Create a `DatabaseLoggerService` with this real DB
    3. Call `UpdateSecretHashAsync("test-ns", "test-secret", "known-hash-value")`
    4. Dispose the scope/DB context
    5. Create a NEW `DatabaseLoggerService` with a NEW DB context (same connection string)
    6. Call `GetSecretHashAsync("test-ns", "test-secret")`
    7. Assert returns "known-hash-value"
  - Use `[Collection("Database")]` if the test project has database test collections

  **Rationale**: This simulates a process restart — the DB persists on disk, and a new process instance reads the hash back.

  **Files to create/modify**:
  - `VaultwardenK8sSync.Tests/DatabaseIntegrationTests.cs` — add new test method
  - OR create `VaultwardenK8sSync.Tests/HashPersistenceTests.cs`

  **References**:
  - Existing DatabaseIntegrationTests.cs patterns (in-memory SQLite, scope factory usage)
  - SecretStateRepository.cs Upsert/GetByNamespaceAndNameAsync methods

  **QA Scenarios**:

  ```
  Scenario: Hash survives DB disposal and re-creation
    Tool: Bash
    Steps:
      1. dotnet test --filter "FullyQualifiedName~HashFromDb_PersistsAcrossRestarts"
    Expected Result: Test passes — hash value matches after dispose+recreate
    Evidence: .sisyphus/evidence/task-3-hash-persistence.txt
  ```

- [x] 4. Build and run all tests with new test

  **What to do**:
  - Run full test suite again now that the new test is added
  - Verify no regressions from the new test
  - Fix any test issues if they arise

  **Files to check**: Test output only

  **QA Scenarios**:

  ```
  Scenario: Full test suite with new test passes
    Tool: Bash
    Steps:
      1. dotnet test VaultwardenK8sSync.Tests --verbosity normal 2>&1 | tail -20
    Expected Result: New test passes, all previous tests still pass
    Evidence: .sisyphus/evidence/task-4-full-suite.txt
  ```

- [ ] 5. Create Docker Compose lite e2e test environment

  **What to do**:
  - Create `docker-compose.e2e.yml` in project root with:
    - **Vaultwarden** service: `vaultwarden/server:latest` on port 8080
    - **Sync service** (console app): build from `VaultwardenK8sSync/Dockerfile`
    - **Dashboard** (optional): build from `dashboard/Dockerfile` on port 5173
    - **API** (optional): build from `VaultwardenK8sSync.Api/Dockerfile` on port 5000
  - Use a shared network so they can communicate
  - Use environment variables to configure the sync service to point at the local Vaultwarden
  - For the K8s interaction: use `--dry-run` mode or a Kind cluster if available
  - If using dry-run: the sync runs, logs what it would do, but doesn't actually create K8s secrets
  - The test environment should be ephemeral (no persistent volumes for test data)

  **Files to create**:
  - `docker-compose.e2e.yml`

  **References**:
  - Existing Dockerfiles: `VaultwardenK8sSync/Dockerfile`, `dashboard/Dockerfile`, `VaultwardenK8sSync.Api/Dockerfile`

  **QA Scenarios**:

  ```
  Scenario: Docker Compose starts and sync runs
    Tool: Bash
    Steps:
      1. docker compose -f docker-compose.e2e.yml up --build -d
      2. sleep 30
      3. docker compose -f docker-compose.e2e.yml logs sync 2>&1 | tail -40
      4. docker compose -f docker-compose.e2e.yml down -v
    Expected Result: Sync service starts, connects to Vaultwarden, logs "No items found" or processes items
    Evidence: .sisyphus/evidence/task-5-compose-up.txt
  ```

- [ ] 6. Create test scenario script for e2e

  **What to do**:
  - Create a `scripts/e2e-test.sh` bash script that:
    1. Starts the Docker Compose environment
    2. Uses `bw` CLI (or curl to Vaultwarden API) to create a test item with known name/fields
    3. Waits for the sync service to process it (check logs)
    4. Renames the item
    5. Waits for next sync cycle
    6. Checks logs: should show only 1 Updated, not all items
    7. Stops and cleans up everything
  - The script should exit 0 on success, non-zero on failure

  **Files to create**:
  - `scripts/e2e-test.sh`

  **QA Scenarios**:

  ```
  Scenario: E2e test passes end-to-end
    Tool: Bash
    Steps:
      1. bash scripts/e2e-test.sh
    Expected Result: Exit code 0, output shows "E2E TEST PASSED"
    Evidence: .sisyphus/evidence/task-6-e2e-result.txt
  ```

- [ ] 7. Verify e2e: name change only updates 1 secret after restart

  **What to do**:
  - This is the CORE behavioral test: prove the fix works end-to-end
  - The e2e script should specifically:
    1. Start clean (down -v)
    2. Start environment
    3. Create 3 test items in Vaultwarden
    4. Run sync once — verify all 3 created
    5. Stop sync service (simulate restart)
    6. Start sync service again
    7. Verify first sync shows all 3 as Skipped (hash matches from DB)
    8. Rename 1 item
    9. Verify sync shows only 1 Updated + 2 Skipped
  - Log all steps for evidence

  **Files to modify**:
  - `scripts/e2e-test.sh` — add this scenario

  **QA Scenarios**:

  ```
  Scenario: Restart e2e test
    Tool: Bash
    Steps:
      1. bash scripts/e2e-test.sh
    Expected Result: Exit code 0, output shows "RESTART TEST PASSED"
    Evidence: .sisyphus/evidence/task-7-restart-test.txt
  ```

- [ ] 8. Production readiness checklist

  **What to do**:
  - Write `.sisyphus/drafts/prod-readiness-checklist.md` covering:
    - **Monitoring**: Prometheus metrics exposed by sync service + API; what dashboards to set up
    - **Alerting**: When sync hasn't run for >2x interval; when failures > threshold; when orphan count spikes
    - **Backup**: SQLite database backup strategy (WAL mode, daily snapshots)
    - **Disaster Recovery**: Rebuilding DB from K8s state; resync from Vaultwarden
    - **Security**: RBAC, network policies, secret encryption at rest, TLS for API/dashboard
    - **Scaling**: Single-instance sync enforced by ProcessLock; read-heavy API scales horizontally with Redis-backed GlobalSyncLock
    - **Logging**: Structured logging (Serilog) — what to log at Info vs Debug; log retention
    - **Upgrades**: Zero-downtime strategy; Helm chart upgrade path; migration scripts
    - **SLO**: Expected sync latency p99 < 30s; uptime targets

  **Format**: Markdown with checkboxes per category

  **Files to create**:
  - `.sisyphus/drafts/prod-readiness-checklist.md`

  **QA Scenarios**:

  ```
  Scenario: Checklist file exists and is comprehensive
    Tool: Bash
    Steps:
      1. head -20 .sisyphus/drafts/prod-readiness-checklist.md
    Expected Result: File exists, contains meaningful content with 8+ categories
    Evidence: .sisyphus/evidence/task-8-checklist.txt
  ```

- [ ] 9. RBAC analysis for YAML K8s object attachments

  **What to do**:
  - Analyze the SyncService's `ProcessAttachmentsAsync` method and how it handles YAML K8s object attachments
  - YAML attachments can define arbitrary K8s objects (Deployments, Services, Ingresses, etc.) — not just Secrets
  - The service currently creates/proxies these objects to K8s via `_kubernetesService.ApplyYamlManifestAsync()`
  - Document the required RBAC permissions:
    - Current: `ClusterRole` rules for `secrets` (get, list, watch, create, update, patch, delete)
    - If YAML attachments can create arbitrary objects: need `*` on many resource types, OR a scoped approach
  - Create a scoped solution:
    - **Option A**: Require users to specify `yaml-rbac: [list of resources]` custom field per item
    - **Option B**: Process all YAML through a single `ResourceQuota`-like validator that checks what resources it's allowed to create
    - **Option C**: Run a separate service with elevated ClusterRole for YAML objects only
  - Write the findings to the checklist

  **Files to modify**:
  - `.sisyphus/drafts/prod-readiness-checklist.md` — add RBAC section

  **References**:
  - `SyncService.cs:ProcessAttachmentsAsync()` method
  - `KubernetesService.cs:ApplyYamlManifestAsync()` method
  - Helm chart: `charts/vaultwarden-kubernetes-secrets/templates/rbac.yaml`

  **QA Scenarios**:

  ```
  Scenario: RBAC document includes permission matrix
    Tool: Bash
    Steps:
      1. grep -c "ClusterRole\|permission\|RBAC" .sisyphus/drafts/prod-readiness-checklist.md
    Expected Result: At least 5 references to RBAC concerns
    Evidence: .sisyphus/evidence/task-9-rbac.txt
  ```

- [ ] 10. Demo environment design with safety guardrails

  **What to do**:
  - Design a demo environment architecture that:
    - Runs the full stack: Vaultwarden + Sync + API + Dashboard + Kind cluster (or K3s)
    - Is safe for public access: no persistent volumes (ephemeral), authentication on dashboard, rate limiting
    - Auto-resets periodically (cron job that nukes and recreates)
    - Has resource limits (CPU/memory quotas)
    - Can't exploit/destroy the host: no host mounts, no privileged containers, no host network
    - Uses a dedicated non-root user
  - Document as a Docker Compose or Helmfile that references the main chart
  - Consider two modes:
    - **Local mode**: Full environment with Kind cluster, K8s-in-Docker (kind)
    - **Cloud demo mode**: Single Helm deployment with restricted values

  **Files to create**:
  - `.sisyphus/drafts/demo-environment-design.md`

  **QA Scenarios**:

  ```
  Scenario: Demo design document exists
    Tool: Bash
    Steps:
      1. cat .sisyphus/drafts/demo-environment-design.md | head -10
    Expected Result: File exists with architecture description
    Evidence: .sisyphus/evidence/task-10-demo-design.txt
  ```

- [ ] 11. Enhancement suggestions (creative, from existing codebase knowledge)

  **What to do**:
  - Analyze the existing codebase and suggest enhancements:
    1. **Vaultwarden webhook-triggered sync**: Instead of polling, use Vaultwarden webhooks to trigger sync immediately when an item changes. Faster reaction, less resource waste.
    2. **Per-item last-synced tracking**: Show which items were actually synced vs skipped in the dashboard. Currently only secret-level tracking.
    3. **Selective item sync via labels**: Let users define `bw-tags` or custom field to include/exclude items from sync, instead of just org/collection/folder filters.
    4. **Multi-architecture Docker images**: Build and push `linux/amd64` and `linux/arm64` manifests.
    5. **Health endpoint with dependency checks**: The API health endpoint currently doesn't check if the sync service is alive. Add Redis/Vaultwarden connectivity checks.
    6. **Configurable log level at runtime**: Expose a `/logs/level` endpoint on the API to dynamically change log level without restart.
    7. **Secret drift detection**: Periodically verify that the K8s secret data still matches what Vaultwarden has (detects manual edits or corruption).
    8. **Read-only API mode**: For dashboard users who shouldn't trigger resyncs.
    9. **Audit log export**: Export sync logs to external SIEM via webhook or syslog.
    10. **Dual-runner mode**: For HA, allow two sync instances where one is passive standby (reads DB to know last sync state).

  - Document suggestions with: benefit, effort estimate, risk assessment
  - Write to the scratchpad

  **Files to create**:
  - `.sisyphus/drafts/enhancement-suggestions.md`

  **QA Scenarios**:

  ```
  Scenario: Enhancement suggestions document exists
    Tool: Bash
    Steps:
      1. wc -l .sisyphus/drafts/enhancement-suggestions.md
    Expected Result: 50+ lines of documented suggestions
    Evidence: .sisyphus/evidence/task-11-enhancements.txt
  ```

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, run `dotnet build`, check test output). For each "Must NOT Have": search for forbidden patterns — reject with file:line if found. Check evidence files exist in `.sisyphus/evidence/`. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build VaultwardenK8sSync.sln` + all tests. Review all changed files for: `as any`/`@ts-ignore`, empty catches, console.log in prod, commented-out code, unused imports. Check AI slop: excessive comments, over-abstraction, generic names.
  Output: `Build [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Run the e2e Docker Compose environment. Execute the restart scenario from Task 7. Verify: first sync after restart shows Skipped for unchanged items, item name change only affects 1 item. Check evidence files.
  Output: `Scenarios [N/N pass] | Integration [N/N] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual changes. Verify everything in spec was built (no missing), nothing beyond spec was built (no creep). Check "Must NOT do" compliance. Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

## Commit Strategy

- **1-4**: `fix(sync): store content hash in SQLite for restart-safe change detection`
  - `VaultwardenK8sSync.Database/Models/SecretState.cs`
  - `VaultwardenK8sSync.Database/Repositories/ISecretStateRepository.cs`
  - `VaultwardenK8sSync.Database/Repositories/SecretStateRepository.cs`
  - `VaultwardenK8sSync/Services/IDatabaseLoggerService.cs`
  - `VaultwardenK8sSync/Services/DatabaseLoggerService.cs`
  - `VaultwardenK8sSync/Services/SyncService.cs`
  - `VaultwardenK8sSync.Tests/DatabaseIntegrationTests.cs` (or new test file)
  - Pre-commit: `dotnet test`

- **5-7**: `test(e2e): add Docker Compose environment with real Vaultwarden for restart persistence tests`
  - `docker-compose.e2e.yml`
  - `scripts/e2e-test.sh`
  - Pre-commit: `bash scripts/e2e-test.sh`

- **8-11**: `docs: production readiness checklist, RBAC analysis, demo design, enhancement suggestions`
  - `.sisyphus/drafts/prod-readiness-checklist.md`
  - `.sisyphus/drafts/demo-environment-design.md`
  - `.sisyphus/drafts/enhancement-suggestions.md`

## Success Criteria

### Verification Commands
```bash
dotnet test VaultwardenK8sSync.Tests
# Expected: 640+ passed, 0 failed (redis flaky excluded)

docker compose -f docker-compose.e2e.yml up --build --abort-on-container-exit
# Expected: sync runs, test scenario completes, exit 0

docker compose -f docker-compose.e2e.yml down -v
# Expected: clean teardown

bash scripts/e2e-test.sh
# Expected: exit 0, "E2E TEST PASSED" + "RESTART TEST PASSED"
```

### Final Checklist
- [x] DB hash read: `_dbLogger.GetSecretHashAsync()` replaces annotation-based hash comparison
- [x] DB hash write: `_dbLogger.UpdateSecretHashAsync()` called after every Create + Update
- [x] 9 caller updates compile: `dotnet build` passes
- [x] All tests pass: `dotnet test` shows 640+ passed
- [x] New test: `HashFromDb_PersistsAcrossRestarts` — hash value survives DB context recreation
- [ ] E2e: `dotnet run` restart shows unchanged items as Skipped on first sync
- [ ] E2e: Item name change only updates 1 secret on next sync
- [ ] Prod readiness checklist has 8+ categories with actionable items
- [ ] RBAC analysis documents required permissions per component, with YAML attachment scoping
- [ ] Demo environment design has explicit safety guardrails (no host mounts, no privileged, ephemeral)
