Usage with kubectl remote apply/kustomize
----------------------------------------

Parameterization is done via a ConfigMap and Secret.

Quick start (replace placeholders):

kubectl apply -k https://raw.githubusercontent.com/antoniolago/vaultwarden-kubernetes-secrets/main/deploy

Then patch the ConfigMap/Secret in place or create overlays.

Recommended: create your own overlay repo or kustomize overlay with image tag and envs.

