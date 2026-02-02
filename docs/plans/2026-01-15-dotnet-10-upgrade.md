# .NET 10 Upgrade Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Upgrade Vaultwarden Kubernetes Secrets from .NET 9.0 to .NET 10.0 for performance improvements and continued support.

**Architecture:** Phased rollout with validation gates - Phase 1 updates local .csproj files and packages, Phase 2 updates Docker base images, Phase 3 updates CI/CD pipeline. Each phase validates before proceeding.

**Tech Stack:** .NET 10.0, C# 14, Entity Framework Core 10, ASP.NET Core 10, Docker, GitHub Actions

---

## PHASE 1: Local Development Environment

### Task 1: Update VaultwardenK8sSync.Database Project

**Files:**
- Modify: `VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj:4`
- Modify: `VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj:10-12`

**Step 1: Update target framework**

Edit line 4 in `VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj`:

```xml
<TargetFramework>net10.0</TargetFramework>
```

**Step 2: Update Microsoft.EntityFrameworkCore packages**

Edit lines 10-12 in `VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
```

**Step 3: Verify changes compile**

Run: `dotnet build VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj`

Expected: Build succeeds or shows clear error messages

**Step 4: Commit Database project update**

```bash
git add VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj
git commit -m "chore: upgrade Database project to .NET 10"
```

---

### Task 2: Update VaultwardenK8sSync Main Project

**Files:**
- Modify: `VaultwardenK8sSync/VaultwardenK8sSync.csproj:5`
- Modify: `VaultwardenK8sSync/VaultwardenK8sSync.csproj:13-22`

**Step 1: Update target framework**

Edit line 5 in `VaultwardenK8sSync/VaultwardenK8sSync.csproj`:

```xml
<TargetFramework>net10.0</TargetFramework>
```

**Step 2: Update Microsoft.EntityFrameworkCore.Sqlite**

Edit line 13 in `VaultwardenK8sSync/VaultwardenK8sSync.csproj`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
```

**Step 3: Update Microsoft.Extensions packages**

Edit lines 14-22 in `VaultwardenK8sSync/VaultwardenK8sSync.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.CommandLine" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Http.Polly" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Debug" Version="10.0.0" />
```

**Step 4: Verify changes compile**

Run: `dotnet build VaultwardenK8sSync/VaultwardenK8sSync.csproj`

Expected: Build succeeds (VwConnector and other third-party packages should remain compatible)

**Step 5: Commit main project update**

```bash
git add VaultwardenK8sSync/VaultwardenK8sSync.csproj
git commit -m "chore: upgrade main sync project to .NET 10"
```

---

### Task 3: Update VaultwardenK8sSync.Api Project

**Files:**
- Modify: `VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj:4`
- Modify: `VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj:10-15`

**Step 1: Update target framework**

Edit line 4 in `VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj`:

```xml
<TargetFramework>net10.0</TargetFramework>
```

**Step 2: Update Microsoft ASP.NET and EF packages**

Edit lines 10-15 in `VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj`:

```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.0" />
```

**Step 3: Verify changes compile**

Run: `dotnet build VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj`

Expected: Build succeeds

**Step 4: Commit API project update**

```bash
git add VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj
git commit -m "chore: upgrade API project to .NET 10"
```

---

### Task 4: Update VaultwardenK8sSync.Tests Project

**Files:**
- Modify: `VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj:4`
- Modify: `VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj:19-20`

**Step 1: Update target framework**

Edit line 4 in `VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj`:

```xml
<TargetFramework>net10.0</TargetFramework>
```

**Step 2: Update Microsoft test packages**

Edit lines 19-20 in `VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj`:

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
```

**Step 3: Verify changes compile**

Run: `dotnet build VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj`

Expected: Build succeeds

**Step 4: Commit test project update**

```bash
git add VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj
git commit -m "chore: upgrade test project to .NET 10"
```

---

### Task 5: Validate Phase 1 - Build and Test

**Files:**
- None (validation only)

**Step 1: Clean and restore all projects**

Run: `dotnet clean && dotnet restore`

Expected: All packages restore successfully, no errors

**Step 2: Build entire solution**

Run: `dotnet build VaultwardenK8sSync.sln --configuration Release`

Expected: Build succeeds with 0 errors. Warnings are acceptable but note them for review.

**Step 3: Run full test suite**

Run: `dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj --verbosity normal`

Expected: All tests pass. Note any skipped or failed tests.

**Step 4: Run tests with coverage**

Run: `dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj /p:CollectCoverage=true`

Expected: Tests pass and coverage report generates successfully

