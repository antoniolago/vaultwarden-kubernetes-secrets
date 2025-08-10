# Vaultwarden Kubernetes Secrets Sync

This software syncs Vaultwarden items to Kubernetes Secrets.

## Install (Helm)

Recommended (from GHCR OCI):

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
helm upgrade -i vaultwarden-sync oci://ghcr.io/antoniolago/charts/vaultwarden-k8s-sync \
  --version "$CHART_VERSION" \
  --namespace "$NAMESPACE" --create-namespace \
  --set env.config.VAULTWARDEN__SERVERURL="$SERVER_URL" \
  --set image.tag="$CHART_VERSION"
```

Notes:
- The chart references Secret `vaultwarden-sync-secrets` for sensitive values; the commands above create it.
- Set optional filters (Org/Folder/Collection) via `--set env.config.VAULTWARDEN__ORGANIZATIONID=...` etc.
- Default image repository is `ghcr.io/antoniolago/vaultwarden-k8s-sync`. Override with `--set image.repository=...` if using a fork.


## Install (kubectl)

As an alternative, you can use the raw manifests. See `deploy/README.md` for step-by-step kubectl apply instructions.

## More docs

For detailed app configuration and usage, see `VaultwardenK8sSync/README.md`.

## Limitations

- **Multiline support**: Multiline values are reliably supported when the item type is a Secure Note. For Login/Card/Identity items, custom fields are single-line only and fenced multiline blocks in notes may not be parsed consistently. If you need multiline content, use a Note item.
- **Organization API Key**: Bitwarden CLI (`bw`) does not support logging in with an Organization API Key. Only user API keys (`BW_CLIENTID`/`BW_CLIENTSECRET`) are supported. Ensure that user has the required access to the organization/collections.
- **Attachments**: File attachments are not synchronized. Only text-based fields (passwords, usernames, notes, custom fields, and note-defined KVs/blocks) are processed.
- **Secret type**: Only `Opaque` Kubernetes Secrets are produced. TLS or other special secret types are not generated.
- **Key sanitization and collisions**: Secret keys are sanitized (lowercased and normalized). Different source keys may collide after sanitization; in collisions, the last writer wins.
- **Kubernetes size limits**: A single Secret must remain under the Kubernetes object size limit (~1 MiB). Very large note content or many combined keys under the same secret may cause an update failure.
- **Name-based filters**: When filtering by organization/folder/collection names, the first matching name is used. Prefer IDs to avoid ambiguity.
- **Namespace tags required**: Items without an explicit namespace tag/field (default `namespaces`) are skipped. Ensure the target namespaces exist or enable namespace creation in the Helm chart.