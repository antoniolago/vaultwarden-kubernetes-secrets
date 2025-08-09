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