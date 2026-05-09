#!/bin/bash
set -e

# Diagnostic script to investigate sync failures
# Usage: ./diagnose-sync-failures.sh [API_URL] [AUTH_TOKEN]

API_URL="${1:-http://localhost:5000}"
AUTH_TOKEN="${2:-}"

echo "🔍 Vaultwarden K8s Sync - Failure Diagnostics"
echo "=============================================="
echo ""

# Build auth header
if [ -n "$AUTH_TOKEN" ]; then
    AUTH_HEADER="Authorization: Bearer $AUTH_TOKEN"
else
    AUTH_HEADER=""
fi

echo "📊 Step 1: Getting Overview..."
OVERVIEW=$(curl -s -H "$AUTH_HEADER" "$API_URL/api/dashboard/overview")
echo "$OVERVIEW" | jq '.'
echo ""

FAILED_COUNT=$(echo "$OVERVIEW" | jq -r '.totalSyncs - .successfulSyncs')
echo "❌ Failed syncs detected: $FAILED_COUNT"
echo ""

echo "📋 Step 2: Fetching recent failed sync logs..."
FAILED_LOGS=$(curl -s -H "$AUTH_HEADER" "$API_URL/api/sync-logs?status=Failed&limit=20")
echo "$FAILED_LOGS" | jq '.'
echo ""

echo "🔍 Step 3: Analyzing error patterns..."
echo ""

# Extract and count error types
echo "Error Message Analysis:"
echo "$FAILED_LOGS" | jq -r '.[] | .errorMessage' | sort | uniq -c | sort -rn
echo ""

# Check for common error patterns
echo "🔎 Common Issues Detection:"
echo ""

# Authentication errors
AUTH_ERRORS=$(echo "$FAILED_LOGS" | jq -r '.[] | select(.errorMessage | contains("authentication") or contains("auth") or contains("login") or contains("401")) | .errorMessage' | wc -l)
if [ "$AUTH_ERRORS" -gt 0 ]; then
    echo "⚠️  AUTHENTICATION ERRORS: $AUTH_ERRORS found"
    echo "   → Check VAULTWARDEN_CLIENT_ID and VAULTWARDEN_CLIENT_SECRET"
    echo "   → Verify Vaultwarden server is accessible"
    echo "   → Check API key hasn't expired"
    echo ""
fi

# Network/timeout errors
NETWORK_ERRORS=$(echo "$FAILED_LOGS" | jq -r '.[] | select(.errorMessage | contains("timeout") or contains("connection") or contains("network") or contains("timed out")) | .errorMessage' | wc -l)
if [ "$NETWORK_ERRORS" -gt 0 ]; then
    echo "⚠️  NETWORK/TIMEOUT ERRORS: $NETWORK_ERRORS found"
    echo "   → Increase timeout values in config"
    echo "   → Check network connectivity to Vaultwarden"
    echo "   → Verify firewall rules"
    echo ""
fi

# Kubernetes API errors
K8S_ERRORS=$(echo "$FAILED_LOGS" | jq -r '.[] | select(.errorMessage | contains("kubernetes") or contains("k8s") or contains("forbidden") or contains("403")) | .errorMessage' | wc -l)
if [ "$K8S_ERRORS" -gt 0 ]; then
    echo "⚠️  KUBERNETES API ERRORS: $K8S_ERRORS found"
    echo "   → Check ServiceAccount permissions (RBAC)"
    echo "   → Verify namespace access"
    echo "   → Run: kubectl auth can-i create secrets --as=system:serviceaccount:default:vaultwarden-sync"
    echo ""
fi

# Validation errors
VALIDATION_ERRORS=$(echo "$FAILED_LOGS" | jq -r '.[] | select(.errorMessage | contains("invalid") or contains("validation") or contains("format")) | .errorMessage' | wc -l)
if [ "$VALIDATION_ERRORS" -gt 0 ]; then
    echo "⚠️  VALIDATION ERRORS: $VALIDATION_ERRORS found"
    echo "   → Check Vaultwarden item names (must be valid K8s secret names)"
    echo "   → Verify 'namespaces' field format in Vaultwarden items"
    echo "   → Check for special characters in secret names"
    echo ""
