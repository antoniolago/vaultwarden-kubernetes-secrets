<div align="center">
  <img src="dashboard/vks.png" alt="VKS Logo" width="120" height="120" />
  
  # Vaultwarden Kubernetes Secrets Sync

> **[🎮 Live Demo](https://antoniolago.github.io/vaultwarden-kubernetes-secrets/)** - Try the dashboard with realistic mock data!

  [![Artifact Hub](https://img.shields.io/endpoint?url=https://artifacthub.io/badge/repository/vaultwarden-kubernetes-secrets)](https://artifacthub.io/packages/search?repo=vaultwarden-kubernetes-secrets) [![GitHub](https://img.shields.io/badge/github-%23121011.svg?style=for-the-badge&logo=github&logoColor=white)](https://github.com/antoniolago/vaultwarden-kubernetes-secrets)
</div>

Automatically sync secrets from [Vaultwarden](https://github.com/dani-garcia/vaultwarden) to Kubernetes. Store your secrets in Vaultwarden, tag them with target namespaces, and they'll be created as Kubernetes Secrets.

⚠️ **Early stage**: May have high CPU usage with frequent syncs. Increase `SYNC__SYNCINTERVALSECONDS` if needed.

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

# Install the sync service (using GitHub Container Registry)
helm upgrade -i vaultwarden-kubernetes-secrets oci://ghcr.io/antoniolago/charts/vaultwarden-kubernetes-secrets \
  --version "$CHART_VERSION" \
  --namespace "$NAMESPACE" --create-namespace \
  --set env.config.VAULTWARDEN__SERVERURL="$SERVER_URL" \
  --set image.tag="$CHART_VERSION"

# Alternative: Use Harbor registry (faster in some regions)
# helm upgrade -i vaultwarden-kubernetes-secrets oci://harbor.lag0.com.br/charts/vaultwarden-kubernetes-secrets \
#   --version "$CHART_VERSION" \
#   --namespace "$NAMESPACE" --create-namespace \
#   --set env.config.VAULTWARDEN__SERVERURL="$SERVER_URL" \
#   --set image.tag="$CHART_VERSION"
```

**Security tip**: Create a dedicated Vaultwarden user for this service and scope it to a specific Organization/Collection.

**Registry Options:**
- **GHCR (Recommended)**: `oci://ghcr.io/antoniolago/charts/vaultwarden-kubernetes-secrets` - Public GitHub Container Registry
- **Harbor**: `oci://harbor.lag0.com.br/charts/vaultwarden-kubernetes-secrets` - Alternative registry 

### 2. Create a Secret in Vaultwarden

In Vaultwarden, create a **Login**, **SSH Key** or **Secure Note** item with:

**Required custom field:**
- Name: `namespaces`
- Value: `your-namespace` (e.g. `staging,production` for multiple)

**Optional custom fields:**
- `secret-name`: Set the Kubernetes Secret name (default: sanitized item name)
- `secret-key-password`: Key name for the password field (default: sanitized item name)
- `secret-key-username`: Key name for the username field (default: `<name>-username`)

**That's it!** The sync service will create the Secret in your specified namespace(s) within the sync interval.

---

## Examples

### Basic Example
**Vaultwarden Item:**
- Name: `postgres-credentials`
- Username: `admin`
- Password: `secret123`
- Custom field: `namespaces` = `production`

**Result in Kubernetes:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: postgres-credentials
  namespace: production
data:
  postgres-credentials: c2VjcmV0MTIz  # password
  postgres-credentials-username: YWRtaW4=  # username
```

### Custom Key Names
**Vaultwarden Item:**
- Name: `Database Config`
- Username: `dbuser`
- Password: `dbpass`
- Custom fields:
  - `namespaces` = `staging,production`
  - `secret-name` = `db-config`
  - `secret-key-username` = `DB_USER`
  - `secret-key-password` = `DB_PASSWORD`

**Result in Kubernetes:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: db-config
  namespace: staging  # Also created in production
data:
  DB_USER: ZGJ1c2Vy
  DB_PASSWORD: ZGJwYXNz
```

### With Additional Custom Fields
**Vaultwarden Item:**
- Name: `app-config`
- Custom fields:
  - `namespaces` = `production`
  - `API_KEY` = `xyz123`
  - `DATABASE_URL` = `postgres://...`

**Result:** All custom fields (except reserved ones) are synced to the Secret.

---

## Configuration Options

### Helm Values
Common settings you can override with `--set`:

```bash
# Scope to specific organization/collection (recommended)
--set env.config.VAULTWARDEN__ORGANIZATIONID="org-id"
--set env.config.VAULTWARDEN__COLLECTIONID="collection-id"

# Adjust sync frequency (seconds)
--set env.config.SYNC__SYNCINTERVALSECONDS="3600"

# Enable continuous sync (default: true)
--set env.config.SYNC__CONTINUOUSSYNC="true"

# Dry run mode (test without creating secrets)
--set env.config.SYNC__DRYRUN="true"
```

### Custom Field Names
You can customize the field names the app looks for:

```bash
--set env.fields.namespaces="k8s-namespaces"
--set env.fields.secretName="k8s-secret-name"
```

See [`values.yaml`](charts/vaultwarden-kubernetes-secrets/values.yaml) for all options.

---

## How It Works

1. The service uses bw-cli to log into Vaultwarden using your credentials
2. Fetches items (optionally filtered by Organization/Collection/Folder)
3. For each item with a `namespaces` custom field:
   - Creates/updates a Kubernetes Secret in each specified namespace
   - Uses the item's username, password, notes, and custom fields as Secret data
   - Sanitizes names to comply with Kubernetes requirements
4. Removes orphaned Secrets (ones previously created but no longer in Vaultwarden)
5. Repeats on the configured interval

---

## Advanced Features

- **Multi-namespace**: One Vaultwarden item → Secrets in multiple namespaces
- **Secret merging**: Multiple items with the same `secret-name` merge into one Secret
- **SSH Keys**: Private key stored as password; public key and fingerprint added automatically
- **Multiline values**: Use **Secure Note** items for multiline content (e.g., certificates)
- **Field filtering**: Use `ignore-field` custom field to exclude specific fields from sync

---

## Important Notes

- **Namespace requirement**: Items must have a `namespaces` custom field to be synced
- **Supported item types**: Login, Secure Note, SSH Key (Card/Identity not recommended)
- **User API keys only**: Organization API keys are not supported by Bitwarden CLI
- **Opaque Secrets only**: TLS and other special Secret types are not generated
- **Size limit**: Secrets must stay under ~1 MiB (Kubernetes limit)

---

## Troubleshooting

**Secrets not appearing?**
- Check the sync service logs: `kubectl logs -n vaultwarden-kubernetes-secrets deployment/vaultwarden-kubernetes-secrets`
- Verify the item has a `namespaces` custom field
- Ensure target namespaces exist in Kubernetes
- Confirm the Vaultwarden user has access to the item (check Organization/Collection permissions)

**High CPU usage?**
- Increase `SYNC__SYNCINTERVALSECONDS` (default: 30 seconds)
- Consider using Organization/Collection filters to reduce item count

---

## Contributing

We welcome contributions! Whether you're fixing bugs, adding features, or improving documentation, your help makes this project better.

**Get started:**
- Read the [Contributing Guide](CONTRIBUTING.md)
- Join the [Discussions](https://github.com/antoniolago/vaultwarden-kubernetes-secrets/discussions)

---

## Additional Resources

- [Contributing Guide](CONTRIBUTING.md) - How to contribute to this project
- [Helm chart values](charts/vaultwarden-kubernetes-secrets/values.yaml)
- [GitHub Issues](https://github.com/antoniolago/vaultwarden-kubernetes-secrets/issues)
