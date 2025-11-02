# Testing Scripts

This directory contains scripts for testing the Vaultwarden Kubernetes Secrets application locally.

## Prerequisites

### For Docker Testing
- Docker installed and running
- Bash shell

### For Kubernetes/Helm Testing
- [Kind](https://kind.sigs.k8s.io/) - Kubernetes in Docker
- kubectl
- Helm 3
- Docker

## Quick Start

### 1. Test Docker Image Build

```bash
./scripts/test-docker-image.sh
```

This script:
- ✅ Builds the Docker image
- ✅ Runs basic validation tests
- ✅ Provides commands for manual testing

**Expected output:**
```
🐳 Docker Image Test
====================

📦 Building Docker image...
✅ Image built successfully: vaultwarden-kubernetes-secrets:test

🧪 Running basic tests...
  ✓ Checking image metadata...
  ✓ Testing debug mode (container should stay alive)...
    ✅ Container running in debug mode
  ✓ Testing help command...
  ✓ Checking required files in image...

✅ All basic tests passed!
```

### 2. Test Helm Chart in Local Kubernetes

```bash
./scripts/test-helm-locally.sh
```

This script:
- 🔧 Creates a Kind cluster (if needed)
- 🐳 Builds and loads the Docker image
- 🏗️ Creates test namespace and secrets
- 📊 Installs the Helm chart in debug mode
- ✅ Verifies the deployment

**Expected output:**
```
🚀 Local Helm Chart Testing with Kind
======================================

📋 Configuration:
  Cluster: vaultwarden-test
  Namespace: vaultwarden-test
  Image Tag: latest

✅ Helm chart installed successfully!
```

## Environment Variables

### test-helm-locally.sh

- `CLUSTER_NAME` - Kind cluster name (default: `vaultwarden-test`)
- `NAMESPACE` - Kubernetes namespace (default: `vaultwarden-test`)
- `IMAGE_TAG` - Docker image tag (default: `latest`)

**Example:**
```bash
IMAGE_TAG=v1.2.3 CLUSTER_NAME=my-test ./scripts/test-helm-locally.sh
```

### test-docker-image.sh

- `IMAGE_TAG` - Docker image tag (default: `test`)

**Example:**
```bash
IMAGE_TAG=dev ./scripts/test-docker-image.sh
```

## Testing Scenarios

### Scenario 1: Test Image Build Only

```bash
./scripts/test-docker-image.sh
```

Use this when:
- You want to quickly verify Docker build works
- You don't need full Kubernetes testing
- You're debugging Dockerfile issues

### Scenario 2: Test Helm Chart with Debug Mode

```bash
./scripts/test-helm-locally.sh
```

Use this when:
- You want to test Helm chart installation
- You want to verify Kubernetes manifests
- You want to test RBAC and permissions
- Container just needs to start (doesn't need to sync)

### Scenario 3: Test Full Sync Locally

After running `test-helm-locally.sh`, upgrade with real config:

```bash
helm upgrade vaultwarden-test ./charts/vaultwarden-kubernetes-secrets \
  --namespace vaultwarden-test \
  --set image.repository=vaultwarden-kubernetes-secrets \
  --set image.tag=latest \
  --set image.pullPolicy=Never \
  --set debug=false \
  --set env.config.VAULTWARDEN__SERVERURL=https://your-vaultwarden.com \
  --set env.config.SYNC__DRYRUN=true \
  --set env.config.SYNC__CONTINUOUSSYNC=true
```

### Scenario 4: Test in CI Environment

The CI workflow automatically:
1. Builds the Docker image
2. Pushes to GHCR
3. Creates a test cluster
4. Installs Helm chart in debug mode
5. Verifies deployment
6. Checks logs

## Troubleshooting

### Helm Install Timeout

**Problem:**
```
Error: client rate limiter Wait returned an error: context deadline exceeded
```

**Solution:**
- Use `--set debug=true` to keep container alive without syncing
- Check pod status: `kubectl get pods -n vaultwarden-test`
- Check events: `kubectl get events -n vaultwarden-test --sort-by='.lastTimestamp'`
- Check logs: `kubectl logs -n vaultwarden-test -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets`

### Image Pull Errors in Kind

**Problem:**
```
Failed to pull image ... ImagePullBackOff
```

**Solution:**
```bash
# Rebuild and reload image
docker build -f VaultwardenK8sSync/Dockerfile -t vaultwarden-kubernetes-secrets:latest .
kind load docker-image vaultwarden-kubernetes-secrets:latest --name vaultwarden-test

# Use imagePullPolicy: Never
helm upgrade vaultwarden-test ./charts/vaultwarden-kubernetes-secrets \
  --set image.pullPolicy=Never \
  ...
```

### Container Exits Immediately

**Problem:**
Container starts then exits with CrashLoopBackOff

**Causes:**
1. **One-shot mode without continuous sync**: Set `SYNC__CONTINUOUSSYNC=true`
2. **Missing credentials**: Check secret is created correctly
3. **Invalid server URL**: Use a reachable Vaultwarden server or use debug mode

**Debug:**
```bash
# Check why it crashed
kubectl logs -n vaultwarden-test <pod-name> --previous

# Use debug mode to keep it alive
helm upgrade vaultwarden-test ./charts/vaultwarden-kubernetes-secrets \
  --set debug=true \
  ...
```

## Cleanup

### Clean up Kind cluster
```bash
kind delete cluster --name vaultwarden-test
```

### Clean up Docker images
```bash
docker rmi vaultwarden-kubernetes-secrets:test
docker rmi vaultwarden-kubernetes-secrets:latest
```

### Clean up Helm release
```bash
helm uninstall vaultwarden-test -n vaultwarden-test
kubectl delete namespace vaultwarden-test
```

## Advanced Usage

### Custom Kind Configuration

Create `kind-config.yaml`:
```yaml
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
nodes:
- role: control-plane
  extraPortMappings:
  - containerPort: 30080
    hostPort: 8080
  - containerPort: 30443
    hostPort: 8443
```

Then:
```bash
kind create cluster --name vaultwarden-test --config kind-config.yaml
```

### Test with Minikube

```bash
# Start minikube
minikube start

# Build and load image
eval $(minikube docker-env)
docker build -f VaultwardenK8sSync/Dockerfile -t vaultwarden-kubernetes-secrets:latest .

# Install chart
helm upgrade --install vaultwarden-test ./charts/vaultwarden-kubernetes-secrets \
  --set image.repository=vaultwarden-kubernetes-secrets \
  --set image.tag=latest \
  --set image.pullPolicy=Never \
  --set debug=true
```

## CI Integration

These scripts are designed to work both locally and in CI. The CI workflow uses similar commands:

```yaml
- name: Build Docker image
  run: docker build -f VaultwardenK8sSync/Dockerfile -t test-image .

- name: Load into Kind
  run: kind load docker-image test-image

- name: Install Helm chart
  run: |
    helm upgrade -i test ./charts/vaultwarden-kubernetes-secrets \
      --set image.repository=test-image \
      --set debug=true \
      --wait --timeout 2m
```

## Support

For issues or questions:
- Check the logs: `kubectl logs -n vaultwarden-test <pod-name>`
- Check the events: `kubectl get events -n vaultwarden-test`
- Open an issue on GitHub with the output from both commands
