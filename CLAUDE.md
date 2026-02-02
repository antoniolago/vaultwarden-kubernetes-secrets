# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Vaultwarden Kubernetes Secrets is a service that automatically synchronizes secrets from Vaultwarden (a Bitwarden-compatible server) to Kubernetes Secrets. It consists of four main components:

1. **VaultwardenK8sSync** - Main sync service (console app)
2. **VaultwardenK8sSync.Api** - REST API for monitoring and control
3. **VaultwardenK8sSync.Database** - SQLite database for state tracking
4. **dashboard** - React/TypeScript frontend for monitoring

## Architecture

### C# Projects (.NET 10.0)

The main sync service follows a layered architecture:

- **Application/** - Application host and command handlers
- **Infrastructure/** - Cross-cutting concerns (ProcessLock, ProcessRunner, MetricsServer, GlobalSyncLock)
- **Services/** - Core business logic (SyncService, VaultwardenService, KubernetesService, WebhookService, MetricsService)
- **Models/** - Domain entities (VaultwardenItem, WebhookEvent, SyncSummary)
- **Configuration/** - Settings and constants
- **HealthChecks/** - Health check implementations
- **Policies/** - Resilience policies (Polly)

Key architectural patterns:
- Uses bw-cli (Bitwarden CLI) as an external process to interact with Vaultwarden
- Enforces single-instance operation via ProcessLock to prevent concurrent syncs
- Uses GlobalSyncLock for coordination between API and sync service via Redis/Valkey
- VwConnector package handles Bitwarden CLI interactions

### Dashboard (React + TypeScript)

Built with:
- **Package Manager**: Bun (preferred) or npm
- **Build Tool**: Vite
- **UI**: MUI Joy components
- **State/Data**: TanStack Query (React Query)
- **Routing**: React Router v6

## Development Commands

### C# Backend

```bash
# Build the solution
dotnet build VaultwardenK8sSync.sln

# Run tests
dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Run the sync service (requires configuration)
dotnet run --project VaultwardenK8sSync/VaultwardenK8sSync.csproj

# Run the API
dotnet run --project VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj
```

### Dashboard

```bash
# Install dependencies (preferred)
cd dashboard && bun install

# Alternative with npm
cd dashboard && npm install

# Development server
bun run dev  # or npm run dev

# Build for production
bun run build  # or npm run build

# Preview production build
bun run preview

# Run linter
bun run lint

# E2E tests
bun run test:e2e
bun run test:e2e:ui  # Interactive mode
```

### Docker

```bash
# Build sync service image
docker build -f VaultwardenK8sSync/Dockerfile -t vaultwarden-kubernetes-secrets:latest .

# Build API image
docker build -f VaultwardenK8sSync.Api/Dockerfile -t vaultwarden-k8s-api:latest .

# Build dashboard image
docker build -f dashboard/Dockerfile -t vaultwarden-k8s-dashboard:latest .
```

### Helm Testing

```bash
# Quick image test
./scripts/test-docker-image.sh

# Full Helm chart test in Kind cluster
./scripts/test-helm-locally.sh

# Package chart
helm package charts/vaultwarden-kubernetes-secrets

# Install chart
helm upgrade -i vaultwarden-kubernetes-secrets \
  oci://ghcr.io/antoniolago/charts/vaultwarden-kubernetes-secrets \
  --namespace vaultwarden-kubernetes-secrets \
  --create-namespace
```

## Configuration

The sync service is configured via environment variables (supports .env files):

### Required
- `VAULTWARDEN__SERVERURL` - Vaultwarden server URL
- `BW_CLIENTID` - API client ID
- `BW_CLIENTSECRET` - API client secret
- `VAULTWARDEN__MASTERPASSWORD` - Master password

### Optional Filters
- `VAULTWARDEN__ORGANIZATIONID` - Filter to specific organization
- `VAULTWARDEN__COLLECTIONID` - Filter to specific collection
- `VAULTWARDEN__FOLDERID` - Filter to specific folder

### Sync Behavior
- `SYNC__SYNCINTERVALSECONDS` - Interval between syncs (default: 300)
- `SYNC__CONTINUOUSSYNC` - Keep syncing in loop (default: true)
- `SYNC__DRYRUN` - Test mode without creating secrets (default: false)

## Testing Strategy

- **Unit Tests**: Use xUnit, Moq, FluentAssertions, AutoFixture
- **Integration Tests**: In VaultwardenK8sSync.Tests/Integration/
- **Security Tests**: In VaultwardenK8sSync.Tests/Security/
- **E2E Tests**: Playwright tests in dashboard/e2e/
- **Coverage**: Configured via Coverlet (opencover format)

Run a single test file:
```bash
dotnet test --filter "FullyQualifiedName~VaultwardenK8sSync.Tests.SomeTestClass"
```

## Key Dependencies

### C# Backend
- **KubernetesClient** - K8s API interaction
- **VwConnector** - Bitwarden CLI wrapper
- **prometheus-net** - Metrics collection
- **StackExchange.Redis** - Redis/Valkey for sync coordination
- **Microsoft.EntityFrameworkCore.Sqlite** - Database for state tracking
- **Polly** - Resilience and retry policies
- **Spectre.Console** - Rich console output

### Dashboard
- **@mui/joy** - UI component library
- **@tanstack/react-query** - Data fetching and caching
- **recharts** - Chart visualization
- **lucide-react** - Icons
- **date-fns** - Date utilities

## Important Implementation Details

1. **Single Instance Enforcement**: The sync service uses a ProcessLock to ensure only one instance runs at a time. This is critical to prevent race conditions.

2. **bw-cli Integration**: The service spawns bw-cli as an external process. All Bitwarden operations go through VwConnector which manages process lifecycle.

3. **Secret Naming**: Vaultwarden item names are sanitized to comply with Kubernetes DNS subdomain requirements (lowercase alphanumeric plus dashes).

4. **Multi-namespace Support**: A single Vaultwarden item can create secrets in multiple namespaces via the `namespaces` custom field (comma-separated).

5. **Secret Merging**: Multiple Vaultwarden items with the same `secret-name` custom field will merge into a single Kubernetes Secret.

6. **Orphan Cleanup**: Secrets previously created but no longer in Vaultwarden are automatically removed.

7. **Custom Fields**:
   - Reserved fields: `namespaces`, `secret-name`, `secret-key-password`, `secret-key`, `secret-key-username`, `secret-annotation`, `secret-label`, `ignore-field`
   - All other custom fields become Secret data entries

8. **Dashboard Authentication**: Uses token-based auth. Token is stored in a Kubernetes Secret created during Helm installation.

## Helm Chart

Located in `charts/vaultwarden-kubernetes-secrets/`. Key values:
- `env.config.*` - Sync service configuration
- `dashboard.enabled` - Enable/disable dashboard
- `api.enabled` - Enable/disable API
- `debug` - Keep container alive without syncing (for testing)

## Common Development Workflows

### Adding a new custom field type
1. Update Constants.cs with the new reserved field name
2. Modify KubernetesService.cs secret creation logic
3. Add tests in VaultwardenK8sSync.Tests
4. Update README.md examples

### Adding a new API endpoint
1. Add controller method in VaultwardenK8sSync.Api/Controllers/
2. Update dashboard API client in dashboard/src/lib/api.ts
3. Add React Query hook if needed
4. Update Swagger documentation

### Modifying sync logic
1. Update SyncService.cs
2. Ensure GlobalSyncLock is properly used for coordination
3. Update metrics in MetricsService.cs
4. Add/update tests
5. Test with debug mode in Kind cluster

## Notes

- The project uses .NET 10.0 - ensure SDK is installed
- Dashboard prefers Bun but works with npm/node
- Helm charts are published to GHCR (GitHub Container Registry)
- CI/CD workflows are in .github/workflows/
- Security: Never commit actual Vaultwarden credentials or API tokens
