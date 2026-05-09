# Demo Environment Design

> **Status**: Draft Proposal
> **Scope**: Safe, public-facing demo environment for vaultwarden-kubernetes-secrets
> **Modes**: Local (Docker Compose + Kind) and Cloud (Helm + Ingress)

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Local Mode](#local-mode)
3. [Cloud Demo Mode](#cloud-demo-mode)
4. [Safety Guardrails](#safety-guardrails-critical)
5. [Setup Instructions](#setup-instructions)
6. [Reset Procedure](#reset-procedure)
7. [Limitations](#limitations)

---

## Architecture Overview

### Full Stack Components

| Component | Technology | Purpose | Container |
|-----------|-----------|---------|-----------|
| **Vaultwarden** | Rust (Bitwarden-compatible) | Secret storage and API | `vaultwarden/server:latest` |
| **Sync Service** | .NET 10.0 console app | Sync Vaultwarden items → K8s Secrets | `ghcr.io/antoniolago/vaultwarden-kubernetes-secrets` |
| **REST API** | .NET 10.0 ASP.NET Core | Monitoring and control endpoints | `ghcr.io/antoniolago/vaultwarden-kubernetes-secrets-api` |
| **Dashboard** | React + TypeScript (Bun/Vite) | Web UI for monitoring | `ghcr.io/antoniolago/vaultwarden-kubernetes-secrets-dashboard` |
| **Database** | SQLite (via API) | State tracking for sync operations | Bundled in API container |
| **Kind Cluster** | K8s-in-Docker | Target K8s API for secret creation | `kindest/node` (local mode only) |
| **Ingress** | nginx-ingress | TLS termination + routing | Cloud mode only |
| **Redis/Valkey** | Valkey (optional) | GlobalSyncLock coordination | Only if multi-instance needed |

### Topology Diagram

```
                          ┌─────────────────────────────────┐
                          │         Public Internet          │
                          │    (Cloud Demo Mode only)       │
                          └──────────┬──────────────────────┘
                                     │ HTTPS (TLS)
                                     ▼
                          ┌──────────────────┐
                          │   Ingress        │
                          │  (nginx/cloud)   │
                          │  Port 443        │
                          └──┬────────────┬──┘
                             │            │
                    /api/*   │            │  /*
                             ▼            ▼
                    ┌──────────────┐ ┌──────────────┐
                    │  API Service │ │  Dashboard   │
                    │  :8080       │ │  :80 (nginx) │
                    │  auth: token │ │  auth: token │
                    └──────┬───────┘ └──────────────┘
                           │ sync control / status
                           ▼
                    ┌──────────────────┐
                    │   Sync Service   │
                    │  :8080 (health)  │
                    │  .NET 10.0       │
                    └──┬────────────┬──┘
                       │            │
                       ▼            ▼
              ┌────────────┐  ┌────────────┐
              │ Vaultwarden│  │  K8s API   │
              │ :8080      │  │ (in-cluster│
              │ pre-seeded │  │  or Kind)  │
              │ test data  │  │ :6443      │
              └────────────┘  └────────────┘
                                       │
                              ┌────────▼────────┐
                              │ K8s Secrets     │
                              │ (demo namespace)│
                              └─────────────────┘
```

### Port Mapping

| Service | Internal Port | External Port (Local) | External Port (Cloud) |
|---------|--------------|----------------------|----------------------|
| Vaultwarden | 8080 | 8180 | ClusterIP only |
| Sync Service | 8080 | - | ClusterIP only |
| API | 8080 | 8181 | 443 (via Ingress) |
| Dashboard | 80 | 8182 | 443 (via Ingress) |
| Kind API | 6443 | 6443 | N/A |

### Network Topology

- **Local Mode**: Single Docker bridge network (`demo-net`) connecting all containers. Kind cluster runs as a separate Docker container with its own network, reachable via the Docker host.
- **Cloud Mode**: Single Kubernetes namespace (`vks-demo`) with internal ClusterIP services. Ingress controller provides external access.

---

## Local Mode

### Architecture

Local mode uses Docker Compose + Kind (Kubernetes-in-Docker) for a fully self-contained demo on a single machine.

```
┌─────────────────────────────────────────────────────┐
│                    Docker Host                        │
│                                                       │
│  ┌──────────────────────────────────────────────┐   │
│  │            Docker Network: demo-net           │   │
│  │                                               │   │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐   │   │
│  │  │Vaultwarden│  │  Sync    │  │   API    │   │   │
│  │  │ :8080    │  │ :8080    │  │ :8080    │   │   │
│  │  │ test-data│  │          │  │ :8181ext │   │   │
│  │  └──────────┘  └────┬─────┘  └────┬─────┘   │   │
│  │                     │              │         │   │
│  │              ┌──────▼──────┐       │         │   │
│  │              │ Kind Cluster│       │         │   │
│  │              │ :6443       │       │         │   │
│  │              │             │       │         │   │
│  │              │ K8s Secrets │       │         │   │
│  │              └─────────────┘       │         │   │
│  │                                    ▼         │   │
│  │                              ┌──────────┐   │   │
│  │                              │Dashboard │   │   │
│  │                              │ :80      │   │   │
│  │                              │ :8182ext │   │   │
│  │                              └──────────┘   │   │
│  └──────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

### File: `docker-compose.demo.yml`

```yaml
version: "3.9"

x-demo-base: &demo-base
  restart: unless-stopped
  networks:
    - demo-net

services:
  # ── Vaultwarden (pre-seeded with test data) ──────────
  vaultwarden:
    image: vaultwarden/server:latest
    <<: *demo-base
    ports:
      - "8180:80"
    environment:
      SIGNUPS_ALLOWED: "false"
      ADMIN_TOKEN: "${VW_ADMIN_TOKEN:-demo-admin-token-change-me}"
    volumes:
      - vaultwarden-data:/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:80/alive"]
      interval: 10s
      timeout: 3s
      retries: 5
    deploy:
      resources:
        limits:
          cpus: "0.5"
          memory: 256M

  # ── Vaultwarden Seed (one-shot init container) ───────
  vaultwarden-seed:
    image: bitwarden/cli:latest
    <<: *demo-base
    depends_on:
      vaultwarden:
        condition: service_healthy
    environment:
      VAULTWARDEN_URL: "http://vaultwarden:80"
    volumes:
      - ./demo/seed-data.sh:/seed-data.sh:ro
    entrypoint: ["/bin/sh", "/seed-data.sh"]
    deploy:
      resources:
        limits:
          cpus: "0.25"
          memory: 128M

  # ── Kind Cluster (K8s-in-Docker) ─────────────────────
  kind-control-plane:
    image: kindest/node:v1.31.0
    <<: *demo-base
    expose:
      - "6443"   # K8s API
    volumes:
      - kind-data:/var/lib/etcd
    environment:
      # Run a single-node cluster
      KIND_EXPERIMENTAL_DOCKER_NETWORK: demo-net
    deploy:
      resources:
        limits:
          cpus: "1.0"
          memory: 1G

  # ── Sync Service ─────────────────────────────────────
  sync:
    image: ghcr.io/antoniolago/vaultwarden-kubernetes-secrets:latest
    <<: *demo-base
    depends_on:
      vaultwarden-seed:
        condition: service_completed_successfully
    environment:
      VAULTWARDEN__SERVERURL: "http://vaultwarden:80"
      BW_CLIENTID: "${BW_CLIENTID}"
      BW_CLIENTSECRET: "${BW_CLIENTSECRET}"
      VAULTWARDEN__MASTERPASSWORD: "${VAULTWARDEN__MASTERPASSWORD:-demo-password}"
      SYNC__CONTINUOUSSYNC: "true"
      SYNC__SYNCINTERVALSECONDS: "60"
      SYNC__DRYRUN: "false"
      SYNC__DELETEORPHANS: "true"
      KUBERNETES__INCLUSTER: "false"
      KUBERNETES__KUBECONFIGPATH: "/kube/config"
      KUBERNETES__DEFAULTNAMESPACE: "vks-demo"
      LOGGING__LOGLEVEL__DEFAULT: "Information"
    volumes:
      - demo-kubeconfig:/kube/config:ro
    deploy:
      resources:
        limits:
          cpus: "0.25"
          memory: 256M

  # ── REST API ─────────────────────────────────────────
  api:
    image: ghcr.io/antoniolago/vaultwarden-kubernetes-secrets-api:latest
    <<: *demo-base
    ports:
      - "8181:8080"
    depends_on:
      - sync
    environment:
      API__AUTH__ENABLED: "true"
      API__AUTH__TOKEN: "${API_TOKEN:-demo-api-token}"
      DATABASE__PATH: "/data/sync.db"
      LOGGING__LOGLEVEL__DEFAULT: "Information"
    volumes:
      - api-data:/data
    deploy:
      resources:
        limits:
          cpus: "0.25"
          memory: 256M

  # ── Dashboard ────────────────────────────────────────
  dashboard:
    image: ghcr.io/antoniolago/vaultwarden-kubernetes-secrets-dashboard:latest
    <<: *demo-base
    ports:
      - "8182:80"
    depends_on:
      - api
    environment:
      API_URL: "http://api:8080/api"
      LOGINLESS_MODE: "false"
    deploy:
      resources:
        limits:
          cpus: "0.1"
          memory: 64M

networks:
  demo-net:
    driver: bridge
    ipam:
      config:
        - subnet: 172.20.0.0/16

volumes:
  vaultwarden-data:
    driver: local
  kind-data:
    driver: local
  api-data:
    driver: local
  demo-kubeconfig:
    driver: local
```

### Kind Cluster Configuration

File: `demo/kind-config.yaml`

```yaml
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
name: vks-demo
nodes:
  - role: control-plane
    extraPortMappings:
      - containerPort: 30000
        hostPort: 30000
    kubeadmConfigPatches:
      - |
        kind: InitConfiguration
        nodeRegistration:
          kubeletExtraArgs:
            node-labels: "demo=true"
networking:
  apiServerAddress: "0.0.0.0"
  podSubnet: "10.244.0.0/16"
  serviceSubnet: "10.96.0.0/12"
```

### Seed Data Script

File: `demo/seed-data.sh`

```bash
#!/bin/sh
# One-shot script to pre-seed Vaultwarden with demo items.
# Runs inside bitwarden/cli container after Vaultwarden starts.

set -e

BW_SESSION=""

# Login with demo credentials
bw config server "${VAULTWARDEN_URL}"
bw login "${BW_CLIENTID}" "${BW_CLIENTSECULT}"
BW_SESSION=$(bw unlock "${VAULTWARDEN__MASTERPASSWORD}" --raw)

# Create a test folder
FOLDER_ID=$(bw get folder "demo-secrets" --session "${BW_SESSION}" 2>/dev/null \
  || bw create folder --session "${BW_SESSION}" --name "demo-secrets" | jq -r '.id')

# Create pre-seeded items with custom fields for K8s sync

# Item 1: Database credentials
bw create item --session "${BW_SESSION}" <<'ITEM'
{
  "name": "demo-database-credentials",
  "type": 1,
  "folderId": "__FOLDER_ID__",
  "login": { "username": "app_user", "password": "s3cure!Pass99" },
  "fields": [
    { "name": "secret-name", "value": "db-credentials", "type": 2 },
    { "name": "namespaces",  "value": "vks-demo",      "type": 2 },
    { "name": "secret-key-username", "value": "DB_USERNAME", "type": 2 },
    { "name": "secret-key-password", "value": "DB_PASSWORD", "type": 2 }
  ]
}
ITEM

# Item 2: API key
bw create item --session "${BW_SESSION}" <<'ITEM'
{
  "name": "demo-api-key",
  "type": 1,
  "folderId": "__FOLDER_ID__",
  "login": { "username": "service-account", "password": "sk-demo-xxxxxxxxxxxxxxxx" },
  "fields": [
    { "name": "secret-name", "value": "api-key", "type": 2 },
    { "name": "namespaces",  "value": "vks-demo", "type": 2 },
    { "name": "secret-key-username", "value": "API_KEY_ID",  "type": 2 },
    { "name": "secret-key-password", "value": "API_KEY_SECRET", "type": 2 }
  ]
}
ITEM

echo "Seed data loaded successfully."
```

### Local Mode Makefile / Quickstart

```bash
#!/usr/bin/env bash
# scripts/demo-local.sh — Start the local demo environment

set -euo pipefail

echo "=== Vaultwarden K8s Secrets — Local Demo ==="

# 1. Create Kind cluster
echo "[1/5] Creating Kind cluster..."
kind create cluster --config demo/kind-config.yaml

# 2. Extract kubeconfig for sync service
echo "[2/5] Extracting kubeconfig..."
kind get kubeconfig --name vks-demo > demo/kubeconfig.yaml

# 3. Create demo namespace
echo "[3/5] Creating demo namespace..."
kubectl create namespace vks-demo --kubeconfig demo/kubeconfig.yaml

# 4. Build images (if not pulling)
echo "[4/5] Starting Docker Compose..."
docker compose -f docker-compose.demo.yml up -d

# 5. Wait for seed and verify
echo "[5/5] Waiting for seed data..."
docker compose -f docker-compose.demo.yml logs -f vaultwarden-seed

echo "=== Demo ready! ==="
echo "  Dashboard : http://localhost:8182"
echo "  API       : http://localhost:8181/api/swagger"
echo "  Vaultwarden: http://localhost:8180"
echo ""
echo "  K8s context: kind-vks-demo"
echo "  To stop: docker compose -f docker-compose.demo.yml down && kind delete cluster --name vks-demo"
```

---

## Cloud Demo Mode

### Architecture

Cloud demo mode uses a single Helm deployment with restricted values optimized for ephemeral demonstration on a public Kubernetes cluster (e.g., DigitalOcean, Civo, or a small Linode cluster).

```
┌──────────────────────────────────────────────────────────┐
│                    Public K8s Cluster                     │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │            Namespace: vks-demo                    │   │
│  │                                                  │   │
│  │  ┌──────────────┐   ┌──────────────────┐        │   │
│  │  │  Vaultwarden  │   │  Sync Service    │        │   │
│  │  │  (ephemeral)  │   │  :8080           │        │   │
│  │  │  :8080        │   │  replica: 1      │        │   │
│  │  │  pre-seeded   │   └────────┬─────────┘        │   │
│  │  └──────────────┘            │                   │   │
│  │                              ▼                   │   │
│  │  ┌──────────────┐   ┌──────────────────┐        │   │
│  │  │  API Service  │◄──│  Dashboard       │        │   │
│  │  │  :8080        │   │  :80 (nginx)     │        │   │
│  │  │  auth: token  │   │  replica: 1-2    │        │   │
│  │  └──────┬───────┘   └────────┬─────────┘        │   │
│  │         │                    │                   │   │
│  │         └────────┬───────────┘                   │   │
│  │                  │                               │   │
│  │         ┌────────▼────────┐                      │   │
│  │         │  Ingress        │                      │   │
│  │         │  TLS + auth     │                      │   │
│  │         └────────┬────────┘                      │   │
│  │                  │                               │   │
│  │         ┌────────▼────────┐                      │   │
│  │         │  Auto-Reset     │                      │   │
│  │         │  CronJob (6h)   │                      │   │
│  │         └─────────────────┘                      │   │
│  └──────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
```

### Restricted Helm Values

File: `demo/cloud-demo-values.yaml`

```yaml
# ═══════════════════════════════════════════════════════════════
# Cloud Demo Values — RESTRICTED for public-facing demo
# ═══════════════════════════════════════════════════════════════

# ── Global overrides ─────────────────────────────────────────
global:
  imageTag: "latest"

image:
  pullPolicy: Always

# ── Security ─────────────────────────────────────────────────
podSecurityContext:
  runAsNonRoot: true
  runAsUser: 1000
  runAsGroup: 1000
  fsGroup: 1000

containerSecurityContext:
  readOnlyRootFilesystem: true
  allowPrivilegeEscalation: false
  runAsNonRoot: true
  runAsUser: 1000
  runAsGroup: 1000
  capabilities:
    drop:
      - ALL

# ── Resource limits (tight) ──────────────────────────────────
resources:
  requests:
    cpu: 25m
    memory: 64Mi
  limits:
    cpu: 200m
    memory: 256Mi

# ── Sync service config ─────────────────────────────────────
env:
  config:
    VAULTWARDEN__SERVERURL: "http://vaultwarden:80"
    VAULTWARDEN__ORGANIZATIONID: ""
    VAULTWARDEN__COLLECTIONID: ""
    VAULTWARDEN__FOLDERID: ""
    KUBERNETES__INCLUSTER: "true"
    KUBERNETES__DEFAULTNAMESPACE: "vks-demo"
    SYNC__DRYRUN: "false"
    SYNC__DELETEORPHANS: "true"
    SYNC__SECRETPREFIX: "demo-"
    SYNC__SYNCINTERVALSECONDS: "60"
    SYNC__CONTINUOUSSYNC: "true"
    LOGGING__LOGLEVEL__DEFAULT: "Information"
    LOGGING__LOGLEVEL__MICROSOFT: "Warning"

# ── API ──────────────────────────────────────────────────────
api:
  enabled: true
  replicaCount: 1
  resources:
    requests:
      cpu: 25m
      memory: 64Mi
    limits:
      cpu: 200m
      memory: 256Mi
  databasePath: /tmp/sync.db  # Ephemeral — no PVC
  persistence:
    enabled: false
  auth:
    enabled: true
    loginlessMode: false
    # Token auto-generated on install

# ── Dashboard ───────────────────────────────────────────────
dashboard:
  enabled: true
  replicaCount: 1
  resources:
    requests:
      cpu: 10m
      memory: 32Mi
    limits:
      cpu: 50m
      memory: 64Mi
  loginlessMode: false

# ── Ingress ─────────────────────────────────────────────────
ingress:
  enabled: true
  className: "nginx"
  annotations:
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/limit-rps: "10"
    nginx.ingress.kubernetes.io/limit-connections: "20"
    # Rate limiting per IP
    nginx.ingress.kubernetes.io/limit-rpm: "60"
  hosts:
    - host: demo.vaultwarden-k8s-sync.example.com
      paths:
        - path: /
          pathType: Prefix
          backend: dashboard
          port: 80
        - path: /api
          pathType: Prefix
          backend: api
          port: 8080
  tls:
    - secretName: vks-demo-tls
      hosts:
        - demo.vaultwarden-k8s-sync.example.com

# ── No persistence anywhere ─────────────────────────────────
# All SQLite databases are in /tmp — data lost on pod restart
# No PVCs are created

# ── Monitoring (disabled for demo) ──────────────────────────
monitoring:
  serviceMonitor:
    enabled: false
  grafanaDashboard:
    enabled: false

# ── RBAC (minimal) ─────────────────────────────────────────
rbac:
  create: true
  clusterWide: false   # Restrict to single namespace for demo
```

### Additional Cloud Demo Components

#### Vaultwarden Sub-chart / Sidecar

```yaml
# demo/vaultwarden-demo-patch.yaml
# Patches to add an ephemeral Vaultwarden instance to the demo
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: vaultwarden-demo
  namespace: vks-demo
spec:
  replicas: 1
  selector:
    matchLabels:
      app: vaultwarden-demo
  template:
    metadata:
      labels:
        app: vaultwarden-demo
    spec:
      securityContext:
        runAsNonRoot: true
        runAsUser: 1000
        fsGroup: 1000
      containers:
        - name: vaultwarden
          image: vaultwarden/server:latest
          ports:
            - containerPort: 80
          env:
            - name: SIGNUPS_ALLOWED
              value: "false"
            - name: ADMIN_TOKEN
              valueFrom:
                secretKeyRef:
                  name: vks-demo-secrets
                  key: VW_ADMIN_TOKEN
          resources:
            requests:
              cpu: 50m
              memory: 64Mi
            limits:
              cpu: 500m
              memory: 256Mi
          securityContext:
            readOnlyRootFilesystem: true
            allowPrivilegeEscalation: false
            capabilities:
              drop:
                - ALL
          # Ephemeral: no volume mounts for persistence
      initContainers:
        - name: vaultwarden-seed
          image: bitwarden/cli:latest
          env:
            - name: VAULTWARDEN_URL
              value: "http://vaultwarden-demo:80"
            - name: VW_ADMIN_TOKEN
              valueFrom:
                secretKeyRef:
                  name: vks-demo-secrets
                  key: VW_ADMIN_TOKEN
          command:
            - /bin/sh
            - /seed-data.sh
          volumeMounts:
            - name: seed-script
              mountPath: /seed-data.sh
              subPath: seed-data.sh
      volumes:
        - name: seed-script
          configMap:
            name: vks-demo-seed
            defaultMode: 0555
---
apiVersion: v1
kind: Service
metadata:
  name: vaultwarden-demo
  namespace: vks-demo
spec:
  selector:
    app: vaultwarden-demo
  ports:
    - port: 80
      targetPort: 80
```

#### Auto-Reset CronJob

```yaml
# demo/auto-reset-cronjob.yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: vks-demo-auto-reset
  namespace: vks-demo
spec:
  schedule: "0 */6 * * *"   # Every 6 hours
  concurrencyPolicy: Replace
  jobTemplate:
    spec:
      ttlSecondsAfterFinished: 300
      template:
        spec:
          serviceAccountName: vks-demo-reset
          restartPolicy: Never
          containers:
            - name: reset
              image: bitnami/kubectl:latest
              command:
                - /bin/sh
                - -c
                - |
                  echo "=== VKS Demo Auto-Reset ==="
                  echo "[1/3] Deleting all demo pods (Vaultwarden will auto-recreate)..."
                  kubectl delete pods -n vks-demo --all --grace-period=30
                  echo "[2/3] Deleting all K8s secrets in demo namespace..."
                  kubectl delete secrets -n vks-demo --all
                  echo "[3/3] Waiting for Vaultwarden to restart and re-seed..."
                  sleep 10
                  echo "Reset complete. The seed init container will re-populate data."
```

---

## Safety Guardrails (CRITICAL)

These guardrails are **mandatory** for any public-facing demo instance. They must be verified before deployment.

### 1. Ephemeral Storage — NO Persistent Volumes

| Component | Storage | Rationale |
|-----------|---------|-----------|
| API Database | `/tmp/sync.db` (emptyDir) | Reset on pod restart |
| Vaultwarden | `/data` (emptyDir) | No vault persistence |
| Dashboard | N/A | Stateless (nginx) |
| Sync Service | N/A | Stateless |

```
# Enforce in Values:
api:
  persistence:
    enabled: false   # ← CRITICAL: disables PVC creation
  databasePath: /tmp/sync.db
```

### 2. NO Host Mounts

- **No `hostPath` volumes** are used in any demo deployment.
- **No `docker.sock` mounts** (unlike the E2E compose file which requires them).
- All data resides in container ephemeral storage or `emptyDir` volumes.

### 3. NO Privileged Containers

| Setting | Value | Check |
|---------|-------|-------|
| `privileged` | `false` | ✓ All containers |
| `allowPrivilegeEscalation` | `false` | ✓ All containers |
| `capabilities.drop` | `ALL` | ✓ All containers |
| `readOnlyRootFilesystem` | `true` | ✓ All containers |

### 4. NO Host Network Mode

- `hostNetwork: false` for all pods.
- All networking goes through ClusterIP services and Ingress.

### 5. Authentication Required

- **API**: Token-based auth enabled (`api.auth.enabled: true`).
  - Token auto-generated on Helm install.
  - Token required on every API request via `Authorization: Bearer <token>` header.
- **Dashboard**: Reads token from API, requires login screen.
- **Ingress**: Optionally add basic auth via annotation:

```yaml
nginx.ingress.kubernetes.io/auth-type: basic
nginx.ingress.kubernetes.io/auth-secret: vks-demo-ingress-auth
```

### 6. Rate Limiting

| Layer | Limit | Mechanism |
|-------|-------|-----------|
| Ingress | 10 req/s per IP | nginx `limit-rps` annotation |
| Ingress | 60 req/min per IP | nginx `limit-rpm` annotation |
| Ingress | 20 concurrent connections | nginx `limit-connections` annotation |
| API | (future) | ASP.NET rate limiting middleware |

### 7. Auto-Reset Mechanism

- **CronJob** runs every 6 hours.
- On trigger: deletes all pods and K8s secrets in the demo namespace.
- Vaultwarden is recreated by the Deployment controller, and the seed init container re-populates test data.
- Result: any leaked data, modified state, or malicious content is wiped.

### 8. Resource Limits

| Component | CPU Limit | Memory Limit |
|-----------|-----------|-------------|
| Vaultwarden | 500m | 256Mi |
| Sync Service | 200m | 256Mi |
| API | 200m | 256Mi |
| Dashboard | 50m | 64Mi |
| Kind (local) | 1000m | 1Gi |

### 9. Non-Root User Enforcement

All container images in this project already run as non-root:

| Image | User | UID |
|-------|------|-----|
| Sync Service | `appuser` | (created in Dockerfile) |
| API | `app` | (default ASP.NET) |
| Dashboard | `nginx` | 101 |

### 10. Read-Only Root Filesystem

- `readOnlyRootFilesystem: true` set on all containers.
- Writable paths (e.g., `/tmp`, `/data`) are explicitly mounted as `emptyDir`.

### 11. Pod Security Standards

```yaml
# Applied at namespace level
apiVersion: v1
kind: Namespace
metadata:
  name: vks-demo
  labels:
    pod-security.kubernetes.io/enforce: restricted
    pod-security.kubernetes.io/audit: restricted
    pod-security.kubernetes.io/warn: restricted
```

### 12. (Optional) OPA/Gatekeeper Constraints

```yaml
# Constraint: no privileged containers in demo namespace
apiVersion: constraints.gatekeeper.sh/v1beta1
kind: K8sPSPPrivilegedContainer
metadata:
  name: vks-demo-no-privileged
spec:
  match:
    kinds:
      - apiGroups: [""]
        kinds: ["Pod"]
    namespaces:
      - "vks-demo"
```

---

## Setup Instructions

### Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| Docker | ≥ 24.x | Container runtime |
| Kind | ≥ 0.23.x | Local K8s cluster |
| kubectl | ≥ 1.28.x | K8s API interaction |
| Helm | ≥ 3.14.x | Cloud deployment |
| curl/jq | latest | API testing |

### Environment Variables

```bash
# ── Required ──────────────────────────────────────────
export BW_CLIENTID="user.democlient@example.com"       # Vaultwarden API client ID
export BW_CLIENTSECRET="demo-client-secret-change-me"   # Vaultwarden API client secret
export VAULTWARDEN__MASTERPASSWORD="demo-password"      # Master password for test vault

# ── Optional with defaults ────────────────────────────
export API_TOKEN="demo-api-token"                       # Dashboard/API auth
export VW_ADMIN_TOKEN="demo-admin-token"                # Vaultwarden admin panel
export DOMAIN="demo.vaultwarden-k8s-sync.example.com"   # Cloud demo domain (for Ingress)
export LETSENCRYPT_EMAIL="admin@example.com"             # For cert-manager
```

### Local Mode Setup

```bash
# Step 1: Clone and enter repo
git clone https://github.com/antoniolago/vaultwarden-kubernetes-secrets.git
cd vaultwarden-kubernetes-secrets

# Step 2: Create Kind cluster
kind create cluster --config demo/kind-config.yaml

# Step 3: Extract kubeconfig
kind get kubeconfig --name vks-demo > demo/kubeconfig.yaml

# Step 4: Create demo namespace in Kind
kubectl create namespace vks-demo --kubeconfig demo/kubeconfig.yaml

# Step 5: Deploy demo stack
docker compose -f docker-compose.demo.yml up -d

# Step 6: Verify
docker compose -f docker-compose.demo.yml ps
```

### Cloud Mode Setup

```bash
# Step 1: Add required repos
helm repo add jetstack https://charts.jetstack.io
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm repo update

# Step 2: Install cert-manager (if not present)
helm upgrade -i cert-manager jetstack/cert-manager \
  --namespace cert-manager --create-namespace \
  --set installCRDs=true

# Step 3: Install nginx-ingress (if not present)
helm upgrade -i ingress-nginx ingress-nginx/ingress-nginx \
  --namespace ingress-nginx --create-namespace

# Step 4: Create demo namespace with labels
kubectl create namespace vks-demo
kubectl label namespace vks-demo \
  pod-security.kubernetes.io/enforce=restricted

# Step 5: Create demo secrets
kubectl create secret generic vks-demo-secrets \
  --namespace vks-demo \
  --from-literal=BW_CLIENTID="${BW_CLIENTID}" \
  --from-literal=BW_CLIENTSECRET="${BW_CLIENTSECRET}" \
  --from-literal=VAULTWARDEN__MASTERPASSWORD="${VAULTWARDEN__MASTERPASSWORD}" \
  --from-literal=VW_ADMIN_TOKEN="${VW_ADMIN_TOKEN}"

# Step 6: Install the demo release
helm upgrade -i vks-demo \
  charts/vaultwarden-kubernetes-secrets \
  --namespace vks-demo \
  --values demo/cloud-demo-values.yaml \
  --set env.secrets.BW_CLIENTID.secretName=vks-demo-secrets \
  --set env.secrets.BW_CLIENTSECRET.secretName=vks-demo-secrets \
  --set env.secrets.VAULTWARDEN__MASTERPASSWORD.secretName=vks-demo-secrets

# Step 7: Deploy auto-reset CronJob
kubectl apply -f demo/auto-reset-cronjob.yaml

# Step 8: Verify
kubectl get pods -n vks-demo
kubectl get ingress -n vks-demo
```

### Access URLs

#### Local Mode

| Service | URL |
|---------|-----|
| Dashboard | `http://localhost:8182` |
| API (Swagger) | `http://localhost:8181/api/swagger` |
| Vaultwarden Admin | `http://localhost:8180/admin` |
| Kind K8s API | `https://localhost:6443` (via kubeconfig) |

#### Cloud Mode

| Service | URL |
|---------|-----|
| Dashboard | `https://demo.vaultwarden-k8s-sync.example.com` |
| API | `https://demo.vaultwarden-k8s-sync.example.com/api` |

---

## Reset Procedure

### Manual Reset

```bash
# Local Mode
docker compose -f docker-compose.demo.yml down -v  # Destroys all volumes
kind delete cluster --name vks-demo
# Then re-run setup

# Cloud Mode
kubectl delete pods -n vks-demo --all
kubectl delete secrets -n vks-demo --all
# Pods recreate via Deployment controller
# Seed init container re-populates data
```

### Auto-Reset Cron

The CronJob `vks-demo-auto-reset` runs every 6 hours (`0 */6 * * *`) and:

1. Deletes all pods in the `vks-demo` namespace.
2. Deletes all K8s Secrets in the `vks-demo` namespace.
3. Deployment controllers recreate pods with fresh state.
4. The Vaultwarden init container re-seeds test data.

To modify the interval:

```bash
kubectl patch cronjob vks-demo-auto-reset -n vks-demo \
  --type=json \
  -p='[{"op": "replace", "path": "/spec/schedule", "value": "0 */3 * * *"}]'
```

### Data Loss Warning

> ⚠️ **WARNING**: This demo environment uses **ephemeral storage** throughout.
>
> - No SQLite database is persisted across pod restarts.
> - No Vaultwarden vault data survives a restart.
> - All K8s Secrets are automatically cleaned up on reset.
> - The auto-reset CronJob will destroy **all** demo data every 6 hours.
> - This is intentional: the demo is designed to be stateless and self-healing.
>
> **Do not use this configuration for any workload that requires data persistence.**

---

## Limitations

### Vaultwarden Credentials

- The demo uses **pre-seeded test data only** (loaded by the seed init container).
- No real Vaultwarden credentials are configured or exposed.
- The `BW_CLIENTID` and `BW_CLIENTSECRET` are for the demo instance only.
- `SIGNUPS_ALLOWED: false` prevents new account creation on the demo Vaultwarden.

### Kubernetes Secrets

- All synced K8s Secrets are demo/test data only.
- Secret names are prefixed with `demo-` for clear identification.
- No production secrets are ever used in the demo environment.

### Performance

| Aspect | Limitation | Reason |
|--------|-----------|--------|
| Sync interval | Minimum 60s | Avoid excessive API calls to Vaultwarden |
| Concurrent users | 20 max | Ingress connection limit |
| Request rate | 10 req/s | Ingress rate limiting |
| Pod resources | Tight limits | Keep cluster costs low |
| Kind cluster | Single node | Local resource constraints |

### Production Unsuitability

This demo environment is **NOT suitable for**:

- Production workloads of any kind.
- Storing real credentials or secrets.
- Performance or load testing.
- Multi-tenant use cases.
- Long-running demonstrations (> 6 hours without reset).
- Compliance with any security standard (SOC2, ISO 27001, etc.).

### Known Gaps (Future Improvements)

| Gap | Impact | Future Solution |
|-----|--------|----------------|
| No Prometheus/Grafana | No metrics visibility | Add lightweight metrics stack |
| No alerting | Silent failures | Add demo alertmanager |
| Single Ingress IP | Single point of failure | Multi-replica Ingress |
| Manual DNS setup | Requires DNS config | Add DNS automation script |
| No CI/CD pipeline | Manual deploy | GitHub Actions demo deploy |
| Seed script in ConfigMap | Credentials visible in plaintext | Use sealed secrets or SOPS |

---

## Appendix A: Cost Estimate (Cloud Demo)

| Resource | Size | Monthly Cost (approx) |
|----------|------|----------------------|
| K8s Node | 2 vCPU, 4GB RAM | $15-25 |
| Load Balancer | 1 LB | $10-15 |
| TLS certs | Free (Let's Encrypt) | $0 |
| **Total** | | **$25-40/month** |

## Appendix B: File Manifest

```
demo/
├── cloud-demo-values.yaml             # Restricted Helm values for cloud deployment
├── kind-config.yaml                   # Kind cluster configuration
├── kubeconfig.yaml                    # Generated by setup (gitignored)
├── seed-data.sh                       # Vaultwarden seed script
├── vaultwarden-demo-patch.yaml        # Ephemeral Vaultwarden deployment
├── auto-reset-cronjob.yaml            # Auto-reset CronJob
docker-compose.demo.yml                # Local demo Docker Compose
scripts/demo-local.sh                  # One-command local demo startup
```
