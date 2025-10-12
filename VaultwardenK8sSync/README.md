# Vaultwarden Kubernetes Secrets Sync

A .NET 9 console application that synchronizes Vaultwarden vault entries to Kubernetes secrets. The tool automatically syncs secrets based on namespace custom fields in Vaultwarden items and maintains consistency by cleaning up orphaned secrets.

## Features

- **Automatic Sync**: Syncs Vaultwarden items to Kubernetes secrets based on namespace custom fields
- **Consistency Management**: Automatically deletes secrets that no longer exist in Vaultwarden
- **Authentication**: API key authentication only
- **Dry Run Mode**: Test sync operations without making changes
- **Continuous Sync**: Run sync continuously with configurable intervals
- **Comprehensive Logging**: Detailed logging for troubleshooting
- **Multiple Item Types**: Supports login, secure notes, cards, and identity items
- **Custom Fields**: Syncs custom fields from Vaultwarden items



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
  name: vaultwarden-kubernetes-secrets
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
  name: vaultwarden-kubernetes-secrets
  namespace: flux-system
spec:
  interval: 5m
  prune: true
  targetNamespace: vaultwarden-kubernetes-secrets
  sourceRef:
    kind: GitRepository
    name: vaultwarden-kubernetes-secrets
  path: ./VaultwardenK8sSync/deploy
  # Optionally override image tag
  images:
    - name: harbor.lag0.com.br/library/vaultwarden-kubernetes-secrets
      newTag: latest
```

Notes:
- Manage sensitive values by creating `Secret/vaultwarden-kubernetes-secrets` in `vaultwarden-kubernetes-secrets` namespace (or use SealedSecrets/SOPS).
- Non-sensitive values live in `ConfigMap/vaultwarden-kubernetes-secrets-config` and can be customized via an overlay in your Git repo.

## Configuration

The application reads configuration from environment variables in the following order:
1. `.env` file (loaded first)
2. System environment variables
3. Launch configuration environment variables

### Environment Variables (.env file)

You can use a `.env` file to store sensitive configuration like passwords and API keys. Create a `.env` file in the project root:

```bash
# Copy the example file
cp env.example .env

# Edit the .env file with your actual values
```

Example `.env` file:
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

# Kubernetes Configuration
KUBERNETES__KUBECONFIGPATH=
KUBERNETES__CONTEXT=
KUBERNETES__DEFAULTNAMESPACE=default
KUBERNETES__INCLUSTER=false

# Sync Configuration

SYNC__DRYRUN=false
SYNC__DELETEORPHANS=true
SYNC__SECRETPREFIX=
SYNC__SYNCINTERVALSECONDS=3600
SYNC__CONTINUOUSSYNC=false
```

**Note:** `SYNC__SYNCINTERVALSECONDS` is specified in seconds. Common values:
- 60 = 1 minute
- 300 = 5 minutes  
- 600 = 10 minutes
- 3600 = 1 hour (default)
- 86400 = 24 hours

**Important**: Add `.env` to your `.gitignore` file to prevent committing sensitive information to version control.

## Configure items in Bitwarden (GUI)

Use custom fields in the Bitwarden web/desktop UI to control how items sync to Kubernetes Secrets.

- Namespaces (required for syncing)
  - Custom field: name `namespaces`, value `staging,production`

- Secret name
  - Custom field: name `secret-name`, value `my-secret`
  - Default when omitted: sanitized item name

- Primary value key (password/content)
  - Custom field: name `secret-key-password` (or `secret-key`), value `db_password`
  - Default when omitted: sanitized item name

- Username key (when username exists)
  - Custom field: name `secret-key-username`, value `db_user`
  - Default when omitted: `<sanitized_item_name>-username`

- SSH Key items (Bitwarden type "SSH Key")
  - Private key becomes the primary value (under your chosen password key name or default)
  - If present, `publicKey` and `fingerprint` are added automatically as `<item>_public_key` and `<item>_fingerprint`

Defaults and overrides
- Default field names are:
  - `namespaces`, `secret-name`, `secret-key-password` (synonym: `secret-key`), `secret-key-username`
- You can override these via environment variables (or Helm values):
  - `SYNC__FIELD__NAMESPACES`, `SYNC__FIELD__SECRETNAME`, `SYNC__FIELD__SECRETKEYPASSWORD`, `SYNC__FIELD__SECRETKEYUSERNAME`

Quick examples
- Database login (Login item)
  - Custom fields:
    - `namespaces`: `production`
    - `secret-name`: `oracle-secrets`
    - `secret-key-password`: `db_password`
    - `secret-key-username`: `db_user`
  - Username/password come from the Login item; two keys `db_user` and `db_password` are written

