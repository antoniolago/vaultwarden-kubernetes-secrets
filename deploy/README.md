Deploy (kubectl apply)

Quick start using upstream manifests in this folder.

1) Create namespace and RBAC:

```bash
kubectl apply -f namespace.yaml
```

2) Create ConfigMap and Secret (edit files or create your own):

```bash
kubectl apply -f configmap.yaml
kubectl apply -f secret.yaml
```

3) Choose runtime: Deployment (continuous) or CronJob (periodic)

Continuous (Deployment):
```bash
kubectl apply -f deployment.yaml
```

Periodic (CronJob):
```bash
kubectl apply -f cronjob.yaml
```

Notes:
- Sensitive variables must be provided via Secret, referenced by the Deployment/CronJob using envFrom.secretRef.
- Non-sensitive configuration is provided by ConfigMap.
- Override image repository/tag as needed before applying.
Usage with kubectl remote apply/kustomize
----------------------------------------

Parameterization is done via a ConfigMap and Secret.

Quick start (replace placeholders):

kubectl apply -k https://raw.githubusercontent.com/antoniolago/vaultwarden-kubernetes-secrets/main/deploy

Then patch the ConfigMap/Secret in place or create overlays.

Recommended: create your own overlay repo or kustomize overlay with image tag and envs.

