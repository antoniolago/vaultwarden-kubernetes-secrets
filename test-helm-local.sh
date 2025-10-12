#!/bin/bash

# Test Helm chart locally without act
# This creates a kind cluster and tests the helm chart installation

set -e

echo "🚀 Testing Helm chart locally..."
echo ""

# Check if kind is installed
if ! command -v kind &> /dev/null; then
    echo "❌ kind is not installed. Please install it first:"
    echo "   https://kind.sigs.k8s.io/docs/user/quick-start/#installation"
    exit 1
fi

# Check if helm is installed
if ! command -v helm &> /dev/null; then
    echo "❌ helm is not installed. Please install it first:"
    echo "   https://helm.sh/docs/intro/install/"
    exit 1
fi

# Create kind cluster if it doesn't exist
if ! kind get clusters | grep -q "helm-test"; then
    echo "📦 Creating kind cluster..."
    kind create cluster --name helm-test --wait 30s
else
    echo "✅ Using existing kind cluster 'helm-test'"
fi

# Set kubectl context
kubectl cluster-info --context kind-helm-test

echo ""
echo "🔍 Validating Helm chart..."
helm lint charts/vaultwarden-kubernetes-secrets

echo ""
echo "📋 Creating test namespace..."
kubectl create namespace vaultwarden-test --dry-run=client -o yaml | kubectl apply -f -

echo ""
echo "🔐 Creating test secrets..."
kubectl create secret generic vaultwarden-kubernetes-secrets -n vaultwarden-test \
  --from-literal=BW_CLIENTID="test-client-id" \
  --from-literal=BW_CLIENTSECRET="test-client-secret" \
  --from-literal=VAULTWARDEN__MASTERPASSWORD="test-password" \
  --dry-run=client -o yaml | kubectl apply -f -

echo ""
echo "📦 Installing Helm chart (dry-run mode)..."
helm upgrade -i vaultwarden-test ./charts/vaultwarden-kubernetes-secrets \
  --namespace vaultwarden-test \
  --set image.repository=ghcr.io/antoniolago/vaultwarden-kubernetes-secrets \
  --set image.tag=latest \
  --set env.config.VAULTWARDEN__SERVERURL="https://test.example.com" \
  --set env.config.SYNC__DRYRUN="true" \
  --set env.config.SYNC__CONTINUOUSSYNC="false" \
  --wait --timeout 2m

echo ""
echo "🔍 Verifying deployment..."
kubectl get pods -n vaultwarden-test
kubectl get deployment -n vaultwarden-test

echo ""
echo "📋 Deployment details:"
kubectl describe deployment vaultwarden-test-vaultwarden-kubernetes-secrets -n vaultwarden-test

echo ""
echo "✅ Helm chart test completed successfully!"
echo ""
echo "To view logs, run:"
echo "  kubectl logs -n vaultwarden-test -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets"
echo ""
echo "To cleanup, run:"
echo "  kubectl delete namespace vaultwarden-test"
echo "  kind delete cluster --name helm-test"
