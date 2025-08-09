# Vaultwarden Kubernetes Secrets Sync (Deployment)

This repository provides Kubernetes manifests and an image that syncs secrets from Vaultwarden instance (Bitwarden not tested but should work) to Kubernetes Secrets.

For development and detailed usage, see `VaultwardenK8sSync/README.md`.

## Quick deploy

Remote (public repo):

```
kubectl apply -k https://raw.githubusercontent.com/antoniolago/vaultwarden-kubernetes-secrets/main/deploy
```

Local (cloned repo):

```
kubectl apply -k VaultwardenK8sSync/deploy
```

## What gets created

- Namespace `vaultwarden-sync`
- ServiceAccount + RBAC for managing Secrets
- ConfigMap `vaultwarden-sync-config` (non-sensitive)
- Secret `vaultwarden-sync-secrets` (sensitive)
- Deployment `vaultwarden-sync`
 - CronJob `vaultwarden-sync` (optional; runs sync on a schedule)

## Configure values

Apply these manifests (replace placeholders):

ConfigMap:
```yaml
apiVersion: v1                      # Core API group
kind: ConfigMap                     # Non-sensitive configuration
metadata:
  name: vaultwarden-sync-config     # ConfigMap name used by the Deployment
  namespace: vaultwarden-sync       # Must match the app namespace
data:
  # Vaultwarden server base URL (e.g., https://vault.yourdomain.com)
  VAULTWARDEN__SERVERURL: "https://your-vaultwarden-server.com"

  # Optional scoping (limit listing items to an organization, folder, or collection)
  VAULTWARDEN__ORGANIZATIONID: ""   # Prefer ID when known
  VAULTWARDEN__ORGANIZATIONNAME: "" # Or resolve by name
  VAULTWARDEN__FOLDERID: ""         # Folder ID
  VAULTWARDEN__FOLDERNAME: ""       # Or folder name
  VAULTWARDEN__COLLECTIONID: ""     # Collection ID
  VAULTWARDEN__COLLECTIONNAME: ""   # Or collection name

  # Kubernetes / Sync behavior
  KUBERNETES__INCLUSTER: "true"     # Set true when running in cluster
  SYNC__NAMESPACETAG: "#namespaces:" # Notes tag to target namespaces in Vaultwarden items
  SYNC__DRYRUN: "false"             # true to simulate without applying changes
  SYNC__DELETEORPHANS: "true"       # Remove managed secrets not present in Vaultwarden
  SYNC__SYNCINTERVALSECONDS: "300"  # Interval for continuous sync mode
  SYNC__CONTINUOUSSYNC: "true"      # true to run periodically; false for one-shot (good for cronjobs)
```

Secret:
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: vaultwarden-sync-secrets
  namespace: vaultwarden-sync
type: Opaque
stringData:
  # Bitwarden CLI API key credentials
  BW_CLIENTID: "your-client-id"
  BW_CLIENTSECRET: "your-client-secret"
  # Bitwarden/Vaultwarden CLI will login via API key and unlock with this password
  VAULTWARDEN__MASTERPASSWORD: "your-master-password"

```

## Image override

Default image: `ghcr.io/antoniolago/vaultwarden-k8s-sync:latest`.

To pin/override via kustomize images, create an overlay and apply that overlay.

## CronJob variant

If you pefer to use CronJobs, there is a manifest provided at `VaultwardenK8sSync/deploy/cronjob.yaml`. It runs a one-shot sync on a schedule (default every 10 minutes). Adjust `spec.schedule` as needed.

## Uninstall

```
kubectl delete -k https://raw.githubusercontent.com/antoniolago/vaultwarden-kubernetes-secrets/main/deploy
```