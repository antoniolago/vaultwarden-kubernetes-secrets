# Vaultwarden Kubernetes Secrets Sync

A .NET 9 console application that synchronizes Vaultwarden vault entries to Kubernetes secrets. The tool automatically syncs secrets based on namespace tags in Vaultwarden item descriptions and maintains consistency by cleaning up orphaned secrets.

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
kubectl apply -k https://raw.githubusercontent.com/antoniolago/vaultwarden-kubernetes-secrets/main/VaultwardenK8sSync/deploy
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
SYNC__NAMESPACETAG=#namespaces:
SYNC__DRYRUN=false
SYNC__DELETEORPHANS=true
SYNC__SECRETPREFIX=vaultwarden-
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
SYNC__NAMESPACETAG=#namespaces:
SYNC__DRYRUN=false
SYNC__DELETEORPHANS=true
SYNC__SECRETPREFIX=vaultwarden-
SYNC__SYNCINTERVALSECONDS=3600
SYNC__CONTINUOUSSYNC=false
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

### Namespace Tagging

To sync a Vaultwarden item to Kubernetes namespaces, add the following to the item's notes:

```
#namespaces:your-namespace-name
```

**Examples:**
```
# Sync to multiple namespaces
#namespaces:staging,production,development

# This will work too
#namespaces:ns1,ns2,ns3
#namespaces:ns4
```

### Secret Name Tagging

To specify a custom name for the Kubernetes secret, add the following to the item's notes:

```
#secret-name:your-secret-name
```

**Example:**
```
Database credentials for production environment

#namespaces:production
#secret-name:oracle-secrets
#secret-key:db_password
```

**Note:** If no secret name is specified, the tool will use the item name.

### Secret Key Tagging

The application supports separate key tags for username and password data:

#### Password Key Tagging
To specify a custom key name for the password within the Kubernetes secret, add the following to the item's notes:

```
#secret-key-password:your-password-key
```

#### Username Key Tagging
To specify a custom key name for the username within the Kubernetes secret, add the following to the item's notes:

```
#secret-key-username:your-username-key
```

#### Legacy Key Tagging (Backward Compatible)
The old `#secret-key:` tag is still supported for backward compatibility and applies to the password:

```
#secret-key:your-key-name
```

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
```

**Note:** 
- If no password key is specified, the tool will use the sanitized item name as the key
- If no username key is specified but a username is available, the tool will use `{sanitized_item_name}_username` as the key
- Username data is only included if the Vaultwarden item has login information or custom fields with username data

### Multiple Items with Same Secret Name

When multiple Vaultwarden items point to the same secret name (via `#secret-name:` or default naming), they will be combined into a single Kubernetes secret. Each item's data will be stored with its own key:

- If a custom `#secret-key:` is specified, that key will be used
- If no custom key is specified, the sanitized item name will be used as the key
- If multiple items have the same key, the last item processed will overwrite previous values

### Secret Naming

Secrets are named using one of the following patterns:

1. **Custom Secret Name**: If `#secret-name:` is specified in the item's notes, that name is used directly
2. **Default Pattern**: `{SecretPrefix}{SanitizedItemName}`

By default, an item named "Database Credentials" becomes a secret named `database-credentials` unless a custom `#secret-name:` is specified.

### Secret Management and Security

**Application Labels**: All secrets created by this application are automatically labeled with:
- `app.kubernetes.io/managed-by: vaultwarden-k8s-sync`
- `app.kubernetes.io/created-by: vaultwarden-k8s-sync`

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

## Examples

### Example 1: Database Credentials

**Vaultwarden Item:**
- Name: "Production Database"
- Description: "Production database credentials\n#namespaces:production"
- Username: "dbuser"
- Password: "securepassword123"

**Resulting Kubernetes Secret:**
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

**Resulting Kubernetes Secret:**
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

**Note:** Both items are combined into a single secret named `oracle-secrets`, with each item's username and password data stored under separate keys.

## Security Considerations

1. **Network Security**: Ensure secure communication between the sync tool and Vaultwarden/Kubernetes.
2. **Access Control**: Limit the sync tool's access to only necessary collections/folders, namespaces and secrets.
3. **Safe Secret Management**: The application only touches secrets with its specific labels (`app.kubernetes.io/managed-by: vaultwarden-k8s-sync`), preventing accidental deletion of secrets created by other means.

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