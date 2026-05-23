<div align="center">
  <img src="dashboard/public/vks.png" alt="VKS Logo" width="120" height="120" />
  
  # Vaultwarden Kubernetes Secrets

  [![Artifact Hub](https://img.shields.io/endpoint?url=https://artifacthub.io/badge/repository/vaultwarden-kubernetes-secrets)](https://artifacthub.io/packages/search?repo=vaultwarden-kubernetes-secrets) [![GitHub](https://img.shields.io/badge/github-%23121011.svg?style=for-the-badge&logo=github&logoColor=white)](https://github.com/antoniolago/vaultwarden-kubernetes-secrets)
[![Forgejo](https://img.shields.io/badge/forgejo-%23FB923C.svg?style=for-the-badge&logo=forgejo&logoColor=white)](https://git.lag0.com.br/antoniolago/vaultwarden-kubernetes-secrets)
</div>

Automatically sync secrets from [Vaultwarden](https://github.com/dani-garcia/vaultwarden) to Kubernetes. Store your secrets in Vaultwarden, tag them with target namespaces, and they'll be created as Kubernetes Secrets.

> **⚠️ v2.0 Breaking Changes**: If upgrading from v1.x, see the [CHANGELOG](CHANGELOG.md). Secret key names have changed from item-derived names to fixed defaults (`password`, `username`).

**Navigation**
- [Quick Start](#quick-start): Install + create your first secret
- [How It Works](#how-it-works): High-level flow
- [Custom Fields Reference](#available-custom-fields): Table of all fields
- [Configuration](#configuration): Helm values + env vars
- [Troubleshooting](#troubleshooting): Common issues
- [Examples](EXAMPLES.md): Full example catalog

---

## Quick Start

### 1. Install with Helm

```bash
# Set your values
NAMESPACE="vaultwarden-kubernetes-secrets"
SERVER_URL="https://your-vaultwarden-server.com"
BW_CLIENTID="<your_client_id>"
BW_CLIENTSECRET="<your_client_secret>"
MASTER_PASSWORD="<your_master_password>"

# Create credentials secret
kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f -
kubectl create secret generic vaultwarden-kubernetes-secrets -n "$NAMESPACE" \
  --from-literal=BW_CLIENTID="$BW_CLIENTID" \
  --from-literal=BW_CLIENTSECRET="$BW_CLIENTSECRET" \
  --from-literal=VAULTWARDEN__MASTERPASSWORD="$MASTER_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f -

# Install the sync service
helm upgrade -i vaultwarden-kubernetes-secrets oci://ghcr.io/antoniolago/charts/vaultwarden-kubernetes-secrets \
  --version "$CHART_VERSION" \
  --namespace "$NAMESPACE" --create-namespace \
  --set env.config.VAULTWARDEN__SERVERURL="$SERVER_URL" \
  --set image.tag="$CHART_VERSION"
```

**Security tip:** Create a dedicated Vaultwarden user for this service and scope it to a specific Organization or Collection.

### 2. Create your first secret

Two ways to get started:

**Standard:** Create a Login item with a `namespaces` custom field.

| Vaultwarden Field | Value |
|------------------|-------|
| Name | `postgres-credentials` |
| Username | `admin` |
| Password | `secret123` |
| Custom field: `namespaces` | `production` |

**Result in Kubernetes:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: postgres-credentials
  namespace: production
data:
  password: c2VjcmV0MTIz  # secret123
  username: YWRtaW4=  # admin
```

**YAML from notes:** Create a Secure Note with raw K8s YAML in the notes field. No custom fields needed.

| Vaultwarden Field | Value |
|------------------|-------|
| Name | `my-config` |
| Type | Secure Note |
| Notes | *(see below)* |

```
apiVersion: v1
kind: Secret
metadata:
  name: my-secret
  namespace: default
type: Opaque
data:
  key: dmFsdWU=  # value
```

This creates a Secret named `my-secret` in the `default` namespace — no `namespaces` field required.

### Available Custom Fields

| Field Name | Description | Default |
|------------|-------------|---------|
| `namespaces` | **Required** for standard items. Comma-separated list of target namespaces | - |
| `secret-name` | Custom name for the Kubernetes Secret | Sanitized item name |
| `secret-key-password` | Key name for the password/credential value | `password` |
| `secret-key-username` | Key name for the username value | `username` |
| `secret-type` | Secret type: `Opaque`, `kubernetes.io/basic-auth`, `kubernetes.io/tls`, `kubernetes.io/dockerconfigjson` | `Opaque` |
| `secret-annotation` | Custom annotations (format: `key=value` or `key: value`) | - |
| `secret-label` | Custom labels (format: `key=value` or `key: value`) | - |
| `context-name` | Filter by cluster context for multi-cluster deployments | Auto-detected from kubeconfig |
| `ignore-field` | Comma-separated list of field names to exclude from sync | - |
| `docker-config-json-server` | Docker registry server URL (for `kubernetes.io/dockerconfigjson`) | `https://index.docker.io/v1/` |
| `docker-config-json-email` | User email (for `kubernetes.io/dockerconfigjson`, optional) | - |

---

## How It Works

1. The service authenticates to Vaultwarden and fetches items (optionally filtered by Organization, Collection, or Folder).
2. For each item with a `namespaces` custom field, it creates or updates a Kubernetes Secret in each target namespace. The item's username, password, notes, and custom fields become Secret data.
3. Orphaned Secrets (previously created but no longer in Vaultwarden) are removed automatically.
4. The cycle repeats on the configured interval (default: 300 seconds).

Items without a `namespaces` field but containing valid Kubernetes YAML in their notes are applied directly using the manifest's `metadata.namespace`. See [EXAMPLES.md](EXAMPLES.md#kubernetes-yaml-from-notes) for details.

---

## Configuration

### Helm Values

```bash
# Scope to a specific organization or collection (recommended)
--set env.config.VAULTWARDEN__ORGANIZATIONID="org-id"
--set env.config.VAULTWARDEN__COLLECTIONID="collection-id"

# Adjust sync frequency (seconds)
--set env.config.SYNC__SYNCINTERVALSECONDS="3600"

# Continuous sync (default: true)
--set env.config.SYNC__CONTINUOUSSYNC="true"

# Dry run mode (test without creating secrets)
--set env.config.SYNC__DRYRUN="true"
```

See [`values.yaml`](charts/vaultwarden-kubernetes-secrets/values.yaml) for all options.

### Preventing "New Device Logged In" Notifications

The sync service generates a deterministic device ID from your server URL and client ID. This prevents Vaultwarden from sending "New device logged in" emails on every sync cycle. To set an explicit device ID:

```bash
VAULTWARDEN__DEVICEID="your-fixed-device-id"
```

### Private Registry Support

If pulling images from a private registry:

```yaml
imagePullSecrets:
  - name: my-registry-secret
```

### Logging

The service uses structured logging with environment-aware output. Production (Kubernetes) uses compact JSON format for log aggregators. Development uses colored human-readable output with timestamps.

| Variable | Default | Description |
|----------|---------|-------------|
| `LOG_LEVEL` | `Information` | Global default log level |
| `LOG_LEVEL_SYNC` | (inherit) | SyncService log level |
| `LOG_LEVEL_KUBERNETES` | (inherit) | KubernetesService log level |
| `LOG_LEVEL_VAULTWARDEN` | (inherit) | VaultwardenService log level |
| `LOG_LEVEL_DATABASE` | (inherit) | Database/Repository log level |
| `LOG_LEVEL_WEBHOOK` | (inherit) | WebhookService log level |
| `LOG_LEVEL_METRICS` | (inherit) | MetricsServer log level |
| `LOG_LEVEL_MICROSOFT` | `Warning` | Microsoft namespace log level |

Valid levels: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`

```bash
--set env.config.LOG_LEVEL="Information"
--set env.config.LOG_LEVEL_SYNC="Debug"
```

---

## Troubleshooting

**Secrets not appearing?**
- Check the sync service logs: `kubectl logs -n vaultwarden-kubernetes-secrets deployment/vaultwarden-kubernetes-secrets`
- Verify the item has a `namespaces` custom field, or that it contains valid Kubernetes YAML with `metadata.namespace` set
- Make sure target namespaces exist in Kubernetes
- Confirm the Vaultwarden user has access to the item (check Organization and Collection permissions)
- Enable the API and dashboard to visualize sync status
- Secrets must stay under the Kubernetes size limit (~1 MiB)

**Need more detail?**
- Set component-specific log levels (see [Logging](#logging) above)
- Logs include correlation IDs (`SyncId`, `Namespace`, `SecretName`) for tracing operations

---

## Examples

For detailed examples including custom field variations, secret types, annotations, labels, multi-namespace setups, secret merging, multi-cluster filtering, and migration from v1.x, see [EXAMPLES.md](EXAMPLES.md).
