# Examples

## Basic: Login → Opaque Secret

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
  password: c2VjcmV0MTIz  # secret123
  username: YWRtaW4=  # admin
```

## Custom Key Names

Override default key names with `secret-key-username` and `secret-key-password`.

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

## Custom Fields as Secret Data

Any custom field that isn't a reserved name becomes a data entry in the Secret.

**Vaultwarden Item:**
- Name: `app-config`
- Custom fields:
  - `namespaces` = `production`
  - `API_KEY` = `xyz123`
  - `DATABASE_URL` = `postgres://...`

**Result:** All custom fields (except reserved ones like `namespaces`, `secret-name`, etc.) are synced as data entries.

## TLS Certificate (kubernetes.io/tls)

**Vaultwarden Item:**
- Name: `my-tls-cert`
- Custom fields:
  - `namespaces` = `production`
  - `secret-type` = `kubernetes.io/tls`
  - `tls.crt` = `-----BEGIN CERTIFICATE-----...`
  - `tls.key` = `-----BEGIN PRIVATE KEY-----...`

**Result in Kubernetes:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: my-tls-cert
  namespace: production
type: kubernetes.io/tls
data:
  tls.crt: LS0tLS1CRUdJTi...  # base64 encoded
  tls.key: LS0tLS1CRUdJTi...  # base64 encoded
```

## Docker Registry Credentials (kubernetes.io/dockerconfigjson)

Two approaches. Store the raw Docker config JSON in the password field:

**Vaultwarden Item:**
- Name: `my-ghcr-token`
- Username: `(empty)`
- Password: `{"auths":{"ghcr.io":{"username":"...","password":"...","auth":"..."}}}`
- Custom fields:
  - `namespaces` = `production`
  - `secret-type` = `kubernetes.io/dockerconfigjson`

Or use individual custom fields:

**Vaultwarden Item:**
- Name: `my-ghcr-token`
- Username: `user`
- Password: `example-token`
- Custom fields:
  - `namespaces` = `production`
  - `secret-type` = `kubernetes.io/dockerconfigjson`
  - `docker-config-json-server` = `ghcr.io`
  - `docker-config-json-email` = `me@example.com`

**Result in Kubernetes:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: my-ghcr-token
  namespace: production
type: kubernetes.io/dockerconfigjson
data:
  .dockerconfigjson: eyJhdXRocyI6eyJnaGNyLmlvIjp7InVzZXJuYW1lIjoidXNlciIsInBhc3N3b3JkIjoiZXhhbXBsZS10b2tlbiIsImVtYWlsIjoibWVAZXhhbXBsZS5jb20iLCJhdXRoIjoiZFhObGNqcGxlYlhBbWNtRmpiMlJsYzJVdCJ9fX0=
```

Decoded `.dockerconfigjson`:
```json
{"auths":{"ghcr.io":{"username":"user","password":"example-token","email":"me@example.com","auth":"dXNlcjpleGFtcGxlLXRva2Vu"}}}
```

**Supported secret types:**
- `Opaque` (default): arbitrary user-defined data
- `kubernetes.io/basic-auth`: credentials for basic authentication
- `kubernetes.io/tls`: TLS certificate and key
- `kubernetes.io/dockerconfigjson`: Docker registry credentials

## SSH Key

Vaultwarden SSH Key items sync the private key, public key, and fingerprint automatically.

**Vaultwarden SSH Key Item:**
- Name: `deploy-key`
- Private key is stored as the password field
- Custom fields:
  - `namespaces` = `production`

**Result in Kubernetes:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: deploy-key
  namespace: production
data:
  private-key: LS0tLS1CRUdJTiBPUEVOU1NIIFBSSVZBVEUgS0VZLS0tLS0...
  public-key: c3NoLWVkMjU1MTkgQUFB...
  fingerprint: U0hBMjU2Onh4eHh4...
```

The default key names are `private-key`, `public-key`, and `fingerprint`. Use `secret-key-password` to rename the private key entry.

## Kubernetes YAML from Notes

Secure Note items containing valid Kubernetes YAML are applied via the Kubernetes API.

**Vaultwarden Secure Note (YAML in notes, no custom fields):**
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: my-config
  namespace: default
data:
  key: value
```

*You can still use "namespaces" custom field to sync to multiple namespaces.

**Result:** ConfigMap is created or updated in the `default` namespace using `metadata.namespace` from the YAML.

## stringData: Mode

Notes starting with `stringData:` are parsed as key-value pairs.

