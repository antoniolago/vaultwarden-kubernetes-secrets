[![Artifact Hub](https://img.shields.io/endpoint?url=https://artifacthub.io/badge/repository/vaultwarden-kubernetes-secrets)](https://artifacthub.io/packages/search?repo=vaultwarden-kubernetes-secrets) [![GitHub](https://img.shields.io/badge/github-%23121011.svg?style=for-the-badge&logo=github&logoColor=white)](https://github.com/antoniolago/vaultwarden-kubernetes-secrets)
# Vaultwarden Kubernetes Secrets Sync

This software leverages [bw-cli](https://bitwarden.com/help/cli/) to sync [Vaultwarden](https://github.com/dani-garcia/vaultwarden) items to Kubernetes Secrets.

⚠️ **WARNING: This application is not yet stable and may have significant CPU usage in high sync volume scenarios (needs more testing, you can help!).

## Install (Helm)

Recommended:

```bash
# Required inputs
NAMESPACE="vaultwarden-sync"      # Target namespace
SERVER_URL="https://your-vaultwarden-server.com"
BW_CLIENTID="<your_user_client_id>"
BW_CLIENTSECRET="<your_user_client_secret>"
MASTER_PASSWORD="<your_master_password>"

# Create namespace and the Secret with required credentials
kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f -
kubectl create secret generic vaultwarden-sync-secrets -n "$NAMESPACE" \
  --from-literal=BW_CLIENTID="$BW_CLIENTID" \
  --from-literal=BW_CLIENTSECRET="$BW_CLIENTSECRET" \
  --from-literal=VAULTWARDEN__MASTERPASSWORD="$MASTER_PASSWORD" \
  --dry-run=client -o yaml | kubectl apply -f -

# Install/upgrade via Helm OCI
helm upgrade -i vaultwarden-sync oci://harbor.lag0.com.br/charts/vaultwarden-k8s-sync \
  --version "$CHART_VERSION" \
  --namespace "$NAMESPACE" --create-namespace \
  --set env.config.VAULTWARDEN__SERVERURL="$SERVER_URL" \
  --set image.tag="$CHART_VERSION"
```

Notes:
- The chart references Secret `vaultwarden-sync-secrets` for sensitive values; the commands above create it.
- Set optional filters (Org/Folder/Collection) via `--set env.config.VAULTWARDEN__ORGANIZATIONID=...` etc.
- Security tips:
    Create a Vaultwarden user just for this purpose
    Use filter to a specific Collection inside an Organization

## How to use it

- Create an item in Vaultwarden: Login, Secure Note, or SSH Key
- Add target namespaces (required) via custom field:
  - Custom field: name `namespaces`, value `staging,production`
- Optional: set the Kubernetes Secret name via custom field:
  - Custom field: name `secret-name`, value `my-secret`
  - Default when omitted: sanitized item name
- Optional: choose keys for values written to the Secret via custom fields:
  - Password/content key: custom field name `secret-key-password`, value `db_password`
  - Username key: custom field name `secret-key-username`, value `db_user`
  - Defaults: password key = sanitized item name; username key = `<sanitized_item_name>_username`

- Optional:
  - All the custom fields you add (that are not the ones used by this app for configuration) will also be synced to the secret, if you want a field to not be synced, use custom field "ignore-field" with the fields you want to ignore as values separated by comma.
- Save the item. The sync job will:
  - Create/update one Secret per target namespace
  - Purge old secrets (only the ones created by the sync app)
  - Merge multiple items pointing to the same `secret-name` into one Secret (last writer wins on key conflicts)
  - For SSH Key items, store the private key under the password key; if present, also add `<item>_public_key` and `<item>_fingerprint`

## Quick examples

### Example 1 - default fields
Item:
 
<img width="406" height="454" alt="image" src="https://github.com/user-attachments/assets/26fa4b39-3a82-435e-bd62-14c9cbd6ee0f" />

Will result in:

<img width="1126" height="76" alt="image" src="https://github.com/user-attachments/assets/bf331363-8a61-4deb-b4e7-363bb0d1f599" />

### Example 2 - custom fields
Item:

<img width="578" height="622" alt="image" src="https://github.com/user-attachments/assets/3da6e6ba-b169-4910-acbf-31c114a52796" />

Will result in:

<img width="1179" height="69" alt="image" src="https://github.com/user-attachments/assets/5aef594c-5308-4e9f-8a8e-4075165daaa8" />

## More docs

For detailed app configuration and usage, see `VaultwardenK8sSync/README.md`.

## Limitations

- **Multiline support**: Multiline values are reliably supported when the item type is a Secure Note. For Login/Card/Identity items, custom fields are single-line only. If you need multiline content, use a Note item.
- **Organization API Key**: Bitwarden CLI (`bw`) does not support logging in with an Organization API Key. Only user API keys (`BW_CLIENTID`/`BW_CLIENTSECRET`) are supported. Ensure that user has the required access to the organization/collections.
- **Attachments**: File attachments are not synchronized. Only text-based fields (passwords, usernames, notes, custom fields) are processed.
- **Secret type**: Only `Opaque` Kubernetes Secrets are produced. TLS or other special secret types are not generated.
- **Key sanitization and collisions**: Secret keys are sanitized (lowercased and normalized). Different source keys may collide after sanitization; in collisions, the last writer wins.
- **Kubernetes size limits**: A single Secret must remain under the Kubernetes object size limit (~1 MiB). Very large note content or many combined keys under the same secret may cause an update failure.
- **Name-based filters**: When filtering by organization/folder/collection names, the first matching name is used. Prefer IDs to avoid ambiguity.
- **Namespace custom field required**: Items without an explicit namespace custom field (default `namespaces`) are skipped. Ensure the target namespaces exist or enable namespace creation in the Helm chart.
