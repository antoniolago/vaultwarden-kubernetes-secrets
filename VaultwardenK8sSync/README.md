# Vaultwarden Kubernetes Secrets Sync

.NET 9 app that syncs Vaultwarden items to Kubernetes Secrets. Simple tags or custom fields on items control what gets synced.

## Features

- **Automatic Sync**: Syncs Vaultwarden items to Kubernetes secrets based on namespace tags
- **Consistency Management**: Automatically deletes secrets that no longer exist in Vaultwarden
- **Authentication**: API key authentication only
- **Dry Run Mode**: Test sync operations without making changes
- **Continuous Sync**: Run sync continuously with configurable intervals
- **Comprehensive Logging**: Detailed logging for troubleshooting
- **Multiple Item Types**: Supports login, secure notes, cards, and identity items
- **Custom Fields**: Syncs custom fields from Vaultwarden items
- **Custom Fields**: Syncs custom fields from Vaultwarden items
- **Multiline Secrets from Notes**: Define extra secret keys and multiline values directly in item notes


## Prerequisites

- .NET 9.0 Runtime
- Vaultwarden server (self-hosted or cloud)
- Kubernetes cluster access
- Bitwarden CLI (`bw`) installed and configured

## Installation

### Local Development

1. Clone the repository:
```bash
git clone <repository-url>
cd VaultwardenK8sSync
```

2. Build the application:
```bash
dotnet build
```

3. Configure the application (see Configuration section)

### Kubernetes Deployment

Remote apply via kustomize:
```bash
kubectl apply -k https://raw.githubusercontent.com/antoniolago/vaultwarden-kubernetes-secrets/main/deploy
```
Then edit the `ConfigMap` and `Secret` to set your server URL and credentials, or fork and create an overlay that pins the image/tag and values.

#### FluxCD (GitRepository + Kustomization)

Example to deploy this base with FluxCD:

```yaml
apiVersion: source.toolkit.fluxcd.io/v1
kind: GitRepository
metadata:
  name: vaultwarden-sync
  namespace: flux-system
spec:
  interval: 1m
  url: https://github.com/<org>/<repo>.git
  ref:
    branch: main
---
apiVersion: kustomize.toolkit.fluxcd.io/v1
kind: Kustomization
metadata:
  name: vaultwarden-sync
  namespace: flux-system
spec:
  interval: 5m
  prune: true
  targetNamespace: vaultwarden-sync
  sourceRef:
    kind: GitRepository
    name: vaultwarden-sync
  path: ./VaultwardenK8sSync/deploy
  # Optionally override image tag
  images:
    - name: harbor.lag0.com.br/library/vaultwarden-k8s-sync
      newTag: latest
```

Notes:
- Manage sensitive values by creating `Secret/vaultwarden-sync-secrets` in `vaultwarden-sync` namespace (or use SealedSecrets/SOPS).
- Non-sensitive values live in `ConfigMap/vaultwarden-sync-config` and can be customized via an overlay in your Git repo.

## Configuration

Create `.env` (see `env.example`).

### Environment Variables (.env file)

You can use a `.env` file to store sensitive configuration like passwords and API keys. Create a `.env` file in the project root:

```bash
# Copy the example file
cp env.example .env

# Edit the .env file with your actual values
```

Minimal `.env`:
```env
# Vaultwarden Configuration
VAULTWARDEN__SERVERURL=https://your-vaultwarden-server.com
VAULTWARDEN__MASTERPASSWORD=your-master-password
BW_CLIENTID=your-client-id
BW_CLIENTSECRET=your-client-secret
VAULTWARDEN__ORGANIZATIONID=optional-org-id
VAULTWARDEN__ORGANIZATIONNAME=optional-org-name
VAULTWARDEN__FOLDERID=optional-folder-id
VAULTWARDEN__FOLDERNAME=optional-folder-name
VAULTWARDEN__COLLECTIONID=optional-collection-id
VAULTWARDEN__COLLECTIONNAME=optional-collection-name

# Kubernetes (optional)
KUBERNETES__KUBECONFIGPATH=
KUBERNETES__CONTEXT=
KUBERNETES__DEFAULTNAMESPACE=default
KUBERNETES__INCLUSTER=false

# Sync
SYNC__NAMESPACETAG=#namespaces:
SYNC__DRYRUN=false
SYNC__DELETEORPHANS=true
SYNC__SECRETPREFIX=vaultwarden-
SYNC__SYNCINTERVALSECONDS=3600
SYNC__CONTINUOUSSYNC=false
```

