# Vaultwarden Kubernetes Secrets Sync

This repository syncs Vaultwarden items to Kubernetes Secrets.

## Install (Helm)

Recommended method:

```bash
helm upgrade -i vaultwarden-sync charts/vaultwarden-k8s-sync \
  --namespace vaultwarden-sync --create-namespace \
  --set image.repository=ghcr.io/antoniolago/vaultwarden-k8s-sync \
  --set image.tag=latest
```

Configure env via values (sensitive via secretRefs). See chart values in `charts/vaultwarden-k8s-sync/values.yaml`.

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