- API token only (Secure Note)
  - Custom fields:
    - `namespaces`: `staging`
    - `secret-name`: `service-x`
    - `API_URL`: `https://api.example.com`
  - Note body becomes the main value; custom field `API_URL` is also written to the secret

- SSH key (SSH Key item)
  - Custom fields:
    - `namespaces`: `prod,staging`
    - `secret-name`: `deploy-ssh`
    - `secret-key-password`: `private_key`
  - Private key stored under `private_key`; if available, `*-public-key` and `*-fingerprint` are added

Common mistakes
- Missing `namespaces` custom field → items won't sync
- Target namespace doesn't exist → create it first (or enable namespace creation in the chart)
- RBAC not cluster-wide when syncing to multiple namespaces → set `rbac.clusterWide=true` in Helm

### Vaultwarden Configuration

**Environment Variables:**
```bash
VAULTWARDEN__SERVERURL=https://your-vaultwarden-server.com
VAULTWARDEN__MASTERPASSWORD=your-master-password
BW_CLIENTID=your-client-id
BW_CLIENTSECRET=your-client-secret
VAULTWARDEN__ORGANIZATIONID=optional-org-id
VAULTWARDEN__ORGANIZATIONNAME=optional-org-name
```

**Authentication:** API key only (default and only mode)
- Set `BW_CLIENTID` and `BW_CLIENTSECRET`
- Set `VAULTWARDEN__MASTERPASSWORD` to unlock the vault after API key login

**Organization Scoping (Optional):**
- To sync only items that belong to a specific organization, set one of:
  - `VAULTWARDEN__ORGANIZATIONID`
  - `VAULTWARDEN__ORGANIZATIONNAME`
  
If an organization is specified, the tool will fetch all items and then filter to that organization.

**Folder/Collection Filters (Optional):**
- Restrict synced items further by folder or collection:
  - `VAULTWARDEN__FOLDERID` or `VAULTWARDEN__FOLDERNAME`
  - `VAULTWARDEN__COLLECTIONID` or `VAULTWARDEN__COLLECTIONNAME`
The tool resolves names to IDs via `bw list folders` / `bw list collections` and filters items accordingly.

### Kubernetes Configuration

**Environment Variables:**
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
SYNC__DRYRUN=false
SYNC__DELETEORPHANS=true
SYNC__SECRETPREFIX=
SYNC__SYNCINTERVALSECONDS=3600
SYNC__CONTINUOUSSYNC=false
# Custom field names (optional overrides)
SYNC__FIELD__NAMESPACES=namespaces
SYNC__FIELD__SECRETNAME=secret-name
SYNC__FIELD__SECRETKEYPASSWORD=secret-key-password
SYNC__FIELD__SECRETKEYUSERNAME=secret-key-username
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

### Namespace Configuration

To sync a Vaultwarden item to Kubernetes namespaces, use custom fields.

Custom field:
- Add a custom field named `namespaces` with value like `staging,production` or a single namespace `production`.

**Examples:**
```
# Custom field configuration:
# field name: namespaces
# field value: staging,production,development

# Or single namespace:
# field name: namespaces
# field value: production
```

### Secret Name Configuration

You can set the Kubernetes secret name via a custom field.

Custom field:
- Add a custom field named `secret-name` with the desired secret name.

**Example:**
```
# Custom field configuration:
# field name: namespaces       value: production
# field name: secret-name      value: oracle-secrets
# field name: secret-key-password   value: db_password
```

**Note:** If no secret name is specified, the tool will use the item name.

### Secret Key Configuration

The application supports separate key configuration for username and password data via custom fields.

#### Password Key Configuration
To specify a custom key name for the password within the Kubernetes secret:

Custom field:
- Add a custom field named `secret-key-password` (or `secret-key`) with the desired key.

#### Username Key Configuration
To specify a custom key name for the username within the Kubernetes secret:

Custom field:
- Add a custom field named `secret-key-username` with the desired key.



**Examples:**
```
# Username and password keys using custom fields:
# fields:
#   namespaces=production
#   secret-name=oracle-secrets
#   secret-key-password=db_password
#   secret-key-username=db_user
```

**Note:** 
- If no password key is specified, the tool will use the sanitized item name as the key
- If no username key is specified but a username is available, the tool will use `{sanitized_item_name}-username` as the key
- Username data is only included if the Vaultwarden item has login information or custom fields with username data

### Multiple Items with Same Secret Name

When multiple Vaultwarden items point to the same secret name (via `secret-name` custom field or default naming), they will be combined into a single Kubernetes secret. Each item's data will be stored with its own key:

- If a custom `secret-key-password` is specified, that key will be used
- If no custom key is specified, the sanitized item name will be used as the key
- If multiple items have the same key, the last item processed will overwrite previous values

### Secret Naming

Secrets are named using one of the following patterns:

1. **Custom Secret Name**: If `secret-name` is specified in the item's custom fields, that name is used directly
2. **Default Pattern**: `{SecretPrefix}{SanitizedItemName}`

By default, an item named "Database Credentials" becomes a secret named `database-credentials` unless a custom `secret-name` custom field is specified.

### Secret Management and Security

**Application Labels**: All secrets created by this application are automatically labeled with:
- `app.kubernetes.io/managed-by: vaultwarden-kubernetes-secrets`
- `app.kubernetes.io/created-by: vaultwarden-kubernetes-secrets`

**Safe Cleanup**: The application only manages secrets with its specific labels, ensuring it never deletes secrets created by other applications. This prevents accidental deletion of secrets managed by other tools or applications.

**Change Detection**: The application uses SHA256 hashes to detect changes in Vaultwarden items. A hash is calculated from all relevant item fields (name, notes, password, login info, custom fields, etc.) and stored in the Kubernetes secret. This ensures that any change to the Vaultwarden item (including metadata changes like notes, username, or custom fields) will trigger a secret update, even if the final secret data remains the same.

### Secret Data Structure

The tool creates secrets with the following structure:

**Key:** The sanitized item name (e.g., `database_user`, `api_key`, `ssh_private_key`)
**Value:** The password, SSH key, or credential value from the item

**Example:**
For a Vaultwarden item named "Database User" with password "mypassword123":
- Key: `database_user`
- Value: `mypassword123`

### Multiline Secret Handling

The application properly handles multiline secrets (such as SSH keys, certificates, etc.) by:

1. **Storage**: Multiline values are stored correctly in Kubernetes secrets with all line breaks preserved
2. **Normalization**: Line endings are normalized to Unix-style (`\n`) for consistency across platforms
3. **Export**: The `export` command displays multiline values using YAML's literal block style (`|`) instead of quoted strings

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

**Note:** When using `kubectl get secret -o yaml`, multiline values may appear quoted. Use the application's `export` command for proper YAML formatting with literal block style.

### Adding Extra Keys via Custom Fields

You can define additional secret entries directly in the Vaultwarden item's custom fields:

Simply add custom fields with the desired key names and values:
```
# Example custom fields:
# API_URL: https://api.example.com
# API_KEY: sk-123...
# DATABASE_HOST: db.example.com
```

Notes:
- All custom fields (except metadata fields like `namespaces`, `secret-name`, etc.) are included in the secret
- Keys are sanitized to be valid Kubernetes secret keys (lowercase, underscores)
- Last writer wins if the same key appears multiple times

### Syncing Secure Notes Content

- If an item has no password or login password, the tool writes the note body as the primary value under the resolved password key (from `secret-key-password` or the sanitized item name).
- The full note body is included as-is in the secret data.
- To include specific content as separate keys, use custom fields as shown above.

## Examples

### Example 1: Database Credentials

**Vaultwarden Item:**
- Name: "Production Database"
- Description: "Production database credentials"
- Username: "dbuser"
- Password: "securepassword123"
- Custom Fields:
  - namespaces: "production"

**Resulting Kubernetes Secret:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: vaultwarden-production-database
  namespace: production
  labels:
    app.kubernetes.io/managed-by: vaultwarden-kubernetes-secrets
type: Opaque
data:
  production_database: c2VjdXJlcGFzc3dvcmQxMjM=
```

### Example 2: API Keys

**Vaultwarden Item:**
- Name: "External API"
- Description: "External service API keys"
- Custom Fields:
  - namespaces: "staging"
  - API_KEY: "sk-1234567890abcdef"
  - API_URL: "https://api.external.com"

**Resulting Kubernetes Secret:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: external-api
  namespace: staging
  labels:
    app.kubernetes.io/managed-by: vaultwarden-kubernetes-secrets
type: Opaque
data:
  external_api: c2stMTIzNDU2Nzg5MGFiY2RlZg==
```

### Example 3: Custom Secret Names

**Vaultwarden Item:**
- Name: "Database User"
- Notes: "Production database user"
- Username: "dbuser"
- Password: "securepassword123"
- Custom Fields:
  - namespaces: "production"
  - secret-name: "oracle-secrets"
  - secret-key-password: "db_password"
  - secret-key-username: "db_user"

**Resulting Kubernetes Secret:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: oracle-secrets
  namespace: production
  labels:
    app.kubernetes.io/managed-by: vaultwarden-kubernetes-secrets
type: Opaque
data:
  db_user: ZGJ1c2Vy
  db_password: c2VjdXJlcGFzc3dvcmQxMjM=