Note: `SYNC__SYNCINTERVALSECONDS` is seconds:
- 60 = 1 minute
- 300 = 5 minutes  
- 600 = 10 minutes
- 3600 = 1 hour (default)
- 86400 = 24 hours

Add `.env` to `.gitignore`.

### Vaultwarden Configuration

Required:
```bash
VAULTWARDEN__SERVERURL=https://your-vaultwarden-server.com
VAULTWARDEN__MASTERPASSWORD=your-master-password
BW_CLIENTID=your-client-id
BW_CLIENTSECRET=your-client-secret
VAULTWARDEN__ORGANIZATIONID=optional-org-id
VAULTWARDEN__ORGANIZATIONNAME=optional-org-name
```

Auth: API key (`BW_CLIENTID`/`BW_CLIENTSECRET`) + `VAULTWARDEN__MASTERPASSWORD`.

Organization scoping (optional): set `VAULTWARDEN__ORGANIZATIONID` or `VAULTWARDEN__ORGANIZATIONNAME`.

Folder/Collection filters (optional): `VAULTWARDEN__FOLDERID/FOLDERNAME`, `VAULTWARDEN__COLLECTIONID/COLLECTIONNAME`.

### Kubernetes Configuration

Vars:
```bash
KUBERNETES__KUBECONFIGPATH=/path/to/kubeconfig
KUBERNETES__CONTEXT=your-context
KUBERNETES__DEFAULTNAMESPACE=default
KUBERNETES__INCLUSTER=false
```

**Connection Options:**
- **Local Development**: Use `KUBERNETES__KUBECONFIGPATH` and `KUBERNETES__CONTEXT` for local kubeconfig
- **In-Cluster**: Set `KUBERNETES__INCLUSTER=true` when running inside a Kubernetes pod

### Sync Configuration

**Environment Variables:**
```bash
SYNC__NAMESPACETAG=#namespaces:
SYNC__DRYRUN=false
SYNC__DELETEORPHANS=true
SYNC__SECRETPREFIX=vaultwarden-
SYNC__SYNCINTERVALSECONDS=3600
SYNC__CONTINUOUSSYNC=false
# Custom field name overrides (optional)
SYNC__FIELD__NAMESPACES=namespaces
SYNC__FIELD__SECRETNAME=secret-name
SYNC__FIELD__SECRETKEYPASSWORD=secret-key-password
SYNC__FIELD__SECRETKEYUSERNAME=secret-key-username
SYNC__FIELD__SECRETKEY=secret-key
```

## Usage

### Basic Commands

#### Commands
```bash
# Perform full sync
dotnet run sync

# Perform continuous sync (runs every SYNC__SYNCINTERVALSECONDS)
SYNC__CONTINUOUSSYNC=true dotnet run sync

# Sync specific namespace
dotnet run sync-namespace production

# Clean up orphaned secrets
dotnet run cleanup

# List items with namespace tags
dotnet run list

# Export a secret as YAML with proper multiline formatting
dotnet run export my-secret
dotnet run export my-secret production
```

#### General Commands
```bash
# Validate configuration
dotnet run config

# Show help
dotnet run help
```

### Namespace
Notes:
```
#namespaces:your-namespace-name
```
Custom field:
- `namespaces=staging,production` (comma-separated)

**Examples:**
```
# Sync to multiple namespaces via notes
#namespaces:staging,production,development

# Or use a custom field instead of notes:
# field name: namespaces
# field value: staging,production,development

# This will work too
#namespaces:ns1,ns2,ns3
#namespaces:ns4
```

