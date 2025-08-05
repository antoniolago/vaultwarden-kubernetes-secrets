# Vaultwarden Kubernetes Secrets Sync

A .NET 9 console application that synchronizes Vaultwarden vault entries to Kubernetes secrets. The tool automatically syncs secrets based on namespace tags in Vaultwarden item descriptions and maintains consistency by cleaning up orphaned secrets.

## Features

- **Automatic Sync**: Syncs Vaultwarden items to Kubernetes secrets based on namespace tags
- **Consistency Management**: Automatically deletes secrets that no longer exist in Vaultwarden
- **Flexible Configuration**: Supports both API key and password authentication
- **Dry Run Mode**: Test sync operations without making changes
- **Comprehensive Logging**: Detailed logging for troubleshooting
- **Multiple Item Types**: Supports login, secure notes, cards, and identity items
- **Custom Fields**: Syncs custom fields from Vaultwarden items
- **Dual Implementation**: Supports both bw CLI (V1) and VwConnector library (V2) for Vaultwarden integration

## Prerequisites

- .NET 9.0 Runtime
- Vaultwarden server (self-hosted or cloud)
- Kubernetes cluster access
- **For V1 (bw CLI)**: Bitwarden CLI (`bw`) installed and configured
- **For V2 (VwConnector)**: No additional CLI tools required

## Installation

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

## Configuration

The application reads configuration from multiple sources in the following order:
1. `.env` file (loaded first)
2. `appsettings.json`
3. `appsettings.Development.json`
4. Environment variables
5. Command line arguments

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
VAULTWARDEN__CLIENTID=your-client-id
VAULTWARDEN__CLIENTSECRET=your-client-secret
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
SYNC__SYNCINTERVALMINUTES=60
SYNC__CONTINUOUSSYNC=false
```

**Important**: Add `.env` to your `.gitignore` file to prevent committing sensitive information to version control.

### Vaultwarden Configuration

```json
{
  "Vaultwarden": {
    "ServerUrl": "https://your-vaultwarden-server.com",
    "Email": "your-email@example.com",
    "MasterPassword": "your-master-password",
    "ApiKey": "your-api-key",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "UseApiKey": false
  }
}
```

**Authentication Options:**
- **Password Authentication**: Set `UseApiKey` to `false` and provide `Email` and `MasterPassword`
- **API Key Authentication**: Set `UseApiKey` to `true` and provide `ApiKey`, `ClientId`, and `ClientSecret`

### Kubernetes Configuration

```json
{
  "Kubernetes": {
    "KubeConfigPath": "/path/to/kubeconfig",
    "Context": "your-context",
    "DefaultNamespace": "default",
    "InCluster": false
  }
}
```

**Connection Options:**
- **Local Development**: Use `KubeConfigPath` and `Context` for local kubeconfig
- **In-Cluster**: Set `InCluster` to `true` when running inside a Kubernetes pod

### Sync Configuration

```json
{
  "Sync": {
    "NamespaceTag": "#namespace:",
    "DryRun": false,
    "DeleteOrphans": true,
    "SecretPrefix": "vaultwarden-",
    "SyncIntervalMinutes": 60,
    "ContinuousSync": false
  }
}
```

## Usage

### Basic Commands

#### V1 Commands (bw CLI)
```bash
# Perform full sync
dotnet run sync

# Sync specific namespace
dotnet run sync-namespace production

# Clean up orphaned secrets
dotnet run cleanup

# List items with namespace tags
dotnet run list
```

#### V2 Commands (VwConnector - No bw CLI required)
```bash
# Perform full sync
dotnet run v2-sync

# Sync specific namespace
dotnet run v2-sync-namespace production

# Clean up orphaned secrets
dotnet run v2-cleanup

# List items with namespace tags
dotnet run v2-list
```

#### General Commands
```bash
# Validate configuration
dotnet run config

# Show help
dotnet run help
```

### Namespace Tagging

To sync a Vaultwarden item to a Kubernetes namespace, add the following to the item's description:

```
#namespace:your-namespace-name
```

**Example:**
```
Database credentials for production environment
#namespace:production
```

### Secret Naming

Secrets are automatically named using the pattern:
```
{SecretPrefix}{SanitizedItemName}
```

Default prefix is `vaultwarden-`, so an item named "Database Credentials" becomes `vaultwarden-database-credentials`.

### Secret Data Structure

The tool extracts the following data from Vaultwarden items:

**Login Items:**
- `username` - Username
- `password` - Password
- `url` - URL
- `notes` - Notes
- `login_username` - Login-specific username
- `login_password` - Login-specific password
- `totp` - TOTP secret
- `uri_0`, `uri_1`, etc. - URIs

**Custom Fields:**
- All custom fields are included with sanitized names

**Card Items:**
- `cardholder_name` - Cardholder name
- `card_brand` - Card brand
- `card_number` - Card number
- `card_exp_month` - Expiration month
- `card_exp_year` - Expiration year
- `card_code` - Security code

**Identity Items:**
- `title`, `first_name`, `last_name`, etc. - Identity fields

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
  username: ZGJ1c2Vy
  password: c2VjdXJlcGFzc3dvcmQxMjM=
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
  api_key: c2stMTIzNDU2Nzg5MGFiY2RlZg==
  api_url: aHR0cHM6Ly9hcGkuZXh0ZXJuYWwuY29t
```

## Implementation Versions

### V1 (bw CLI)
- Uses Bitwarden CLI (`bw`) for Vaultwarden communication
- Requires `bw` to be installed and configured
- More mature and stable implementation
- Supports API key and password authentication

### V2 (VwConnector)
- Uses VwConnector library for direct Vaultwarden API communication
- No external CLI tools required
- More efficient and faster
- Currently supports password authentication only
- Automatically filters out deleted items

## Security Considerations

1. **Credentials Storage**: Never store sensitive credentials in configuration files. Use environment variables or secure secret management.
2. **API Keys**: Use API key authentication when possible for better security (V1 only).
3. **Network Security**: Ensure secure communication between the sync tool and Vaultwarden/Kubernetes.
4. **Access Control**: Limit the sync tool's access to only necessary namespaces and secrets.

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

### Logging

The application provides detailed logging at different levels:
- `Information`: General sync operations
- `Warning`: Non-critical issues
- `Error`: Sync failures and errors

### Dry Run Mode

Use dry run mode to test sync operations without making changes:
```json
{
  "Sync": {
    "DryRun": true
  }
}
```

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