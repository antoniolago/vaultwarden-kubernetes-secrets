# .NET 10 Upgrade Design

**Date:** 2026-01-15
**Status:** Approved
**Owner:** Development Team

## Overview

Upgrade the Vaultwarden Kubernetes Secrets project from .NET 9.0 to .NET 10.0 to achieve performance improvements and maintain current support status.

## Motivation

- **Performance improvements:** Leverage .NET 10's runtime and compiler enhancements
- **Stay current:** Keep dependencies up-to-date and supported
- **Security:** Benefit from latest security patches and improvements

## Primary Concerns

1. **Breaking changes:** APIs or behaviors that changed between .NET 9 and 10
2. **Dependencies:** NuGet packages that might not support .NET 10 yet, particularly VwConnector

## Overall Strategy

**Phased rollout with validation gates.** Each phase completes successfully before moving to the next, making it easy to identify and fix issues as they arise.

### Phase 1: Local Development Environment

**Goal:** Get the solution building and tests passing on .NET 10 locally, before touching Docker or CI/CD.

**Steps:**
1. Update all `.csproj` files to target `net10.0` instead of `net9.0`
2. Update Microsoft.* packages to version `10.0.x` equivalents
3. Run `dotnet restore` and handle any immediate conflicts
4. Run `dotnet build` and fix compilation errors (if any)
5. Run full test suite (`dotnet test`) and ensure all tests pass
6. Check VwConnector compatibility - if it works, great; if not, address separately

**Validation Gate:** ✅ All tests pass locally on .NET 10

### Phase 2: Docker Images

**Goal:** Update container base images to use .NET 10 SDK and runtime.

**Files to modify:**
- `VaultwardenK8sSync/Dockerfile` - Lines 2 and 21 (SDK and aspnet base images)
- `VaultwardenK8sSync.Api/Dockerfile` - Lines 1 and 5 (aspnet base and SDK images)

**Changes:**
- `mcr.microsoft.com/dotnet/sdk:9.0` → `mcr.microsoft.com/dotnet/sdk:10.0`
- `mcr.microsoft.com/dotnet/aspnet:9.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`

**Local validation:**
- Build both Docker images locally
- Run containers to verify they start successfully
- Use existing test scripts: `./scripts/test-docker-image.sh`

**Validation Gate:** ✅ Docker images build and containers start successfully

### Phase 3: CI/CD Pipeline

**Goal:** Update GitHub Actions workflow to use .NET 10 SDK for builds and tests.

**File:** `.github/workflows/docker-publish.yml`
- Line 25: `dotnet-version: '9.0.x'` → `dotnet-version: '10.0.x'`
- Line 99: Same change for security job

**Validation Gate:** ✅ CI pipeline runs successfully on test branch

## Dependency Management

### Microsoft Package Updates

The following Microsoft packages need version bumps from `9.0.x` to `10.0.x`:

**VaultwardenK8sSync.csproj:**
- `Microsoft.EntityFrameworkCore.Sqlite`: 9.0.0 → 10.0.x
- `Microsoft.Extensions.Configuration`: 9.0.10 → 10.0.x
- `Microsoft.Extensions.Configuration.CommandLine`: 9.0.10 → 10.0.x
- `Microsoft.Extensions.Configuration.EnvironmentVariables`: 9.0.10 → 10.0.x
- `Microsoft.Extensions.Configuration.Json`: 9.0.10 → 10.0.x
- `Microsoft.Extensions.Diagnostics.HealthChecks`: 9.0.10 → 10.0.x
- `Microsoft.Extensions.Http.Polly`: 9.0.10 → 10.0.x
- `Microsoft.Extensions.Logging`: 9.0.10 → 10.0.x
- `Microsoft.Extensions.Logging.Console`: 9.0.10 → 10.0.x
- `Microsoft.Extensions.Logging.Debug`: 9.0.10 → 10.0.x

**VaultwardenK8sSync.Api.csproj:**
- `Microsoft.AspNetCore.OpenApi`: 9.0.0 → 10.0.x
- `Microsoft.EntityFrameworkCore.Design`: 9.0.0 → 10.0.x
- `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`: 9.0.0 → 10.0.x

**VaultwardenK8sSync.Tests.csproj:**
- `Microsoft.AspNetCore.Mvc.Testing`: 9.0.0 → 10.0.x
- `Microsoft.NET.Test.Sdk`: 17.8.0 → 17.12.0 (latest compatible version)

### Third-Party Packages Strategy

**Leave at current versions initially** (test compatibility first):
- `VwConnector` (1.34.2-rev.1) - Critical dependency, test as-is first
- `KubernetesClient` (17.0.14) - Usually backward compatible
- `StackExchange.Redis` (2.9.32) - Stable, well-maintained
- `Polly` (8.6.4) - Already on v8, should work fine
- `prometheus-net` (8.2.1) - Stable library
- `Spectre.Console` (0.52.0) - Recent version
- `dotenv.net` (4.0.0) - Simple library, likely compatible
- `Swashbuckle.AspNetCore` (7.2.0) - May need update if OpenAPI has breaking changes

**Testing strategy:** After initial migration, check if any packages have newer versions specifically for .NET 10. Only upgrade if there are compatibility issues or security fixes.

### VwConnector Contingency Plan

If VwConnector doesn't work with .NET 10:

- **Option A (Preferred):** Open GitHub issue with maintainer, include error details
- **Option B:** Temporarily multi-target if source is available: `<TargetFrameworks>net9.0;net10.0</TargetFrameworks>`
- **Option C:** Abstract behind an interface (`IVaultwardenConnector`) to make replacement easier
- **Option D:** Fork and update ourselves (last resort)

We won't know until Phase 1 testing, but having a plan reduces risk.

## Risk Mitigation & Testing

### Breaking Changes to Watch For

Based on .NET 9 → 10 migration patterns:

**1. ASP.NET Core Minimal API Changes**
- The API project uses minimal APIs (Web SDK)
- .NET 10 may have changes to endpoint routing or middleware ordering
- **Mitigation:** Run integration tests in `VaultwardenK8sSync.Tests/Integration/`

**2. Entity Framework Core Migrations**
- EF Core 10 may have query translation changes
- SQLite provider behavior might differ
- **Mitigation:** Test database operations thoroughly; check for obsolete API warnings during build

**3. Configuration & Dependency Injection**
- Microsoft.Extensions.* packages may have subtle DI container changes
- **Mitigation:** The existing health checks and startup validation will catch issues

**4. Nullable Reference Types**
- Compiler may be stricter about nullability warnings
- Project already has `<Nullable>enable</Nullable>`
- **Mitigation:** Treat warnings as errors during migration to catch issues early

### Test Coverage Strategy

**Unit Tests (Primary Safety Net):**
- Run after every change: `dotnet test --verbosity normal`
- Watch for any test failures or new warnings
- Coverage report: `dotnet test /p:CollectCoverage=true`

**Integration Tests:**
- Focus on database operations (EF Core changes)
- API endpoint tests (ASP.NET Core changes)
- Kubernetes client interactions

**Manual Smoke Tests:**
- Build Docker images and run locally
- Test sync service in debug mode: `--set debug=true`
- Verify API endpoints respond correctly
- Check dashboard connectivity

### Rollback Plan

If critical issues arise during any phase:

- **Phase 1 (Local):** Simple git reset, no harm done
- **Phase 2 (Docker):** Revert Dockerfile changes, rebuild with .NET 9 tags
- **Phase 3 (CI/CD):** Revert workflow changes, existing builds continue working

The phased approach ensures we can stop and roll back cleanly at any gate.

## Documentation & Verification

### Files Requiring Updates

**Documentation:**
- `CLAUDE.md` - Update all references from "uses .NET 9.0" to "uses .NET 10.0"
- `README.md` - Check if it mentions .NET version requirements
- `charts/vaultwarden-kubernetes-secrets/README.md` - Chart documentation (if applicable)

**Project Files:**
- All `.csproj` files (4 total: sync, api, database, tests)
- Both `Dockerfile` files (2 total)
- GitHub Actions workflow (1 file)

### Post-Upgrade Verification Checklist

**✅ Build & Test:**
- [ ] `dotnet build` succeeds with no warnings
- [ ] `dotnet test` passes 100% of tests
- [ ] No new nullable reference warnings
- [ ] Coverage remains at current levels

**✅ Docker:**
- [ ] Sync service image builds successfully
- [ ] API image builds successfully
- [ ] Both images run without crashes
- [ ] `./scripts/test-docker-image.sh` passes

**✅ Helm & Kubernetes:**
- [ ] `./scripts/test-helm-locally.sh` passes
- [ ] Helm chart lints successfully
- [ ] Pods start and reach Ready state
- [ ] Health checks pass

**✅ CI/CD:**
- [ ] All workflow jobs pass (build-and-test, security, infrastructure-validation)
- [ ] Docker images publish to GHCR successfully
- [ ] Helm chart packages and publishes

**✅ Functionality:**
- [ ] Sync service connects to Vaultwarden (dry-run mode acceptable)
- [ ] API endpoints respond correctly
- [ ] Dashboard connects to API
- [ ] Metrics endpoints work (Prometheus)

## Success Criteria

The upgrade is complete when:
1. All automated tests pass in CI
2. Docker images build and deploy successfully
3. Helm chart installs cleanly in test cluster
4. No performance regressions observed
5. CLAUDE.md reflects new .NET version

## Estimated Timeline

- **Phase 1 (Local):** 30-60 minutes - Update files, build, test, fix issues
- **Phase 2 (Docker):** 20-30 minutes - Update Dockerfiles, test builds
- **Phase 3 (CI/CD):** 15-20 minutes - Update workflow, push to test branch, verify

**Total:** ~1.5-2 hours if everything goes smoothly. Add buffer time if VwConnector or other dependencies need attention.

## Files to Modify

### Phase 1 - .csproj files
1. `VaultwardenK8sSync/VaultwardenK8sSync.csproj`
2. `VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj`
3. `VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj`
4. `VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj`

### Phase 2 - Dockerfiles
5. `VaultwardenK8sSync/Dockerfile`
6. `VaultwardenK8sSync.Api/Dockerfile`

### Phase 3 - CI/CD
7. `.github/workflows/docker-publish.yml`

### Documentation
8. `CLAUDE.md`
9. `README.md` (if applicable)

## Next Steps

1. Commit this design document
2. Set up git worktree for isolated development
3. Create detailed implementation plan with specific file changes
4. Execute Phase 1
