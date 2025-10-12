# Pipeline Changes Summary

## Issues Fixed

### 1. ✅ Harbor Login Error on PRs
**Problem:** Harbor registry login was attempted on all builds, including PRs, causing authentication failures.

**Solution:** Made Harbor login conditional - only runs for non-PR events:
```yaml
- name: Log in to Harbor Registry
  if: github.event_name != 'pull_request'
  uses: docker/login-action@v3
```

### 2. ✅ Build Cache Optimization
**Problem:** Build cache was using Harbor registry which isn't accessible during PR builds.

**Solution:** Switched to GitHub Actions cache (type=gha) for all builds:
```yaml
cache-from: type=gha
cache-to: type=gha,mode=max
```

### 3. ✅ GHCR as Primary Registry
**Problem:** Documentation showed Harbor as primary, but GHCR is more accessible publicly.

**Solution:**
- Updated README to use `oci://ghcr.io/antoniolago/charts/vaultwarden-kubernetes-secrets` as primary
- Harbor remains as secondary/alternative option
- Pipeline already pushes to both registries (GHCR for all builds, Harbor for releases only)

### 4. ✅ Helm Chart Testing
**Problem:** No automated testing to verify Helm chart actually works.

**Solution:** Added `test-helm-chart` job that:
- Creates a kind (Kubernetes in Docker) cluster
- Installs the Helm chart with test configuration
- Verifies deployment succeeds
- Checks pod logs
- Runs on PRs and releases

## New Pipeline Flow

### For Pull Requests:
1. **Build & Test** - Unit tests, code quality, security checks
2. **Build Docker Image** - Push to GHCR with PR tag
3. **Test Helm Chart** - Install in kind cluster and verify
4. **Comment on PR** - Post Docker image and test results

### For Releases (tags):
1. **Build & Test** - Same as PRs
2. **Build Docker Images** - Push to both GHCR and Harbor
3. **Package Helm Chart** - Create versioned chart package
4. **Push Helm Charts** - Push to both GHCR and Harbor registries
5. **Test Helm Chart** - Validate the release works

## Registry Strategy

### Docker Images
- **GHCR** (`ghcr.io/antoniolago/vaultwarden-kubernetes-secrets`):
  - All builds (PRs, branches, releases)
  - Public, no authentication needed for pulls
  - Primary for users

- **Harbor** (`harbor.lag0.com.br/library/vaultwarden-kubernetes-secrets`):
  - Releases only (non-PR builds)
  - Requires authentication
  - Alternative/backup registry

### Helm Charts
- **GHCR** (`oci://ghcr.io/antoniolago/charts/vaultwarden-kubernetes-secrets`):
  - Primary, recommended in documentation
  - Public access
  
- **Harbor** (`oci://harbor.lag0.com.br/charts/vaultwarden-kubernetes-secrets`):
  - Secondary option
  - May be faster in some regions

## Testing Improvements

### Automated Helm Chart Validation
The new `test-helm-chart` job provides:
- ✅ Real Kubernetes cluster testing (kind)
- ✅ Helm chart installation verification
- ✅ Deployment health checks
- ✅ Pod log inspection
- ✅ Runs on every PR and release

### What Gets Tested
- Chart template rendering
- Kubernetes resource creation
- RBAC permissions
- Secret mounting
- Container startup
- Configuration validation

## Benefits

1. **No More PR Failures** - Harbor login only when needed
2. **Faster Builds** - GitHub Actions cache instead of registry cache
3. **Better User Experience** - GHCR as primary (public, no auth)
4. **Increased Confidence** - Automated Helm chart testing
5. **Better PR Feedback** - Clear test results in PR comments

## Migration Notes

Users should update their Helm commands from:
```bash
# Old (still works)
helm upgrade -i app oci://harbor.lag0.com.br/charts/vaultwarden-kubernetes-secrets

# New (recommended)
helm upgrade -i app oci://ghcr.io/antoniolago/charts/vaultwarden-kubernetes-secrets
```

Both registries will continue to receive updates, so existing deployments won't break.
