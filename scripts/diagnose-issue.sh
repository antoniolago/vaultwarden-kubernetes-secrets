#!/bin/bash

CLUSTER_NAME="${CLUSTER_NAME:-vaultwarden-test}"
NAMESPACE="${NAMESPACE:-vaultwarden-test}"

echo "🔍 Kubernetes Diagnostics"
echo "========================"
echo ""

# Check if cluster exists
echo "📋 Cluster Status:"
if ! kind get clusters 2>/dev/null | grep -q "^${CLUSTER_NAME}$"; then
    echo "  ❌ Kind cluster '${CLUSTER_NAME}' not found!"
    echo "     Run: kind create cluster --name ${CLUSTER_NAME}"
    exit 1
fi
echo "  ✅ Cluster exists"

# Check kubectl context
echo ""
echo "📋 Kubectl Context:"
CURRENT_CONTEXT=$(kubectl config current-context 2>/dev/null || echo "none")
echo "  Current: $CURRENT_CONTEXT"
if [[ "$CURRENT_CONTEXT" != "kind-${CLUSTER_NAME}" ]]; then
    echo "  ⚠️  Not using correct context!"
    echo "     Run: kubectl config use-context kind-${CLUSTER_NAME}"
fi

# Check nodes
echo ""
echo "📋 Node Status:"
kubectl get nodes -o wide 2>/dev/null || echo "  ❌ Cannot get nodes"

# Check namespace
echo ""
echo "📋 Namespace Status:"
if kubectl get namespace "$NAMESPACE" >/dev/null 2>&1; then
    echo "  ✅ Namespace '$NAMESPACE' exists"
else
    echo "  ❌ Namespace '$NAMESPACE' not found!"
    echo "     Run: kubectl create namespace $NAMESPACE"
fi

# Check all pods in cluster
echo ""
echo "📋 All Pods in Cluster:"
kubectl get pods -A -o wide

# Check pods in target namespace
echo ""
echo "📋 Pods in '$NAMESPACE' namespace:"
if kubectl get pods -n "$NAMESPACE" >/dev/null 2>&1; then
    kubectl get pods -n "$NAMESPACE" -o wide
    
    # If there are pods, describe them
    if kubectl get pods -n "$NAMESPACE" -o name | head -1 >/dev/null 2>&1; then
        echo ""
        echo "📋 Pod Details:"
        for POD in $(kubectl get pods -n "$NAMESPACE" -o name); do
            echo ""
            echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
            echo "Pod: $POD"
            echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
            kubectl describe -n "$NAMESPACE" "$POD"
            echo ""
            echo "📜 Pod Logs:"
            kubectl logs -n "$NAMESPACE" "$POD" --all-containers=true --tail=50 2>&1 || echo "No logs available"
        done
    fi
else
    echo "  No pods found"
fi

# Check deployments
echo ""
echo "📋 Deployments:"
kubectl get deployments -n "$NAMESPACE" -o wide 2>/dev/null || echo "  No deployments found"

# Check replicasets
echo ""
echo "📋 ReplicaSets:"
kubectl get replicasets -n "$NAMESPACE" -o wide 2>/dev/null || echo "  No replicasets found"

# Check events
echo ""
echo "📋 Recent Events (last 20):"
kubectl get events -n "$NAMESPACE" --sort-by='.lastTimestamp' 2>/dev/null | tail -20 || echo "  No events found"

# Check secrets
echo ""
echo "📋 Secrets:"
kubectl get secrets -n "$NAMESPACE" 2>/dev/null || echo "  No secrets found"

# Check images in kind
echo ""
echo "📋 Container Images in Kind Cluster:"
docker exec "${CLUSTER_NAME}-control-plane" crictl images 2>/dev/null | grep -E "(IMAGE|vaultwarden|REPOSITORY)" || echo "  Cannot access kind images"

# Check helm releases
echo ""
echo "📋 Helm Releases:"
helm list -n "$NAMESPACE" 2>/dev/null || echo "  No helm releases found"

# Summary
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "🔍 Quick Fix Suggestions:"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Check for common issues
if ! kubectl get pods -n "$NAMESPACE" -o jsonpath='{.items[0].status.phase}' 2>/dev/null | grep -q "Running"; then
    echo ""
    echo "⚠️  Pods are not running. Common causes:"
    echo ""
    echo "1. ImagePullBackOff - Image not loaded in kind:"
    echo "   docker build -f VaultwardenK8sSync/Dockerfile -t vaultwarden-kubernetes-secrets:latest ."
    echo "   kind load docker-image vaultwarden-kubernetes-secrets:latest --name $CLUSTER_NAME"
    echo ""
    echo "2. CrashLoopBackOff - Container exiting:"
    echo "   • Check logs above"
    echo "   • Use debug mode: --set debug=true"
    echo "   • Check if secrets exist: kubectl get secrets -n $NAMESPACE"
    echo ""
    echo "3. Init container failing:"
    echo "   • Check security context settings"
    echo "   • Check volume permissions"
fi

# Check for pending pods
PENDING=$(kubectl get pods -n "$NAMESPACE" -o jsonpath='{.items[?(@.status.phase=="Pending")].metadata.name}' 2>/dev/null)
if [ -n "$PENDING" ]; then
    echo ""
    echo "⚠️  Pods stuck in Pending:"
    echo "   $PENDING"
    echo ""
    echo "   Check node resources:"
    echo "   kubectl describe node"
fi

echo ""
echo "📋 To clean up and retry:"
echo "  helm uninstall vaultwarden-test -n $NAMESPACE"
echo "  kubectl delete namespace $NAMESPACE"
echo "  ./scripts/test-helm-locally.sh"
echo ""
echo "📋 To delete cluster and start fresh:"
echo "  kind delete cluster --name $CLUSTER_NAME"
echo "  ./scripts/test-helm-locally.sh"
echo ""
