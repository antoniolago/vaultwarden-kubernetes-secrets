# E2E Test Suite for Vaultwarden Kubernetes Secrets

This directory contains a comprehensive end-to-end test suite written in **C#** that:

1. **Creates a Kubernetes cluster** (kind) inside Docker
2. **Deploys Vaultwarden** as the password manager
3. **Creates a test user** with API credentials using Bitwarden CLI
4. **Deploys the vaultwarden-kubernetes-secrets operator**
5. **Creates test vault items** with various configurations
6. **Verifies secrets are correctly synced** to Kubernetes

## Quick Start

### Run All Tests (Recommended)

```bash
# From the project root
dotnet test VaultwardenK8sSync.E2ETests -c Release --logger "console;verbosity=detailed"

# Or use the shell script
./tests/e2e/run-e2e-tests.sh
```

### Run in Docker (CI-ready)

```bash
# Build and run the test container
docker build -t vks-e2e-tests -f tests/e2e/Dockerfile .
docker run --privileged -v /var/run/docker.sock:/var/run/docker.sock vks-e2e-tests
```

## Test Structure

```
VaultwardenK8sSync.E2ETests/
├── VaultwardenK8sSync.E2ETests.csproj  # Test project
├── Infrastructure/
│   ├── E2ETestFixture.cs               # Test fixture (cluster setup, teardown)
│   ├── E2ETestCollection.cs            # xUnit collection definition
│   └── TestResultReporter.cs           # JSON result reporter
└── Tests/
    └── SecretSyncTests.cs              # All sync verification tests

tests/e2e/
├── Dockerfile                          # Docker test environment
├── run-e2e-tests.sh                    # Shell script runner
├── manifests/
│   └── vaultwarden.yaml                # Vaultwarden k8s manifests
└── README.md                           # This file
```

## Test Cases

All tests are in `VaultwardenK8sSync.E2ETests/Tests/SecretSyncTests.cs`:

| Test | Description |
|------|-------------|
| `BasicLoginItem_ShouldCreateSecret` | Basic login item sync |
| `CustomSecretName_ShouldBeRespected` | `secret-name` custom field |
| `MultiNamespace_ShouldCreateSecretsInAllNamespaces` | `namespaces` field |
| `CustomKeyNames_ShouldBeUsed` | `secret-key-username/password` |
| `ExtraCustomFields_ShouldBeIncluded` | Additional custom fields |
| `AnnotationsAndLabels_ShouldBeApplied` | `secret-annotation/label-*` |
| `SecureNote_ShouldSyncWithCustomFields` | Secure note items |
| `SecretMerging_ShouldCombineMultipleItems` | Multiple items → one secret |
| `HiddenField_ShouldBeSynced` | Hidden field type |
| `ManagedByLabel_ShouldBePresent` | Managed-by label check |
| `ExpectedSecretCount_ShouldBeCorrect` | Total secret count |

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `E2E_KEEP_CLUSTER` | Don't delete cluster after tests | `false` |

## Requirements

- **Docker** with privileged mode support
- **.NET 10 SDK**
- **kind** (Kubernetes in Docker)
- **kubectl**
- **helm**
- **bw** (Bitwarden CLI)
- ~4GB RAM available
- ~10GB disk space

## How It Works

1. **E2ETestFixture** (xUnit IAsyncLifetime) sets up the environment:
   - Creates kind cluster with port mappings
   - Deploys Vaultwarden from `manifests/vaultwarden.yaml`
   - Uses `bw` CLI to create test user and vault items
   - Builds and loads operator Docker image
   - Deploys operator via Helm chart

2. **Tests** use KubernetesClient to verify secrets are created correctly

3. **Cleanup** deletes the kind cluster (unless `E2E_KEEP_CLUSTER=true`)

## Troubleshooting

### Tests fail with "cluster not ready"
```bash
# Check cluster status
kubectl cluster-info --context kind-vks-e2e
```

### Vaultwarden API errors
```bash
kubectl logs -n vaultwarden -l app=vaultwarden
```

### Operator sync failures
```bash
kubectl logs -n vaultwarden-kubernetes-secrets -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets
```

### Keep cluster for debugging
```bash
E2E_KEEP_CLUSTER=true dotnet test VaultwardenK8sSync.E2ETests
```