**Vaultwarden Secure Note:**
```text
stringData:
DATABASE_URL=postgres://user:pass@host/db
API_KEY=secret123
ENABLE_FEATURE=true
```

**Result in Kubernetes Secret:**
```yaml
data:
  DATABASE_URL: cG9zdGdyZXM6Ly91c2VyOnBhc3NAaG9zdC9kYg==
  API_KEY: c2VjcmV0MTIz
  ENABLE_FEATURE: dHJ1ZQ==
```

## Annotations and Labels

Add metadata using `secret-annotation` and `secret-label`. Multiple fields with the same name are supported (one `key=value` per field).

**Vaultwarden Item:**
- Name: `monitoring-config`
- Custom fields:
  - `namespaces` = `production`
  - `secret-annotation` = `app.kubernetes.io/version=1.2.3`
  - `secret-annotation` = `example.com/owner: platform-team`
  - `secret-annotation` = `monitoring.enabled=true`
  - `secret-label` = `environment=production`
  - `secret-label` = `argocd.argoproj.io/secret-type=repository`
  - `secret-label` = `app=myapp`

**Result in Kubernetes:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: monitoring-config
  namespace: production
  annotations:
    app.kubernetes.io/version: "1.2.3"
    example.com/owner: "platform-team"
    monitoring.enabled: "true"
  labels:
    environment=production
    argocd.argoproj.io/secret-type=repository
    app=myapp
data:
  # ... secret data ...
```

## Multi-Namespace

Use a comma-separated list in the `namespaces` field to create Secrets in multiple namespaces from a single item.

**Vaultwarden Item:**
- Name: `shared-config`
- Custom fields:
  - `namespaces` = `us-east,us-west,eu-central`

**Result:** A Secret named `shared-config` in each namespace (us-east, us-west, eu-central).

## Secret Merging

Multiple items with the same `secret-name` merge into a single Secret.

**Vaultwarden Item 1:**
- Name: `app-db-config`
- Custom fields:
  - `namespaces` = `production`
  - `secret-name` = `app-config`
  - `DATABASE_URL` = `postgres://...`

**Vaultwarden Item 2:**
- Name: `app-api-config`
- Custom fields:
  - `namespaces` = `production`
  - `secret-name` = `app-config`
  - `API_KEY` = `xyz123`

**Result:** A single Secret `app-config` containing both `DATABASE_URL` and `API_KEY`.

## Multi-Cluster (context-name)

Use `context-name` to sync items only to specific clusters.

**Vaultwarden Item:**
- Name: `us-only-secret`
- Custom fields:
  - `namespaces` = `production`
  - `context-name` = `us-cluster`

This secret only syncs to the cluster with a matching context name. Items without `context-name` sync to all clusters.

**How the context name is determined:**
1. `SYNC__CONTEXTNAME` env var (set per deployment) — used when configured.
2. Otherwise a predictable default (`default`), which represents the unnamed/unconfigured cluster.

Each cluster that should receive cluster-specific items must set `SYNC__CONTEXTNAME` to a unique name (e.g. `production`, `us-cluster`) so filtering matches. When run outside the cluster (kubeconfig), the current kubeconfig context name is used when available.

## Field Filtering

Use `ignore-field` to exclude specific fields from the Secret.

**Vaultwarden Item:**
- Name: `filtered-config`
- Custom fields:
  - `namespaces` = `production`
  - `ignore-field` = `INTERNAL_NOTE,DEBUG_FLAG`
  - `API_KEY` = `xyz123`
  - `INTERNAL_NOTE` = `this is an internal note`
  - `DEBUG_FLAG` = `true`

**Result:** The Secret only contains `API_KEY`. The `INTERNAL_NOTE` and `DEBUG_FLAG` fields are excluded.

## Breaking Changes (v2.0)

If upgrading from v1.x, default secret key names changed:

**Before (v1.x):**
- Username key: `<item-name>-username` (e.g., `my-secret-username`)
- Password key: `<item-name>` (e.g., `my-secret`)
- SSH public key: `<item-name>-public-key`
- SSH fingerprint: `<item-name>-fingerprint`

**After (v2.0):**
- Username key: `username`
- Password key: `password`
- SSH private key: `private-key`
- SSH public key: `public-key`
- SSH fingerprint: `fingerprint`

**Migration:** Update your applications to use the new static key names, or use custom field overrides:
- `secret-key-username` = your preferred username key
- `secret-key-password` = your preferred password key