fi

# Null reference errors
NULL_ERRORS=$(echo "$FAILED_LOGS" | jq -r '.[] | select(.errorMessage | contains("null") or contains("NullReference") or contains("not set")) | .errorMessage' | wc -l)
if [ "$NULL_ERRORS" -gt 0 ]; then
    echo "⚠️  NULL REFERENCE ERRORS: $NULL_ERRORS found"
    echo "   → Check Vaultwarden items have required fields (username, password, namespaces)"
    echo "   → Verify 'namespaces' custom field exists and is populated"
    echo "   → Ensure items have Login type (not Secure Note or Card)"
    echo ""
fi

echo "📋 Step 4: Checking secret states..."
SECRET_STATES=$(curl -s -H "$AUTH_HEADER" "$API_URL/api/secret-states")
FAILED_SECRETS=$(echo "$SECRET_STATES" | jq '[.[] | select(.status == "Failed")] | length')
echo "Failed secret states: $FAILED_SECRETS"
echo ""

if [ "$FAILED_SECRETS" -gt 0 ]; then
    echo "Failed secrets details:"
    echo "$SECRET_STATES" | jq '[.[] | select(.status == "Failed")] | .[] | {namespace, secretName, itemName, lastError}'
    echo ""
fi

echo "📋 Step 5: Recent sync attempts..."
RECENT_SYNCS=$(curl -s -H "$AUTH_HEADER" "$API_URL/api/sync-logs?limit=5")
echo "$RECENT_SYNCS" | jq '.[] | {startTime, status, duration: .durationSeconds, itemsProcessed, errors: .errorMessage}'
echo ""

echo "✅ Step 6: Recommendations"
echo "=========================="
echo ""

# Calculate failure rate
TOTAL_SYNCS=$(echo "$OVERVIEW" | jq -r '.totalSyncs')
SUCCESS_RATE=$(echo "scale=1; (($TOTAL_SYNCS - $FAILED_COUNT) * 100) / $TOTAL_SYNCS" | bc)
echo "Current success rate: $SUCCESS_RATE%"
echo ""

if [ "$FAILED_COUNT" -gt 100 ]; then
    echo "🚨 HIGH FAILURE RATE DETECTED!"
    echo ""
    echo "Immediate actions:"
    echo "1. Check application logs:"
    echo "   kubectl logs -n default deployment/vaultwarden-sync --tail=100"
    echo ""
    echo "2. Verify configuration:"
    echo "   kubectl get configmap vaultwarden-sync-config -o yaml"
    echo ""
    echo "3. Check pod logs for authentication errors:"
    echo "   kubectl logs -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets"
    echo ""
elif [ "$FAILED_COUNT" -gt 10 ]; then
    echo "⚠️  MODERATE FAILURES DETECTED"
    echo ""
    echo "Suggested actions:"
    echo "1. Review error patterns above"
    echo "2. Fix most common error type first"
    echo "3. Monitor next sync cycle"
    echo ""
else
    echo "ℹ️  LOW FAILURE COUNT"
    echo ""
    echo "These may be transient errors. Monitor the next few sync cycles."
    echo ""
fi

echo "🔧 Quick Fixes:"
echo ""
echo "# Force a new sync to test fixes"
echo "curl -X POST -H \"$AUTH_HEADER\" $API_URL/api/sync"
echo ""
echo "# Clear failed secret states"
echo "# (This will retry failed secrets on next sync)"
echo "kubectl exec -it deployment/vaultwarden-sync -- rm -f /data/sync.db"
echo "kubectl rollout restart deployment/vaultwarden-sync"
echo ""
echo "# Check current pod status"
echo "kubectl get pods -l app=vaultwarden-sync"
echo ""

echo "✅ Diagnostics complete!"