### Secret name
Notes:
```
#secret-name:your-secret-name
```
Custom field:
- `secret-name=my-secret`

**Example:**
```
Database credentials for production environment

#namespaces:production
#secret-name:oracle-secrets
#secret-key:db_password

# Or using custom fields instead of notes for secret-name and namespaces:
# field name: namespaces       value: production
# field name: secret-name      value: oracle-secrets
# field name: secret-key       value: db_password   # legacy password key, optional
```

**Note:** If no secret name is specified, the tool will use the item name.

### Keys in the secret
Password key:
Notes:
```
#secret-key-password:your-password-key
```
Custom field:
- `secret-key-password=db_password`
Username key:
Notes:
```
#secret-key-username:your-username-key
```
Custom field:
- `secret-key-username=db_user`

Legacy (password key):
Notes:
```
#secret-key:your-key-name
```
Custom field: `secret-key=your-key-name`

**Examples:**
```
# Separate username and password keys
Database credentials for production environment
#namespaces:production
#secret-name:oracle-secrets
#secret-key-password:db_password
#secret-key-username:db_user

# Legacy format (password only)
Database credentials for production environment
#namespaces:production
#secret-name:oracle-secrets
#secret-key:db_password

# Same using custom fields (no note tags required):
# fields:
#   namespaces=production
#   secret-name=oracle-secrets
#   secret-key-password=db_password
#   secret-key-username=db_user
```

Notes:
- Default password key: sanitized item name
- Default username key: `{sanitized_item_name}_username` (only if username exists)

### Multiple Items with Same Secret Name

When multiple Vaultwarden items point to the same secret name (via `#secret-name:` or default naming), they will be combined into a single Kubernetes secret. Each item's data will be stored with its own key:

- If a custom `#secret-key:` is specified, that key will be used
- If no custom key is specified, the sanitized item name will be used as the key
- If multiple items have the same key, the last item processed will overwrite previous values

### Secret naming
- If `secret-name` is set: use it
- Else: sanitized item name

### Behavior
- Labels set: `app.kubernetes.io/managed-by|created-by: vaultwarden-k8s-sync`
- Orphan cleanup only touches labeled secrets
- Hash-based change detection updates when content/metadata changes

### Secret Data Structure

The tool creates secrets with the following structure:

**Key:** The sanitized item name (e.g., `database_user`, `api_key`, `ssh_private_key`)
**Value:** The password, SSH key, or credential value from the item

**Example:**
For a Vaultwarden item named "Database User" with password "mypassword123":
- Key: `database_user`
- Value: `mypassword123`

### Multiline & extra keys via Notes
```
#kv:API_URL=https://api.example.com
```secret:private_key
-----BEGIN PRIVATE KEY-----
...
-----END PRIVATE KEY-----
```
```

**Example Export Output:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: ssh-keys
  namespace: production
type: Opaque
data:
  private_key: |
    -----BEGIN PRIVATE KEY-----
    MIIEvgIBADANBgkqhkiG9wsaFASCBKwggSk34AgEA123AoIBQC7VJTUt9Us8cKB
    ...
    -----END PRIVATE KEY-----
  username: admin
```

Use `dotnet run export` for readable YAML (literal blocks).

### Adding Extra Keys and Multiline Values via Notes

You can define additional secret entries directly in the Vaultwarden item's notes using either inline key/value or fenced blocks:

1. Inline key/value (single line):
```
#kv:API_URL=https://api.example.com
#kv:API_KEY=sk-123...
```

2. Fenced block (for multiline values):
```
```secret:private_key
-----BEGIN PRIVATE KEY-----
...
-----END PRIVATE KEY-----
```
```

Notes:
- Keys from notes are combined with the regular username/password and custom fields
- Keys are sanitized to be valid Kubernetes secret keys (lowercase, underscores)
- Last writer wins if the same key appears multiple times

Secure notes: if no password exists, the note body (without metadata lines/secret blocks) is stored under the resolved password key.

## Examples

### Example 1: Database Credentials

**Vaultwarden Item:**
- Name: "Production Database"
- Description: "Production database credentials\n#namespaces:production"
- Username: "dbuser"
- Password: "securepassword123"

Resulting Secret:
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: vaultwarden-production-database
  namespace: production
  labels:
    app.kubernetes.io/managed-by: vaultwarden-k8s-sync
type: Opaque
data:
  production_database: c2VjdXJlcGFzc3dvcmQxMjM=
```

