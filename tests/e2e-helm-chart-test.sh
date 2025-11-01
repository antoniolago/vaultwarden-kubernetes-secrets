#!/bin/bash
set -e

# Comprehensive E2E Test for Vaultwarden Kubernetes Secrets Helm Chart
# This script tests the Helm chart installation and basic functionality

NAMESPACE="vks-e2e-test"
RELEASE_NAME="vks-test"
CHART_PATH="./charts/vaultwarden-kubernetes-secrets"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

echo_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

echo_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

cleanup() {
    echo_info "Cleaning up test resources..."
    helm uninstall $RELEASE_NAME -n $NAMESPACE --ignore-not-found || true
    kubectl delete namespace $NAMESPACE --ignore-not-found --timeout=60s || true
    echo_info "Cleanup complete"
}

# Register cleanup on exit
trap cleanup EXIT

echo_info "Starting E2E Helm Chart Tests"
echo_info "=============================="

# Check prerequisites
echo_info "Checking prerequisites..."
command -v kubectl >/dev/null 2>&1 || { echo_error "kubectl is required but not installed. Aborting."; exit 1; }
command -v helm >/dev/null 2>&1 || { echo_error "helm is required but not installed. Aborting."; exit 1; }

# Check if cluster is accessible
if ! kubectl cluster-info >/dev/null 2>&1; then
    echo_error "Cannot connect to Kubernetes cluster. Please check your kubeconfig."
    exit 1
fi

echo_info "Prerequisites check passed ✓"

# Test 1: Helm Lint
echo_info ""
echo_info "Test 1: Helm Chart Linting"
echo_info "----------------------------"
if helm lint $CHART_PATH; then
    echo_info "Helm lint passed ✓"
else
    echo_error "Helm lint failed ✗"
    exit 1
fi

# Test 2: Template Rendering
echo_info ""
echo_info "Test 2: Template Rendering"
echo_info "---------------------------"
if helm template test-render $CHART_PATH \
    --set env.config.VAULTWARDEN__SERVERURL="https://test.example.com" \
    --debug > /tmp/helm-template-output.yaml; then
    echo_info "Template rendering passed ✓"
    echo_info "Generated $(wc -l < /tmp/helm-template-output.yaml) lines of YAML"
else
    echo_error "Template rendering failed ✗"
    exit 1
fi

# Test 3: Create Namespace
echo_info ""
echo_info "Test 3: Creating Test Namespace"
echo_info "--------------------------------"
kubectl create namespace $NAMESPACE
echo_info "Namespace created ✓"

# Test 4: Create Required Secrets
echo_info ""
echo_info "Test 4: Creating Required Secrets"
echo_info "----------------------------------"
kubectl create secret generic vaultwarden-kubernetes-secrets -n $NAMESPACE \
    --from-literal=BW_CLIENTID="test-client-id-e2e" \
    --from-literal=BW_CLIENTSECRET="test-client-secret-e2e" \
    --from-literal=VAULTWARDEN__MASTERPASSWORD="test-password-e2e"
echo_info "Secrets created ✓"

# Test 5: Install Helm Chart (Dry Run Mode)
echo_info ""
echo_info "Test 5: Installing Helm Chart (Dry Run Mode)"
echo_info "--------------------------------------------"
helm upgrade -i $RELEASE_NAME $CHART_PATH \
    --namespace $NAMESPACE \
    --set env.config.VAULTWARDEN__SERVERURL="https://test.example.com" \
    --set env.config.SYNC__DRYRUN="true" \
    --set env.config.SYNC__CONTINUOUSSYNC="false" \
    --set image.pullPolicy="IfNotPresent" \
    --wait --timeout 3m

echo_info "Helm chart installed ✓"

# Test 6: Verify Deployments
echo_info ""
echo_info "Test 6: Verifying Deployments"
echo_info "------------------------------"

# Check main deployment
MAIN_DEPLOYMENT="${RELEASE_NAME}-vaultwarden-kubernetes-secrets"
if kubectl wait --for=condition=available deployment/$MAIN_DEPLOYMENT -n $NAMESPACE --timeout=120s; then
    echo_info "Main deployment ready ✓"
else
    echo_error "Main deployment not ready ✗"
    kubectl describe deployment/$MAIN_DEPLOYMENT -n $NAMESPACE
    exit 1
fi

