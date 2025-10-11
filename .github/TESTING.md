# Local Testing Guide

This document describes how to test the project locally before pushing to GitHub.

## Testing Methods

### 1. Direct Helm Testing (Recommended for Quick Tests)

Use `test-helm-local.sh` to test the Helm chart directly without act:

```bash
./test-helm-local.sh
```

**What it does:**
- Creates a kind cluster
- Validates the Helm chart with `helm lint`
- Installs the chart in a test namespace
- Verifies the deployment
- Shows pod logs

**Requirements:**
- `kind` - Kubernetes in Docker
- `helm` - Helm package manager
- `kubectl` - Kubernetes CLI

**Cleanup:**
```bash
kubectl delete namespace vaultwarden-test
kind delete cluster --name helm-test
```

---

### 2. GitHub Actions Testing with Act

Use `run-helm-test.sh` to run GitHub Actions workflows locally with act:

```bash
# Run all jobs
./run-helm-test.sh

# Run specific job
./run-helm-test.sh -j test-helm-chart
./run-helm-test.sh -j infrastructure-validation
./run-helm-test.sh -j code-quality
./run-helm-test.sh -j build-and-test

# Show help
./run-helm-test.sh -h
```

**What it does:**
- Runs GitHub Actions workflows locally using act
- Uses the optimized `test-local.yml` workflow
- Skips authentication and external dependencies
- Tests the full CI/CD pipeline

**Requirements:**
- `act` - Local GitHub Actions runner
- Docker or Podman

---

## Workflows

### Production Workflow: `docker-publish.yml`

Used in CI/CD for:
- Building and pushing Docker images
- Publishing Helm charts
- Running full integration tests
- Commenting on PRs

**Triggers:**
- Push to tags (`v*`)
- Pull requests to `main` or `develop`
- Manual workflow dispatch

### Test Workflow: `test-local.yml`

Optimized for local testing with act:
- No authentication required
- No external registry dependencies
- Faster execution
- Same validation logic as production

**Jobs:**
1. **build-and-test** - Builds and runs .NET tests
2. **code-quality** - Checks for TODO/FIXME comments
3. **infrastructure-validation** - Validates Helm chart and Dockerfile
4. **test-helm-chart** - Tests Helm installation in kind cluster

---

## Quick Reference

| Task | Command |
|------|---------|
| Test Helm chart directly | `./test-helm-local.sh` |
| Run all act tests | `./run-helm-test.sh` |
| Run specific act job | `./run-helm-test.sh -j JOB_NAME` |
| Validate Helm chart | `helm lint charts/vaultwarden-k8s-sync` |
| Build .NET project | `dotnet build --configuration Release` |
| Run .NET tests | `dotnet test --configuration Release` |

---

## Troubleshooting

### Act fails with authentication errors

The production workflow (`docker-publish.yml`) requires registry credentials. Use the test workflow instead:

```bash
./run-helm-test.sh -w test-local.yml
```

### Kind cluster already exists

Delete the existing cluster:

```bash
kind delete cluster --name helm-test
```

### Docker/Podman issues with act

Ensure your container runtime is running:

```bash
# For Docker
sudo systemctl start docker

# For Podman
systemctl --user start podman.socket
```

### Helm chart validation fails

Check the chart structure:

```bash
helm lint charts/vaultwarden-k8s-sync --debug
```

---

## Best Practices

1. **Before committing:**
   - Run `./run-helm-test.sh` to validate all checks pass
   - Or run specific jobs: `./run-helm-test.sh -j code-quality`

2. **When modifying Helm chart:**
   - Run `./test-helm-local.sh` for quick validation
   - Then run `./run-helm-test.sh -j test-helm-chart` for full test

3. **When modifying code:**
   - Run `./run-helm-test.sh -j build-and-test`
   - Or use `dotnet test` directly

4. **Before creating a PR:**
   - Run all tests: `./run-helm-test.sh`
   - Ensure no TODO/FIXME comments in production code
