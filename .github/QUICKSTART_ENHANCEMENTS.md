# Quick Start - New Enhancements

## What's New?

We've added **Prometheus metrics and health checks** to make this project production-ready!

### 🎯 Key Features Added

1. **Prometheus Metrics** - Track sync performance and operations
2. **Health Checks** - Kubernetes-native liveness, readiness, and startup probes
3. **Metrics Server** - Built-in HTTP server on port 9090

---

## Quick Test

### 1. Build the Project

```bash
cd /home/tonio/Documentos/GitHub/vaultwarden-kubernetes-secrets
dotnet restore
dotnet build
```

### 2. Run with Metrics Enabled

```bash
# Set environment variables
export METRICS__ENABLED=true
export METRICS__PORT=9090

# Run the application (you'll need valid Vaultwarden credentials)
dotnet run --project VaultwardenK8sSync sync
```

### 3. Access Metrics

Open in your browser or use curl:

```bash
# Prometheus metrics
curl http://localhost:9090/metrics

# Health check (detailed)
curl http://localhost:9090/health | jq

# Liveness probe
curl http://localhost:9090/healthz

# Readiness probe
curl http://localhost:9090/readyz

# Startup probe
curl http://localhost:9090/startupz
```

---

## Available Metrics

### Sync Metrics
- `vaultwarden_sync_duration_seconds` - How long syncs take (histogram)
- `vaultwarden_sync_total` - Total number of syncs (counter)
- `vaultwarden_secrets_synced_total` - Secrets created/updated/deleted (counter)
- `vaultwarden_sync_errors_total` - Sync errors by type (counter)

### Operational Metrics
- `vaultwarden_items_watched` - Number of items being watched (gauge)
- `vaultwarden_api_calls_total` - Vaultwarden API calls (counter)
- `vaultwarden_kubernetes_api_calls_total` - Kubernetes API calls (counter)
- `vaultwarden_last_successful_sync_timestamp` - Last successful sync time (gauge)

---

## Health Check Endpoints

### `/healthz` - Liveness Probe
Returns 200 if the process is alive. Use this for Kubernetes liveness probes.

```yaml
livenessProbe:
  httpGet:
    path: /healthz
    port: 9090
  initialDelaySeconds: 10
  periodSeconds: 30
```

### `/readyz` - Readiness Probe
Returns 200 if Vaultwarden and Kubernetes are accessible. Use this for readiness probes.

```yaml
readinessProbe:
  httpGet:
    path: /readyz
    port: 9090
  initialDelaySeconds: 5
  periodSeconds: 10
```

### `/startupz` - Startup Probe
Returns 200 after the first successful sync. Use this for startup probes.

```yaml
startupProbe:
  httpGet:
    path: /startupz
    port: 9090
  initialDelaySeconds: 0
  periodSeconds: 5
  failureThreshold: 30
```

### `/health` - Detailed Health
Returns JSON with detailed health information:

```json
{
  "status": "Healthy",
  "totalDuration": 45.2,
  "checks": [
    {
      "name": "vaultwarden",
      "status": "Healthy",
      "description": "Vaultwarden is accessible. 15 items available.",
      "duration": 123.4
    },
    {
      "name": "kubernetes",
      "status": "Healthy",
      "description": "Kubernetes is accessible. 8 namespaces available.",
      "duration": 89.1
    },
    {
      "name": "sync",
      "status": "Healthy",
      "description": "Last successful sync was 45 seconds ago",
      "duration": 0.5
    }
  ]
}
```

---

## Configuration

### Environment Variables

```bash
# Enable/disable metrics server
METRICS__ENABLED=true

# Change metrics port (default: 9090)
METRICS__PORT=9090
```

### In Helm Chart (Coming Soon)

```yaml
metrics:
  enabled: true
  port: 9090
  serviceMonitor:
    enabled: true
    interval: 30s
```

---

## Integration with Prometheus

### Prometheus Configuration

Add this to your Prometheus `scrape_configs`:

```yaml
scrape_configs:
  - job_name: 'vaultwarden-kubernetes-secrets'
    static_configs:
      - targets: ['vaultwarden-kubernetes-secrets:9090']
    scrape_interval: 30s
```

### ServiceMonitor (Prometheus Operator)

```yaml
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
metadata:
  name: vaultwarden-kubernetes-secrets
  namespace: vaultwarden-kubernetes-secrets
spec:
  selector:
    matchLabels:
      app.kubernetes.io/name: vaultwarden-kubernetes-secrets
  endpoints:
    - port: metrics
      interval: 30s
      path: /metrics
```

---

## Example Prometheus Queries

```promql
# Average sync duration over 5 minutes
rate(vaultwarden_sync_duration_seconds_sum[5m]) / rate(vaultwarden_sync_duration_seconds_count[5m])

# Sync success rate
rate(vaultwarden_sync_total{success="true"}[5m]) / rate(vaultwarden_sync_total[5m])

# Secrets synced per minute
rate(vaultwarden_secrets_synced_total[1m]) * 60

# Time since last successful sync
time() - vaultwarden_last_successful_sync_timestamp

# API call rate
rate(vaultwarden_api_calls_total[5m])
```

---

## Example Grafana Dashboard

### Panels to Create

1. **Sync Duration** (Graph)
   - Query: `rate(vaultwarden_sync_duration_seconds_sum[5m]) / rate(vaultwarden_sync_duration_seconds_count[5m])`
   - Unit: seconds

2. **Sync Success Rate** (Gauge)
   - Query: `rate(vaultwarden_sync_total{success="true"}[5m]) / rate(vaultwarden_sync_total[5m]) * 100`
   - Unit: percent

3. **Items Watched** (Stat)
   - Query: `vaultwarden_items_watched`
   - Unit: none

4. **Secrets Synced** (Graph)
   - Query: `rate(vaultwarden_secrets_synced_total[5m])`
   - Legend: `{{operation}}`

5. **API Calls** (Graph)
   - Query: `rate(vaultwarden_api_calls_total[5m])`
   - Legend: `{{operation}}`

6. **Health Status** (Stat)
   - Query: `up{job="vaultwarden-kubernetes-secrets"}`
   - Thresholds: 0 = red, 1 = green

---

## Troubleshooting

### Metrics server not starting

Check logs for port conflicts:
```bash
dotnet run --project VaultwardenK8sSync sync 2>&1 | grep -i "metrics\|port\|9090"
```

Try a different port:
```bash
export METRICS__PORT=9091
```

### Health checks failing

Check individual health endpoints:
```bash
# Vaultwarden connectivity
curl http://localhost:9090/health | jq '.checks[] | select(.name=="vaultwarden")'

# Kubernetes connectivity
curl http://localhost:9090/health | jq '.checks[] | select(.name=="kubernetes")'

# Sync status
curl http://localhost:9090/health | jq '.checks[] | select(.name=="sync")'
```

### Metrics not updating

Ensure sync is running:
```bash
# Check if syncs are happening
curl http://localhost:9090/metrics | grep vaultwarden_sync_total
```

---

## What's Next?

See [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) for:
- Remaining tasks for Step 1
- Upcoming features (webhooks, rotation, operator pattern)
- Timeline and roadmap

---

## Feedback

Found an issue or have a suggestion? 
- Open an issue: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/issues
- Start a discussion: https://github.com/antoniolago/vaultwarden-kubernetes-secrets/discussions