# Check API deployment
API_DEPLOYMENT="${RELEASE_NAME}-vaultwarden-kubernetes-secrets-api"
if kubectl wait --for=condition=available deployment/$API_DEPLOYMENT -n $NAMESPACE --timeout=120s; then
    echo_info "API deployment ready ✓"
else
    echo_error "API deployment not ready ✗"
    kubectl describe deployment/$API_DEPLOYMENT -n $NAMESPACE
    exit 1
fi

# Check Dashboard deployment
DASHBOARD_DEPLOYMENT="${RELEASE_NAME}-vaultwarden-kubernetes-secrets-dashboard"
if kubectl wait --for=condition=available deployment/$DASHBOARD_DEPLOYMENT -n $NAMESPACE --timeout=120s; then
    echo_info "Dashboard deployment ready ✓"
else
    echo_error "Dashboard deployment not ready ✗"
    kubectl describe deployment/$DASHBOARD_DEPLOYMENT -n $NAMESPACE
    exit 1
fi

# Test 7: Verify Services
echo_info ""
echo_info "Test 7: Verifying Services"
echo_info "--------------------------"

API_SERVICE="${RELEASE_NAME}-vaultwarden-kubernetes-secrets-api"
if kubectl get service $API_SERVICE -n $NAMESPACE >/dev/null 2>&1; then
    echo_info "API service exists ✓"
    kubectl get service $API_SERVICE -n $NAMESPACE
else
    echo_error "API service not found ✗"
    exit 1
fi

DASHBOARD_SERVICE="${RELEASE_NAME}-vaultwarden-kubernetes-secrets-dashboard"
if kubectl get service $DASHBOARD_SERVICE -n $NAMESPACE >/dev/null 2>&1; then
    echo_info "Dashboard service exists ✓"
    kubectl get service $DASHBOARD_SERVICE -n $NAMESPACE
else
    echo_error "Dashboard service not found ✗"
    exit 1
fi

# Test 8: Verify RBAC Resources
echo_info ""
echo_info "Test 8: Verifying RBAC Resources"
echo_info "---------------------------------"

if kubectl get clusterrole vaultwarden-kubernetes-secrets >/dev/null 2>&1; then
    echo_info "ClusterRole exists ✓"
else
    echo_error "ClusterRole not found ✗"
    exit 1
fi

if kubectl get clusterrolebinding vaultwarden-kubernetes-secrets >/dev/null 2>&1; then
    echo_info "ClusterRoleBinding exists ✓"
else
    echo_error "ClusterRoleBinding not found ✗"
    exit 1
fi

SA_NAME="vaultwarden-kubernetes-secrets"
if kubectl get serviceaccount $SA_NAME -n $NAMESPACE >/dev/null 2>&1; then
    echo_info "ServiceAccount exists ✓"
else
    echo_error "ServiceAccount not found ✗"
    exit 1
fi

# Test 9: Verify Pod Security Context
echo_info ""
echo_info "Test 9: Verifying Pod Security Context"
echo_info "---------------------------------------"

MAIN_POD=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets,app.kubernetes.io/component!=api,app.kubernetes.io/component!=dashboard -o jsonpath='{.items[0].metadata.name}')
if [ -n "$MAIN_POD" ]; then
    echo_info "Checking security context for pod: $MAIN_POD"
    
    # Check runAsNonRoot
    RUN_AS_NON_ROOT=$(kubectl get pod $MAIN_POD -n $NAMESPACE -o jsonpath='{.spec.securityContext.runAsNonRoot}')
    if [ "$RUN_AS_NON_ROOT" = "true" ]; then
        echo_info "Pod runs as non-root ✓"
    else
        echo_warn "Pod security context might not enforce non-root"
    fi
    
    # Check readOnlyRootFilesystem
    READ_ONLY=$(kubectl get pod $MAIN_POD -n $NAMESPACE -o jsonpath='{.spec.containers[0].securityContext.readOnlyRootFilesystem}')
    if [ "$READ_ONLY" = "true" ]; then
        echo_info "Pod has read-only root filesystem ✓"
    else
        echo_warn "Pod root filesystem is not read-only"
    fi
else
    echo_error "Main pod not found ✗"
    exit 1
fi

# Test 10: API Health Check
echo_info ""
echo_info "Test 10: API Health Check"
echo_info "-------------------------"

API_POD=$(kubectl get pods -n $NAMESPACE -l app.kubernetes.io/component=api -o jsonpath='{.items[0].metadata.name}')
if [ -n "$API_POD" ]; then
    echo_info "Testing API health endpoint..."
    if kubectl exec -n $NAMESPACE $API_POD -- wget -q -O- http://localhost:8080/health 2>/dev/null | grep -q "Healthy\|healthy\|OK"; then
        echo_info "API health check passed ✓"
    else
        echo_warn "API health check returned unexpected response"
    fi