```

### Example 5: Multiple Items with Same Secret Name

**Vaultwarden Item 1:**
- Name: "Database User"
- Notes: "Production database user"
- Username: "dbuser"
- Password: "userpassword123"
- Custom Fields:
  - namespaces: "production"
  - secret-name: "oracle-secrets"
  - secret-key-password: "user_password"
  - secret-key-username: "user_name"

**Vaultwarden Item 2:**
- Name: "Database Admin"
- Notes: "Production database admin"
- Username: "dbadmin"
- Password: "adminpassword456"
- Custom Fields:
  - namespaces: "production"
  - secret-name: "oracle-secrets"
  - secret-key-password: "admin_password"
  - secret-key-username: "admin_name"

**Resulting Kubernetes Secret:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: oracle-secrets
  namespace: production
  labels:
    app.kubernetes.io/managed-by: vaultwarden-kubernetes-secrets
type: Opaque
data:
  user_name: ZGJ1c2Vy
  user_password: dXNlcnBhc3N3b3JkMTIz
  admin_name: ZGJhZG1pbg==
  admin_password: YWRtaW5wYXNzd29yZDQ1Ng==
```

**Note:** Both items are combined into a single secret named `oracle-secrets`, with each item's username and password data stored under separate keys.

## Security Considerations

1. **Network Security**: Ensure secure communication between the sync tool and Vaultwarden/Kubernetes.
2. **Access Control**: Limit the sync tool's access to only necessary collections/folders, namespaces and secrets.
3. **Safe Secret Management**: The application only touches secrets with its specific labels (`app.kubernetes.io/managed-by: vaultwarden-kubernetes-secrets`), preventing accidental deletion of secrets created by other means.

## Troubleshooting

### Common Issues

1. **Authentication Failed**
   - Verify Vaultwarden credentials
   - Check server URL
   - Ensure Bitwarden CLI is properly configured

2. **Kubernetes Connection Failed**
   - Verify kubeconfig path and context
   - Check cluster access permissions
   - Ensure namespace exists

3. **Sync Errors**
   - Check item descriptions for correct namespace tags
   - Verify secret naming doesn't conflict
   - Review logs for detailed error messages

4. **Changes Not Detected**
   - The application uses hash-based change detection for all item fields
   - Any change to name, notes, password, username, custom fields, or metadata will trigger an update
   - Check logs for "content changes" or "metadata changes" messages

### Logging

The application provides detailed logging at different levels:
- `Information`: General sync operations
- `Warning`: Non-critical issues
- `Error`: Sync failures and errors

### Dry Run Mode

Use dry run mode to test sync operations without making changes:

**Environment Variable:**
```bash
SYNC__DRYRUN=true
```

**Or in .env file:**
```env
SYNC__DRYRUN=true
```

### Continuous Sync Mode

Run the sync continuously with configurable intervals:

**Environment Variables:**
```bash
SYNC__CONTINUOUSSYNC=true
SYNC__SYNCINTERVALSECONDS=300  # 5 minutes
```

**Or in .env file:**
```env
SYNC__CONTINUOUSSYNC=true
SYNC__SYNCINTERVALSECONDS=300
```

**Usage:**
```bash
# Run continuous sync
dotnet run sync

# Stop with Ctrl+C (graceful shutdown)
```

**Features:**
- Runs sync every `SYNC__SYNCINTERVALSECONDS` seconds
- Graceful shutdown with Ctrl+C
- Continues running even if individual sync runs fail
- Detailed logging for each sync run

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

## Limitations

- **Multiline handling**:
  - Multiline values are best handled using Secure Note items with fenced `secret:` blocks in the note body.
  - Login/Card/Identity items support single-line custom fields; parsing fenced multiline blocks from their notes may not be consistent across all cases.
- **Organization API Key**:
  - Bitwarden CLI (`bw`) does not support Organization API Key authentication. Use user API key (`BW_CLIENTID`/`BW_CLIENTSECRET`) plus `VAULTWARDEN__MASTERPASSWORD` to unlock.
- **Attachments**:
  - File attachments are not synchronized. Only text values from passwords, usernames, notes, and custom fields are processed.
- **Secret types**:
  - Only `Opaque` Secrets are created. TLS/dockerconfig or other secret types are not generated.
- **Key sanitization**:
- Custom field names preserve case while ensuring valid Kubernetes secret key names. Default keys are generated from item names using sanitization rules. Collisions after sanitization may overwrite previous values; last writer wins when multiple items map to the same key.
- **Object size limits**:
  - Kubernetes Secrets must be < ~1MiB in total size. Very large notes or many combined keys in one secret can exceed this limit.
- **Name resolution**:
  - Organization/Folder/Collection name filters resolve the first matching name via `bw list`. Use IDs to avoid ambiguity.
- **Namespace requirements**:
  - Items must include a `namespaces` custom field. Namespaces must exist or be created via your deployment method (Helm chart offers optional creation).