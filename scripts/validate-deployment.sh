#!/bin/bash
# Comprehensive deployment validation script

NAMESPACE="${1:-vaultwarden-test}"
EXIT_CODE=0

echo "🧪 Comprehensive Deployment Validation"
echo "========================================"
echo "Namespace: $NAMESPACE"
echo ""

# Test 1: Deployment exists and ready
echo "Test 1: Deployment Status"
if kubectl get deployment -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets >/dev/null 2>&1; then
    READY=$(kubectl get deployment -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[0].status.readyReplicas}')
    DESIRED=$(kubectl get deployment -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[0].spec.replicas}')
    if [ "$READY" == "$DESIRED" ] && [ "$READY" -gt 0 ]; then
        echo "  ✅ Deployment ready: $READY/$DESIRED replicas"
    else
        echo "  ❌ Deployment not ready: $READY/$DESIRED replicas"
        EXIT_CODE=1
    fi
else
    echo "  ❌ Deployment not found"
    EXIT_CODE=1
fi

# Test 2: Pod is running
echo "Test 2: Pod Status"
RUNNING_PODS=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[?(@.status.phase=="Running")].metadata.name}')
if [ -n "$RUNNING_PODS" ]; then
    echo "  ✅ Pod running: $RUNNING_PODS"
else
    echo "  ❌ No running pods"
    kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets
    EXIT_CODE=1
fi

# Test 3: Init container succeeded
echo "Test 3: Init Container Status"
INIT_STATUS=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[0].status.initContainerStatuses[0].state.terminated.reason}' 2>/dev/null)
if [ "$INIT_STATUS" == "Completed" ]; then
    echo "  ✅ Init container completed successfully"
else
    echo "  ❌ Init container status: $INIT_STATUS"
    EXIT_CODE=1
fi

# Test 4: Container is healthy
echo "Test 4: Container Health"
CONTAINER_READY=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[0].status.containerStatuses[0].ready}')
if [ "$CONTAINER_READY" == "true" ]; then
    echo "  ✅ Container is ready"
else
    echo "  ❌ Container not ready"
    EXIT_CODE=1
fi

# Test 5: Security Context is correct
echo "Test 5: Security Context"
POD_NAME=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[0].metadata.name}')
RUNS_AS_USER=$(kubectl get pod "$POD_NAME" -n "$NAMESPACE" -o jsonpath='{.spec.securityContext.runAsUser}')
FS_GROUP=$(kubectl get pod "$POD_NAME" -n "$NAMESPACE" -o jsonpath='{.spec.securityContext.fsGroup}')
if [ "$RUNS_AS_USER" == "1000" ] && [ "$FS_GROUP" == "1000" ]; then
    echo "  ✅ Security context correct (runAsUser: $RUNS_AS_USER, fsGroup: $FS_GROUP)"
else
    echo "  ⚠️  Security context: runAsUser=$RUNS_AS_USER, fsGroup=$FS_GROUP"
fi

# Test 6: Volumes are mounted
echo "Test 6: Volume Mounts"
DATA_MOUNT=$(kubectl get pod "$POD_NAME" -n "$NAMESPACE" -o jsonpath='{.spec.containers[0].volumeMounts[?(@.name=="data")].mountPath}')
TMP_MOUNT=$(kubectl get pod "$POD_NAME" -n "$NAMESPACE" -o jsonpath='{.spec.containers[0].volumeMounts[?(@.name=="tmp")].mountPath}')
if [ "$DATA_MOUNT" == "/data" ] && [ "$TMP_MOUNT" == "/tmp" ]; then
    echo "  ✅ Volumes mounted: data=$DATA_MOUNT, tmp=$TMP_MOUNT"
else
    echo "  ❌ Volume mounts incorrect: data=$DATA_MOUNT, tmp=$TMP_MOUNT"
    EXIT_CODE=1
fi

# Test 7: ServiceAccount exists
echo "Test 7: ServiceAccount"
if kubectl get serviceaccount vaultwarden-kubernetes-secrets -n "$NAMESPACE" >/dev/null 2>&1; then
    echo "  ✅ ServiceAccount exists"
else
    echo "  ❌ ServiceAccount not found"
    EXIT_CODE=1
fi

# Test 8: RBAC resources
echo "Test 8: RBAC Resources"
RELEASE_NAME=$(kubectl get deployment -n "$NAMESPACE" -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -o jsonpath='{.items[0].metadata.labels.app\.kubernetes\.io/instance}')
CR_NAME="${RELEASE_NAME}-vaultwarden-kubernetes-secrets"
if kubectl get clusterrole "$CR_NAME" >/dev/null 2>&1 && kubectl get clusterrolebinding "$CR_NAME" >/dev/null 2>&1; then
    echo "  ✅ ClusterRole and ClusterRoleBinding exist"
else
    echo "  ⚠️  RBAC resources may not exist"
fi

# Test 9: Secrets exist
echo "Test 9: Required Secrets"
if kubectl get secret vaultwarden-kubernetes-secrets -n "$NAMESPACE" >/dev/null 2>&1; then
    echo "  ✅ Vaultwarden secret exists"
else
    echo "  ❌ Vaultwarden secret not found"
    EXIT_CODE=1
fi

# Test 10: No unwanted pods
echo "Test 10: API/Dashboard Components"
API_PODS=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/component=api -o name 2>/dev/null | wc -l)
DASH_PODS=$(kubectl get pods -n "$NAMESPACE" -l app.kubernetes.io/component=dashboard -o name 2>/dev/null | wc -l)
if [ "$API_PODS" -eq 0 ] && [ "$DASH_PODS" -eq 0 ]; then
    echo "  ✅ No API/Dashboard pods (as expected for basic deployment)"
else
    echo "  ℹ️  Found API: $API_PODS, Dashboard: $DASH_PODS pods"
fi

# Test 11: Verify directories exist in container
echo "Test 11: Directory Structure in Container"
DIR_CHECK=$(kubectl exec -n "$NAMESPACE" "$POD_NAME" -- sh -c "test -d /data/home/.config && test -w /data && test -w /tmp && echo 'OK'" 2>/dev/null)
if [ "$DIR_CHECK" == "OK" ]; then
    echo "  ✅ Required directories exist and are writable"
else
    echo "  ❌ Directory check failed"
    EXIT_CODE=1
fi

# Test 12: Container restart count
echo "Test 12: Container Stability"
RESTART_COUNT=$(kubectl get pod "$POD_NAME" -n "$NAMESPACE" -o jsonpath='{.status.containerStatuses[0].restartCount}')
if [ "$RESTART_COUNT" -eq 0 ]; then
    echo "  ✅ No container restarts"
elif [ "$RESTART_COUNT" -lt 3 ]; then
    echo "  ⚠️  Container restarted $RESTART_COUNT time(s)"
else
    echo "  ❌ Container restarted $RESTART_COUNT times"
    EXIT_CODE=1
fi

echo ""
echo "========================================"
if [ $EXIT_CODE -eq 0 ]; then
    echo "✅ ALL TESTS PASSED"
    echo "Deployment is healthy and ready!"
else
    echo "❌ SOME TESTS FAILED"
    echo "Check the output above for details"
fi
echo "========================================"

exit $EXIT_CODE