else
    echo_error "API pod not found ✗"
    exit 1
fi

# Test 11: Check Pod Logs for Errors
echo_info ""
echo_info "Test 11: Checking Pod Logs"
echo_info "---------------------------"

echo_info "Main pod logs (last 20 lines):"
kubectl logs $MAIN_POD -n $NAMESPACE --tail=20 || echo_warn "Could not retrieve main pod logs"

if kubectl logs $MAIN_POD -n $NAMESPACE --tail=100 | grep -i "error\|exception\|fatal" | grep -v "0 error" | grep -v "no error"; then
    echo_warn "Found potential errors in main pod logs (review above)"
else
    echo_info "No obvious errors in main pod logs ✓"
fi

# Test 12: Verify ConfigMap
echo_info ""
echo_info "Test 12: Verifying ConfigMap"
echo_info "-----------------------------"

CONFIGMAP="${RELEASE_NAME}-vaultwarden-kubernetes-secrets-config"
if kubectl get configmap $CONFIGMAP -n $NAMESPACE >/dev/null 2>&1; then
    echo_info "ConfigMap exists ✓"
    
    # Verify dry-run is enabled
    if kubectl get configmap $CONFIGMAP -n $NAMESPACE -o jsonpath='{.data.SYNC__DRYRUN}' | grep -q "true"; then
        echo_info "Dry-run mode is enabled ✓"
    else
        echo_error "Dry-run mode is not enabled ✗"
        exit 1
    fi
else
    echo_error "ConfigMap not found ✗"
    exit 1
fi

# Test 13: Verify PVC
echo_info ""
echo_info "Test 13: Verifying PersistentVolumeClaim"
echo_info "----------------------------------------"

PVC="${RELEASE_NAME}-vaultwarden-kubernetes-secrets-api-data"
if kubectl get pvc $PVC -n $NAMESPACE >/dev/null 2>&1; then
    echo_info "PVC exists ✓"
    
    # Check PVC status
    PVC_STATUS=$(kubectl get pvc $PVC -n $NAMESPACE -o jsonpath='{.status.phase}')
    if [ "$PVC_STATUS" = "Bound" ]; then
        echo_info "PVC is bound ✓"
    else
        echo_warn "PVC status: $PVC_STATUS"
    fi
else
    echo_error "PVC not found ✗"
    exit 1
fi

# Test 14: Helm Upgrade Test
echo_info ""
echo_info "Test 14: Testing Helm Upgrade"
echo_info "------------------------------"

if helm upgrade $RELEASE_NAME $CHART_PATH \
    --namespace $NAMESPACE \
    --set env.config.VAULTWARDEN__SERVERURL="https://updated.example.com" \
    --set env.config.SYNC__DRYRUN="true" \
    --set env.config.SYNC__CONTINUOUSSYNC="false" \
    --reuse-values \
    --wait --timeout 2m; then
    echo_info "Helm upgrade successful ✓"
else
    echo_error "Helm upgrade failed ✗"
    exit 1
fi

# Test 15: Verify Resource Limits
echo_info ""
echo_info "Test 15: Verifying Resource Limits"
echo_info "-----------------------------------"

for pod in $(kubectl get pods -n $NAMESPACE -o jsonpath='{.items[*].metadata.name}'); do
    echo_info "Checking pod: $pod"
    
    # Check if resource limits are set
    LIMITS=$(kubectl get pod $pod -n $NAMESPACE -o jsonpath='{.spec.containers[*].resources.limits}')
    if [ -n "$LIMITS" ] && [ "$LIMITS" != "{}" ]; then
        echo_info "  Resource limits configured ✓"
    else
        echo_warn "  No resource limits configured"
    fi
done

# Final Summary
echo_info ""
echo_info "================================"
echo_info "E2E Test Summary"
echo_info "================================"
echo_info "✓ All critical tests passed!"
echo_info ""
echo_info "Resources created:"
echo_info "  - Namespace: $NAMESPACE"
echo_info "  - Release: $RELEASE_NAME"
echo_info ""
echo_info "Test artifacts:"
echo_info "  - Template output: /tmp/helm-template-output.yaml"
echo_info ""
echo_info "Cleanup will run automatically on exit"
