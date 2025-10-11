# Project Enhancements Implementation Guide

This document tracks the implementation of 5 key enhancements to make this project a solid alternative to External Secrets Operator for Vaultwarden.

## Status Overview

- ✅ **Step 1: Prometheus Metrics and Health Checks** - In Progress
- ⏳ **Step 2: Webhook Support** - Pending
- ⏳ **Step 3: Secret Rotation Tracking** - Pending
- ⏳ **Step 4: Comprehensive Documentation** - Pending
- ⏳ **Step 5: Operator Pattern Foundation** - Pending

---

## Step 1: Prometheus Metrics and Health Checks ✅

### What We're Adding

1. **Prometheus Metrics Endpoint** (`/metrics`)
   - Sync duration histogram
   - Total sync operations counter
   - Secrets synced counter
   - Sync errors counter
   - Items watched gauge
   - API call counters (Vaultwarden & Kubernetes)
   - Last successful sync timestamp

2. **Health Check Endpoints**
   - `/healthz` - Liveness probe (process health)
   - `/readyz` - Readiness probe (dependencies available)
   - `/startupz` - Startup probe (initial sync complete)
   - `/health` - Detailed health status

3. **Health Checks**
   - Vaultwarden connectivity check
   - Kubernetes connectivity check
   - Sync recency check

### Files Created

- `VaultwardenK8sSync/Services/IMetricsService.cs` - Metrics service interface
- `VaultwardenK8sSync/Services/MetricsService.cs` - Metrics implementation
- `VaultwardenK8sSync/HealthChecks/VaultwardenHealthCheck.cs` - Vaultwarden health check
- `VaultwardenK8sSync/HealthChecks/KubernetesHealthCheck.cs` - Kubernetes health check
- `VaultwardenK8sSync/HealthChecks/SyncHealthCheck.cs` - Sync health check
- `VaultwardenK8sSync/Infrastructure/MetricsServer.cs` - HTTP server for metrics

### Files Modified

- `VaultwardenK8sSync/VaultwardenK8sSync.csproj` - Added NuGet packages
- `VaultwardenK8sSync/AppSettings.cs` - Added MetricsSettings
- `VaultwardenK8sSync/Configuration/ConfigurationExtensions.cs` - Register metrics settings
- `VaultwardenK8sSync/Application/ApplicationHost.cs` - Register metrics service

### NuGet Packages Added

```xml
<PackageReference Include="Microsoft.AspNetCore.Diagnostics.HealthChecks" Version="9.0.9" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="9.0.9" />
<PackageReference Include="prometheus-net" Version="9.0.0" />
<PackageReference Include="prometheus-net.AspNetCore" Version="9.0.0" />
<PackageReference Include="prometheus-net.AspNetCore.HealthChecks" Version="9.0.0" />
```

### Configuration

New environment variables:
```bash
METRICS__ENABLED=true    # Enable/disable metrics server (default: true)
METRICS__PORT=9090       # Metrics server port (default: 9090)
```

### Next Steps

1. ✅ Restore NuGet packages
2. ⏳ Update ApplicationHost to start metrics server
3. ⏳ Integrate metrics into SyncService
4. ⏳ Update Helm chart with metrics configuration
5. ⏳ Add ServiceMonitor for Prometheus
6. ⏳ Test metrics endpoint

---

## Step 2: Webhook Support (Planned)

### Goals

- Add webhook receiver endpoint
- Validate webhook signatures from Vaultwarden
- Trigger targeted sync for specific items
- Reduce polling overhead

### Implementation Plan

1. Create webhook controller/endpoint
2. Add webhook signature validation
3. Implement selective sync based on webhook payload
4. Add webhook configuration to AppSettings
5. Document webhook setup in Vaultwarden

---

## Step 3: Secret Rotation Tracking (Planned)

### Goals

- Track secret versions with annotations
- Record rotation history
- Enable rollback capability
- Integrate with Reloader for automatic pod restarts

### Implementation Plan

1. Add version tracking to secret annotations
2. Store rotation history
3. Create rollback command
4. Add Reloader integration examples
5. Document rotation workflow

---

## Step 4: Comprehensive Documentation (Planned)

### Goals

- Migration guide from ESO
- Architecture diagrams
- Video tutorials
- Comparison matrix vs ESO
- Common use case examples

### Implementation Plan

1. Create migration guide
2. Add architecture diagrams (Mermaid)
3. Record demo videos
4. Create comparison matrix
5. Add troubleshooting guide
6. Document all metrics and health checks

---

## Step 5: Operator Pattern Foundation (Planned)

### Goals

- Design CRD structure
- Event-driven updates
- Native Kubernetes integration
- Declarative configuration

### Implementation Plan

1. Design VaultwardenSecret CRD
2. Create CRD YAML definitions
3. Implement basic controller pattern
4. Add reconciliation loop
5. Document CRD usage

### CRD Design (Draft)

```yaml
apiVersion: vaultwarden.io/v1alpha1
kind: VaultwardenSecret
metadata:
  name: my-app-secrets
  namespace: production
spec:
  vaultwardenItemId: "item-uuid"
  # OR
  vaultwardenItemName: "Production Database"
  organizationId: "optional"
  refreshInterval: 1h
  target:
    name: db-credentials
    template:
      type: Opaque
      data:
        username: "{{ .username }}"
        password: "{{ .password }}"
```

---

## Testing Checklist

### Step 1: Metrics & Health Checks

- [ ] Build project successfully
- [ ] Metrics server starts on port 9090
- [ ] `/metrics` endpoint returns Prometheus metrics
- [ ] `/healthz` returns healthy
- [ ] `/readyz` checks all dependencies
- [ ] `/startupz` waits for first sync
- [ ] `/health` returns detailed status
- [ ] Metrics update during sync
- [ ] Health checks fail appropriately when services are down

### Integration Testing

- [ ] Deploy to kind cluster
- [ ] Verify metrics in Prometheus
- [ ] Test liveness probe
- [ ] Test readiness probe
- [ ] Test startup probe
- [ ] Verify Grafana dashboard

---

## Rollout Plan

### Phase 1: Core Metrics (Current)
- Implement metrics service
- Add health checks
- Update Helm chart
- Test in development

### Phase 2: Webhook Support
- Implement webhook receiver
- Test with Vaultwarden
- Document setup

### Phase 3: Rotation & Documentation
- Add rotation tracking
- Create comprehensive docs
- Migration guide

### Phase 4: Operator Pattern
- Design CRDs
- Implement controller
- Beta testing

---

## Resources

### Prometheus Metrics Best Practices
- Use histograms for durations
- Use counters for totals
- Use gauges for current state
- Label cardinality should be low

### Health Check Best Practices
- Liveness: Process is alive
- Readiness: Ready to serve traffic
- Startup: Initial setup complete

### Kubernetes Operator Patterns
- Reconciliation loop
- Event-driven updates
- Status conditions
- Finalizers for cleanup