**Step 5: Document Phase 1 completion**

Create a note of:
- Build warnings (if any)
- Test results (passed/failed/skipped counts)
- Any VwConnector compatibility issues observed

---

## PHASE 2: Docker Images

### Task 6: Update Sync Service Dockerfile

**Files:**
- Modify: `VaultwardenK8sSync/Dockerfile:2`
- Modify: `VaultwardenK8sSync/Dockerfile:21`

**Step 1: Update SDK base image**

Edit line 2 in `VaultwardenK8sSync/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
```

**Step 2: Update runtime base image**

Edit line 21 in `VaultwardenK8sSync/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
```

**Step 3: Build Docker image locally**

Run: `docker build -f VaultwardenK8sSync/Dockerfile -t vaultwarden-kubernetes-secrets:net10-test .`

Expected: Image builds successfully without errors

**Step 4: Test container starts**

Run: `docker run --rm -e DEBUG=true vaultwarden-kubernetes-secrets:net10-test --help`

Expected: Container starts and shows help output

**Step 5: Commit Sync Dockerfile update**

```bash
git add VaultwardenK8sSync/Dockerfile
git commit -m "chore: upgrade sync service Dockerfile to .NET 10"
```

---

### Task 7: Update API Dockerfile

**Files:**
- Modify: `VaultwardenK8sSync.Api/Dockerfile:1`
- Modify: `VaultwardenK8sSync.Api/Dockerfile:5`

**Step 1: Update runtime base image**

Edit line 1 in `VaultwardenK8sSync.Api/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
```

**Step 2: Update SDK base image**

Edit line 5 in `VaultwardenK8sSync.Api/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
```

**Step 3: Build Docker image locally**

Run: `docker build -f VaultwardenK8sSync.Api/Dockerfile -t vaultwarden-k8s-api:net10-test .`

Expected: Image builds successfully without errors

**Step 4: Test container starts**

Run: `docker run --rm -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 vaultwarden-k8s-api:net10-test &`
Then: `curl http://localhost:8080/health || echo "Container starting..."`
Then: `docker stop $(docker ps -q --filter ancestor=vaultwarden-k8s-api:net10-test)`

Expected: Container starts successfully (health endpoint may not work without full config, but no crashes)

**Step 5: Commit API Dockerfile update**

```bash
git add VaultwardenK8sSync.Api/Dockerfile
git commit -m "chore: upgrade API Dockerfile to .NET 10"
```

---

### Task 8: Validate Phase 2 - Docker Testing

**Files:**
- None (validation only)

**Step 1: Run Docker image test script**

Run: `./scripts/test-docker-image.sh`

Expected: Script passes validation checks

**Step 2: Clean up test images**

Run:
```bash
docker rmi vaultwarden-kubernetes-secrets:net10-test || true
docker rmi vaultwarden-k8s-api:net10-test || true
```

Expected: Images removed successfully

**Step 3: Document Phase 2 completion**

Note:
- Docker build times (compare to previous builds if known)
- Any warnings during build
- Container startup behavior

---

## PHASE 3: CI/CD Pipeline

### Task 9: Update GitHub Actions Workflow

**Files:**
- Modify: `.github/workflows/docker-publish.yml:25`
- Modify: `.github/workflows/docker-publish.yml:99`

**Step 1: Update .NET version in build-and-test job**

Edit line 25 in `.github/workflows/docker-publish.yml`:

```yaml
        dotnet-version: '10.0.x'
```

**Step 2: Update .NET version in security job**

Edit line 99 in `.github/workflows/docker-publish.yml`:

```yaml
        dotnet-version: '10.0.x'
```

**Step 3: Commit workflow update**

```bash
git add .github/workflows/docker-publish.yml
git commit -m "ci: upgrade GitHub Actions workflow to .NET 10"
```

**Step 4: Push to remote branch**

Run: `git push origin feat/upgrading-dotnet-10`

Expected: Push succeeds, workflow triggers

**Step 5: Monitor CI pipeline**

Action: Go to GitHub Actions tab and watch the workflow run

Expected: All jobs pass (build-and-test, code-quality, infrastructure-validation, security)

---

## PHASE 4: Documentation Updates

### Task 10: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Update .NET version references**

Find and replace in `CLAUDE.md`:
- All instances of `.NET 9.0` → `.NET 10.0`
- All instances of `net9.0` → `net10.0`
- All instances of `9.0.x` → `10.0.x`

**Step 2: Verify documentation accuracy**

Read: `CLAUDE.md`

Expected: All version references are correct and consistent

