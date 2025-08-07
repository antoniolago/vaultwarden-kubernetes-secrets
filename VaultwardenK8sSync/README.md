# Vaultwarden Kubernetes Secrets Sync

A .NET 9 console application that synchronizes Vaultwarden vault entries to Kubernetes secrets. The tool automatically syncs secrets based on namespace tags in Vaultwarden item descriptions and maintains consistency by cleaning up orphaned secrets.

## Features

- **Automatic Sync**: Syncs Vaultwarden items to Kubernetes secrets based on namespace tags
- **Consistency Management**: Automatically deletes secrets that no longer exist in Vaultwarden
- **Flexible Configuration**: Supports both API key and password authentication
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

For production deployment to Kubernetes, see the [deploy/README.md](deploy/README.md) for detailed instructions.

Quick deployment:
```bash
# Build and push the container image
cd deploy
./build.sh

# Deploy to Kubernetes
./deploy.sh
```

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
VAULTWARDEN__EMAIL=your-email@example.com
VAULTWARDEN__MASTERPASSWORD=your-master-password
VAULTWARDEN__APIKEY=your-api-key
BW_CLIENTID=your-client-id
BW_CLIENTSECRET=your-client-secret
VAULTWARDEN__USEAPIKEY=false

# Kubernetes Configuration
KUBERNETES__KUBECONFIGPATH=
KUBERNETES__CONTEXT=
KUBERNETES__DEFAULTNAMESPACE=default
KUBERNETES__INCLUSTER=false

# Sync Configuration
SYNC__NAMESPACETAG=#namespace:
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
VAULTWARDEN__EMAIL=your-email@example.com
VAULTWARDEN__MASTERPASSWORD=your-master-password
VAULTWARDEN__APIKEY=your-api-key
VAULTWARDEN__CLIENTID=your-client-id
VAULTWARDEN__CLIENTSECRET=your-client-secret
VAULTWARDEN__USEAPIKEY=false
```

**Authentication Options:**
- **Password Authentication**: Set `VAULTWARDEN__USEAPIKEY=false` and provide `VAULTWARDEN__EMAIL` and `VAULTWARDEN__MASTERPASSWORD`
- **API Key Authentication**: Set `VAULTWARDEN__USEAPIKEY=true` and provide `VAULTWARDEN__APIKEY`, `VAULTWARDEN__CLIENTID`, and `VAULTWARDEN__CLIENTSECRET`

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
SYNC__NAMESPACETAG=#namespace:
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
#namespace:your-namespace-name
```

**Examples:**
```
# Single namespace
Database credentials for production environment
#namespace:production

# Multiple namespaces (comma-separated)
Shared API keys for multiple environments
#namespace:staging,production,development

# Multiple namespace tags (each on separate line)
Global configuration
#namespace:ns1,ns2,ns3
#namespace:ns4
```

### Secret Name Tagging

To specify a custom name for the Kubernetes secret, add the following to the item's notes:

```
#secret-name:your-secret-name
```

**Example:**
```
Database credentials for production environment
#namespace:production
#secret-name:oracle-secrets
#secret-key:db_password
```

**Note:** If no secret name is specified, the tool will use the item name with the configured prefix.

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
#namespace:production
#secret-name:oracle-secrets
#secret-key-password:db_password
#secret-key-username:db_user

# Legacy format (password only)
Database credentials for production environment
#namespace:production
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

Default prefix is `vaultwarden-`, so an item named "Database Credentials" becomes `vaultwarden-database-credentials` unless a custom secret name is specified.

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
    MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQC7VJTUt9Us8cKB
    ...
    -----END PRIVATE KEY-----
  username: admin
```

**Note:** When using `kubectl get secret -o yaml`, multiline values may appear quoted. Use the application's `export` command for proper YAML formatting with literal block style.

## Examples

### Example 1: Database Credentials

**Vaultwarden Item:**
- Name: "Production Database"
- Description: "Production database credentials\n#namespace:production"
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
- Description: "External service API keys\n#namespace:staging"
- Custom Fields:
  - API_KEY: "sk-1234567890abcdef"
  - API_URL: "https://api.external.com"

**Resulting Kubernetes Secret:**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: vaultwarden-external-api
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
- Notes: "Production database user\n#namespace:production\n#secret-name:oracle-secrets\n#secret-key-password:db_password\n#secret-key-username:db_user"
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

**Note:** The secret name is `oracle-secrets` (from the `#secret-name:` tag) instead of the default `vaultwarden-database-user`.

### Example 4: Multiple Namespaces

**Vaultwarden Item:**
- Name: "Shared API Key"
- Notes: "API key shared across environments\n#namespace:staging,production,development"
- Custom Fields:
  - API_KEY: "sk-abcdef1234567890"
  - API_URL: "https://api.shared.com"

**Resulting Kubernetes Secrets:**
```yaml
# In staging namespace
apiVersion: v1
kind: Secret
metadata:
  name: vaultwarden-shared-api-key
  namespace: staging
  labels:
    app.kubernetes.io/managed-by: vaultwarden-k8s-sync
type: Opaque
data:
  shared_api_key: c2stYWJjZGVmMTIzNDU2Nzg5MA==

---
# In production namespace
apiVersion: v1
kind: Secret
metadata:
  name: vaultwarden-shared-api-key
  namespace: production
  labels:
    app.kubernetes.io/managed-by: vaultwarden-k8s-sync
type: Opaque
data:
  shared_api_key: c2stYWJjZGVmMTIzNDU2Nzg5MA==

---
# In development namespace
apiVersion: v1
kind: Secret
metadata:
  name: vaultwarden-shared-api-key
  namespace: development
  labels:
    app.kubernetes.io/managed-by: vaultwarden-k8s-sync
type: Opaque
data:
  shared_api_key: c2stYWJjZGVmMTIzNDU2Nzg5MA==
```

**Note:** The same secret is created in all three namespaces (staging, production, development) with identical content.

### Example 5: Multiple Items with Same Secret Name

**Vaultwarden Item 1:**
- Name: "Database User"
- Notes: "Production database user\n#namespace:production\n#secret-name:oracle-secrets\n#secret-key-password:user_password\n#secret-key-username:user_name"
- Username: "dbuser"
- Password: "userpassword123"

**Vaultwarden Item 2:**
- Name: "Database Admin"
- Notes: "Production database admin\n#namespace:production\n#secret-name:oracle-secrets\n#secret-key-password:admin_password\n#secret-key-username:admin_name"
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

1. **Credentials Storage**: Never store sensitive credentials in configuration files. Use environment variables or secure secret management.
2. **API Keys**: Use API key authentication when possible for better security.
3. **Network Security**: Ensure secure communication between the sync tool and Vaultwarden/Kubernetes.
4. **Access Control**: Limit the sync tool's access to only necessary namespaces and secrets.
5. **Safe Secret Management**: The application only manages secrets with its specific labels (`app.kubernetes.io/managed-by: vaultwarden-k8s-sync`), preventing accidental deletion of secrets created by other applications.

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