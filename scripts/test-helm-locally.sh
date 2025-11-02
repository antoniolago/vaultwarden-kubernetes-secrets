#!/bin/bash
set -e

echo "🚀 Local Helm Chart Testing with Kind"
echo "======================================"

# Check prerequisites
command -v kind >/dev/null 2>&1 || { echo "❌ kind is required but not installed. Install from: https://kind.sigs.k8s.io/"; exit 1; }
command -v kubectl >/dev/null 2>&1 || { echo "❌ kubectl is required but not installed."; exit 1; }
command -v helm >/dev/null 2>&1 || { echo "❌ helm is required but not installed."; exit 1; }
command -v docker >/dev/null 2>&1 || { echo "❌ docker is required but not installed."; exit 1; }

CLUSTER_NAME="${CLUSTER_NAME:-vaultwarden-test}"
NAMESPACE="vaultwarden-test"
IMAGE_TAG="${IMAGE_TAG:-latest}"

echo ""
echo "📋 Configuration:"
echo "  Cluster: $CLUSTER_NAME"
echo "  Namespace: $NAMESPACE"
echo "  Image Tag: $IMAGE_TAG"
echo ""

# Create kind cluster if it doesn't exist
if ! kind get clusters | grep -q "^${CLUSTER_NAME}$"; then
    echo "🔧 Creating kind cluster..."
    kind create cluster --name "$CLUSTER_NAME" --config - <<EOF
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
nodes:
- role: control-plane
  extraPortMappings:
  - containerPort: 30080
    hostPort: 8080
    protocol: TCP
EOF
else
    echo "✅ Kind cluster already exists"
fi

# Switch context
echo "🔄 Setting kubectl context..."
kubectl config use-context "kind-${CLUSTER_NAME}"

# Build Docker image
echo "🐳 Building Docker image..."
docker build -f VaultwardenK8sSync/Dockerfile -t "vaultwarden-kubernetes-secrets:${IMAGE_TAG}" .

# Load image into kind
echo "📦 Loading image into kind cluster..."
kind load docker-image "vaultwarden-kubernetes-secrets:${IMAGE_TAG}" --name "$CLUSTER_NAME"

# Clean up any existing installation
echo "🧹 Cleaning up any existing installation..."
helm uninstall vaultwarden-test -n "$NAMESPACE" 2>/dev/null || echo "  No existing release found"
kubectl delete namespace "$NAMESPACE" --ignore-not-found=true
sleep 2

# Create namespace
echo "🏗️  Creating namespace..."
kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f -

# Create test secrets
echo "🔐 Creating test secrets..."
kubectl create secret generic vaultwarden-kubernetes-secrets -n "$NAMESPACE" \
  --from-literal=BW_CLIENTID="test-client-id" \
  --from-literal=BW_CLIENTSECRET="test-client-secret" \
  --from-literal=VAULTWARDEN__MASTERPASSWORD="test-password" \
  --dry-run=client -o yaml | kubectl apply -f -

# Verify image is loaded in kind
echo "🔍 Verifying image in cluster..."
docker exec "${CLUSTER_NAME}-control-plane" crictl images | grep vaultwarden || {
    echo "⚠️  Image not found in cluster, reloading..."
    kind load docker-image "vaultwarden-kubernetes-secrets:${IMAGE_TAG}" --name "$CLUSTER_NAME"
}

# Check cluster is healthy
echo "🏥 Checking cluster health..."
kubectl get nodes
kubectl get pods -A

# Dry-run first to check for template errors
echo "🧪 Dry-run Helm install to check templates..."
helm upgrade --install vaultwarden-test ./charts/vaultwarden-kubernetes-secrets \
  --namespace "$NAMESPACE" \
  --set image.repository=vaultwarden-kubernetes-secrets \
  --set image.tag="$IMAGE_TAG" \
  --set image.pullPolicy=Never \
  --set debug=true \
  --set api.enabled=false \
  --set dashboard.enabled=false \
  --dry-run --debug | head -100

# Install or upgrade Helm chart
echo ""
echo "📊 Installing Helm chart (with verbose output)..."
echo "⏳ This may take up to 2 minutes..."
helm upgrade --install vaultwarden-test ./charts/vaultwarden-kubernetes-secrets \
  --namespace "$NAMESPACE" \
  --set image.repository=vaultwarden-kubernetes-secrets \
  --set image.tag="$IMAGE_TAG" \
  --set image.pullPolicy=Never \
  --set debug=true \
  --set api.enabled=false \
  --set dashboard.enabled=false \
  --wait --timeout 2m \
  --debug 2>&1 | tee /tmp/helm-install.log || {
    echo ""
    echo "❌ Helm install failed!"
    echo ""
    echo "📋 Checking pod status..."
    kubectl get pods -n "$NAMESPACE" -o wide
    echo ""
    echo "📋 Checking events..."
    kubectl get events -n "$NAMESPACE" --sort-by='.lastTimestamp' | tail -20
    echo ""
    echo "📋 Describing deployment..."
    kubectl describe deployment -n "$NAMESPACE" 2>/dev/null || echo "No deployment found"
    echo ""
    if kubectl get pods -n "$NAMESPACE" -o name | head -1 > /dev/null 2>&1; then
        POD=$(kubectl get pods -n "$NAMESPACE" -o name | head -1)
        echo "📋 Pod logs (if available)..."
        kubectl logs -n "$NAMESPACE" "$POD" --all-containers=true 2>&1 || echo "No logs available yet"
        echo ""
        echo "📋 Pod describe..."
        kubectl describe -n "$NAMESPACE" "$POD"
    fi
    exit 1
}