### Example 2: API Keys

**Vaultwarden Item:**
- Name: "External API"
- Description: "External service API keys\n#namespaces:staging"
- Custom Fields:
  - API_KEY: "sk-1234567890abcdef"
  - API_URL: "https://api.external.com"

Resulting Secret:
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: external-api
  namespace: staging
  labels:
    app.kubernetes.io/managed-by: vaultwarden-k8s-sync
type: Opaque
data:
  external_api: c2stMTIzNDU2Nzg5MGFiY2RlZg==
```

### Example 3: Custom Secret Names

**Vaultwarden Item:**
- Name: "Database User"
- Notes: "Production database user\n#namespaces:production\n#secret-name:oracle-secrets\n#secret-key-password:db_password\n#secret-key-username:db_user"
- Username: "dbuser"
- Password: "securepassword123"

Resulting Secret:
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: oracle-secrets
  namespace: production
  labels:
    app.kubernetes.io/managed-by: vaultwarden-k8s-sync
type: Opaque
data:
  db_user: ZGJ1c2Vy
  db_password: c2VjdXJlcGFzc3dvcmQxMjM=
```

### Example 5: Multiple Items with Same Secret Name

**Vaultwarden Item 1:**
- Name: "Database User"
- Notes: "Production database user\n#namespaces:production\n#secret-name:oracle-secrets\n#secret-key-password:user_password\n#secret-key-username:user_name"
- Username: "dbuser"
- Password: "userpassword123"

**Vaultwarden Item 2:**
- Name: "Database Admin"
- Notes: "Production database admin\n#namespaces:production\n#secret-name:oracle-secrets\n#secret-key-password:admin_password\n#secret-key-username:admin_name"
- Username: "dbadmin"
- Password: "adminpassword456"

**Resulting Kubernetes Secret:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: oracle-secrets
  namespace: production
  labels:
    app.kubernetes.io/managed-by: vaultwarden-k8s-sync
type: Opaque
data:
  user_name: ZGJ1c2Vy
  user_password: dXNlcnBhc3N3b3JkMTIz
  admin_name: ZGJhZG1pbg==
  admin_password: YWRtaW5wYXNzd29yZDQ1Ng==
```

Note: both items combine into the same secret.

## Security Considerations

1. **Network Security**: Ensure secure communication between the sync tool and Vaultwarden/Kubernetes.
2. **Access Control**: Limit the sync tool's access to only necessary collections/folders, namespaces and secrets.
3. **Safe Secret Management**: The application only touches secrets with its specific labels (`app.kubernetes.io/managed-by: vaultwarden-k8s-sync`), preventing accidental deletion of secrets created by other means.

## Troubleshooting (quick)

1) Auth failed? Check server URL, API key, master password, `bw` installed.
2) K8s failed? Check kubeconfig/context/permissions.
3) Item not selected? Add `#namespaces:` or `namespaces` field.
4) No update? Tool runs `bw sync` before listing; confirm note change wasn’t only metadata.

### Dry run
Set `SYNC__DRYRUN=true`.

### Continuous
Set `SYNC__CONTINUOUSSYNC=true` and `SYNC__SYNCINTERVALSECONDS=300` (for 5m). Ctrl+C to stop.

## Development

### Building from Source

```bash
git clone <repository-url>
cd VaultwardenK8sSync
dotnet restore
dotnet build
dotnet test
```

### Running Tests

```bash
dotnet test
```

### Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For issues and questions:
1. Check the troubleshooting section
2. Review the logs
3. Create an issue in the repository 