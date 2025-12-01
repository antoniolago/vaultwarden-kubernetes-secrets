#!/bin/bash
# Extract production database from Kubernetes for analysis

set -e

NAMESPACE="${1:-default}"
POD_NAME="${2:-}"

echo "🔍 Extracting Production Database from Kubernetes"
echo "=================================================="
echo ""

# Find the pod if not specified
if [ -z "$POD_NAME" ]; then
    echo "Finding vaultwarden-sync pod in namespace: $NAMESPACE"
    POD_NAME=$(kubectl get pods -n "$NAMESPACE" -l app=vaultwarden-sync -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
    
    if [ -z "$POD_NAME" ]; then
        echo "❌ No pod found. Trying without label selector..."
        POD_NAME=$(kubectl get pods -n "$NAMESPACE" -o jsonpath='{.items[?(@.metadata.name contains "vaultwarden")].metadata.name}' 2>/dev/null | awk '{print $1}')
    fi
    
    if [ -z "$POD_NAME" ]; then
        echo "❌ Could not find vaultwarden-sync pod"
        echo ""
        echo "Available pods:"
        kubectl get pods -n "$NAMESPACE"
        echo ""
        echo "Usage: $0 [namespace] [pod-name]"
        exit 1
    fi
fi

echo "✅ Found pod: $POD_NAME"
echo ""

# Check if database exists in pod
echo "Checking database location..."
DB_PATH=$(kubectl exec -n "$NAMESPACE" "$POD_NAME" -- sh -c "ls -la /data/sync.db 2>/dev/null || ls -la /app/data/sync.db 2>/dev/null || echo 'NOT_FOUND'" | grep -v '^total' | awk '{print $NF}')

if [ "$DB_PATH" = "NOT_FOUND" ] || [ -z "$DB_PATH" ]; then
    echo "❌ Database not found in pod"
    echo ""
    echo "Checking possible locations:"
    kubectl exec -n "$NAMESPACE" "$POD_NAME" -- sh -c "find / -name 'sync.db' 2>/dev/null | head -5"
    exit 1
fi

echo "✅ Database found at: $DB_PATH"
echo ""

# Copy database from pod
OUTPUT_FILE="./production-sync.db"
echo "Copying database to: $OUTPUT_FILE"
kubectl cp "$NAMESPACE/$POD_NAME:$DB_PATH" "$OUTPUT_FILE"

if [ ! -f "$OUTPUT_FILE" ]; then
    echo "❌ Failed to copy database"
    exit 1
fi

echo "✅ Database copied successfully!"
echo ""

# Show basic stats
echo "📊 Quick Stats from Production Database:"
echo "========================================"
sqlite3 "$OUTPUT_FILE" <<'EOF'
.mode column
.headers on

SELECT 
    COUNT(*) as total_syncs,
    SUM(CASE WHEN Status = 'Success' THEN 1 ELSE 0 END) as successful,
    SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) as failed,
    printf('%.1f', (SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) * 100.0 / COUNT(*))) as failure_rate_pct
FROM SyncLogs;
EOF
echo ""

FAILED_COUNT=$(sqlite3 "$OUTPUT_FILE" "SELECT COUNT(*) FROM SyncLogs WHERE Status='Failed';")

if [ "$FAILED_COUNT" -gt 0 ]; then
    echo "❌ $FAILED_COUNT failed syncs detected!"
    echo ""
    echo "🔍 Now run full analysis:"
    echo "   bash scripts/analyze-database.sh $OUTPUT_FILE"
    echo ""
else
    echo "✅ No failures in production database!"
    echo ""
fi

echo "📁 Production database saved to: $OUTPUT_FILE"
echo ""