**Step 3: Commit documentation update**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for .NET 10"
```

---

### Task 11: Check and Update README if Needed

**Files:**
- Read: `README.md`
- Modify: `README.md` (if applicable)

**Step 1: Check README for .NET version mentions**

Run: `grep -i "\.net 9\|net9" README.md || echo "No .NET version references found"`

Expected: Either no matches or specific lines to update

**Step 2: Update README if needed**

If matches found, update them to reference .NET 10 instead of .NET 9.

**Step 3: Commit README update (if changed)**

```bash
git add README.md
git commit -m "docs: update README for .NET 10"
```

---

## PHASE 5: Final Validation

### Task 12: Complete Post-Upgrade Verification

**Files:**
- None (validation only)

**Step 1: Run full build with no warnings**

Run: `dotnet build VaultwardenK8sSync.sln --configuration Release /warnaserror`

Expected: Build succeeds with 0 warnings and 0 errors

**Step 2: Run complete test suite**

Run: `dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj --verbosity detailed`

Expected: 100% of tests pass

**Step 3: Verify Docker builds**

Run:
```bash
docker build -f VaultwardenK8sSync/Dockerfile -t vaultwarden-kubernetes-secrets:latest .
docker build -f VaultwardenK8sSync.Api/Dockerfile -t vaultwarden-k8s-api:latest .
```

Expected: Both images build successfully

**Step 4: Run Helm test script (optional but recommended)**

Run: `./scripts/test-helm-locally.sh`

Expected: Helm chart installs successfully in Kind cluster

**Step 5: Review CI pipeline results**

Action: Check GitHub Actions for the pushed branch

Expected: All workflow jobs are green

---

### Task 13: Final Commit and Summary

**Files:**
- None

**Step 1: Check git status**

Run: `git status`

Expected: Working tree clean (all changes committed)

**Step 2: Review commit history**

Run: `git log --oneline -15`

Expected: See all upgrade commits in order

**Step 3: Create summary of changes**

Document:
- Total commits: ~10-12
- Files changed: 9 total (.csproj files, Dockerfiles, workflow, docs)
- Package updates: 15+ Microsoft packages
- Validation results: All tests passing, Docker builds successful

**Step 4: Mark implementation complete**

Update design document status or create completion note

---

## Troubleshooting Steps

### If VwConnector Fails

**Issue:** Build errors related to VwConnector package

**Resolution:**
1. Check error message details
2. Search for VwConnector .NET 10 compatibility on GitHub
3. Try multi-targeting: Add `<TargetFrameworks>net9.0;net10.0</TargetFrameworks>` temporarily
4. Open issue with VwConnector maintainer if needed

### If Tests Fail

**Issue:** Tests that passed on .NET 9 fail on .NET 10

**Resolution:**
1. Read error messages carefully - look for EF Core query translation changes
2. Check for nullable reference type issues
3. Review ASP.NET Core breaking changes documentation
4. Fix tests or implementation as needed
5. Commit fixes separately with descriptive messages

### If Docker Build Fails

**Issue:** Docker image build errors

**Resolution:**
1. Ensure local `dotnet build` works first
2. Check Docker base image availability: `docker pull mcr.microsoft.com/dotnet/sdk:10.0`
3. Review build context - may need to clean bin/obj folders
4. Check Dockerfile syntax if error is before dotnet commands

### If CI Pipeline Fails

**Issue:** GitHub Actions workflow fails

**Resolution:**
1. Check specific job that failed
2. Review logs for error messages
3. Ensure local builds work before pushing
4. Verify workflow YAML syntax is correct
5. Re-run failed jobs if transient issue

---

## Success Criteria Checklist

- [x] All `.csproj` files target `net10.0`
- [x] All Microsoft packages updated to `10.0.x`
- [x] Solution builds with no errors
- [x] All tests pass locally
- [x] Both Dockerfiles use `sdk:10.0` and `aspnet:10.0`
- [x] Docker images build successfully
- [x] GitHub Actions workflow uses `10.0.x`
- [x] CI pipeline passes all jobs
- [x] CLAUDE.md updated
- [x] README.md checked and updated if needed
- [x] No performance regressions observed

---

## Execution Notes

**Estimated Time:** 1.5-2 hours total

**Prerequisites:**
- .NET 10 SDK installed locally
- Docker Desktop running
- Git branch: `feat/upgrading-dotnet-10` (already created)

**Rollback Strategy:** Each phase is independently committable. If issues arise, revert commits from most recent backward.

**Related Skills:**
- @superpowers:verification-before-completion - Use before marking any phase complete
- @superpowers:systematic-debugging - Use if unexpected failures occur