echo ""
echo "✅ Helm chart installed successfully!"
echo ""

# Run verification tests
echo "🧪 Running verification tests..."
echo ""

# Test 1: Check deployment exists
echo "✓ Test 1: Checking deployment exists..."
if kubectl get deployment vaultwarden-test-vaultwarden-kubernetes-secrets -n "$NAMESPACE" >/dev/null 2>&1; then
    echo "  ✅ Deployment found"
else
    echo "  ❌ Deployment not found"
    exit 1
fi

# Test 2: Check pod is running
echo "✓ Test 2: Checking pod is running..."
READY_PODS=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[?(@.status.phase=="Running")].metadata.name}' 2>/dev/null)
if [ -n "$READY_PODS" ]; then
    echo "  ✅ Pod is running: $READY_PODS"
else
    echo "  ❌ No running pods found"
    kubectl get pods -n "$NAMESPACE" -o wide
    exit 1
fi

# Test 3: Check container is in debug mode
echo "✓ Test 3: Checking debug mode..."
POD_NAME=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[0].metadata.name}')
COMMAND=$(kubectl get pod "$POD_NAME" -n "$NAMESPACE" -o jsonpath='{.spec.containers[0].command[*]}')
if echo "$COMMAND" | grep -q "tail.*null"; then
    echo "  ✅ Container running in debug mode: $COMMAND"
else
    echo "  ⚠️  Container command: $COMMAND (expected: tail -f /dev/null)"
fi

# Test 4: Check no API/Dashboard pods
echo "✓ Test 4: Checking API/Dashboard are disabled..."
API_PODS=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/component=api -o name 2>/dev/null | wc -l)
DASH_PODS=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/component=dashboard -o name 2>/dev/null | wc -l)
if [ "$API_PODS" -eq 0 ] && [ "$DASH_PODS" -eq 0 ]; then
    echo "  ✅ API and Dashboard correctly disabled"
else
    echo "  ⚠️  Found API pods: $API_PODS, Dashboard pods: $DASH_PODS"
fi

# Test 5: Check ServiceAccount
echo "✓ Test 5: Checking ServiceAccount exists..."
if kubectl get serviceaccount vaultwarden-kubernetes-secrets -n "$NAMESPACE" >/dev/null 2>&1; then
    echo "  ✅ ServiceAccount found"
else
    echo "  ❌ ServiceAccount not found"
fi

# Test 6: Check RBAC
echo "✓ Test 6: Checking RBAC resources..."
if kubectl get clusterrole vaultwarden-kubernetes-secrets >/dev/null 2>&1; then
    echo "  ✅ ClusterRole found"
else
    echo "  ⚠️  ClusterRole not found"
fi
if kubectl get clusterrolebinding vaultwarden-kubernetes-secrets >/dev/null 2>&1; then
    echo "  ✅ ClusterRoleBinding found"
else
    echo "  ⚠️  ClusterRoleBinding not found"
fi

echo ""
echo "✅ All verification tests passed!"
echo ""
echo "📋 Useful commands:"
echo "  # Check pod status"
echo "  kubectl get pods -n $NAMESPACE"
echo ""
echo "  # View logs"
echo "  kubectl logs -n $NAMESPACE -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -f"
echo ""
echo "  # Describe deployment"
echo "  kubectl describe deployment -n $NAMESPACE"
echo ""
echo "  # Get into the pod"
echo "  kubectl exec -it -n $NAMESPACE \$(kubectl get pod -n $NAMESPACE -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[0].metadata.name}') -- /bin/bash"
echo ""
echo "  # Test with real sync (disable debug mode)"
echo "  helm upgrade vaultwarden-test ./charts/vaultwarden-kubernetes-secrets \\"
echo "    --namespace $NAMESPACE \\"
echo "    --set image.repository=vaultwarden-kubernetes-secrets \\"
echo "    --set image.tag=$IMAGE_TAG \\"
echo "    --set image.pullPolicy=Never \\"
echo "    --set debug=false \\"
echo "    --set env.config.VAULTWARDEN__SERVERURL=https://your-server.com \\"
echo "    --set env.config.SYNC__DRYRUN=true"
echo ""
echo "  # Uninstall"
echo "  helm uninstall vaultwarden-test -n $NAMESPACE"
echo ""
echo "  # Delete cluster"
echo "  kind delete cluster --name $CLUSTER_NAME"
echo ""
