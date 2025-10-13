#!/bin/bash

# Diagnostic script for immich-secrets-ton issue

echo "==============================================="
echo "🔍 Immich Secret Diagnostic"
echo "==============================================="
echo

# 1. Check database state
echo "1️⃣  Database State:"
echo "-------------------------------------------"
sqlite3 ../VaultwardenK8sSync/data/sync.db <<EOF
.mode column
.headers on
SELECT namespace, secretName, status, vaultwardenItemId, lastSynced 
FROM SecretStates 
WHERE namespace='immich' OR secretName LIKE '%immich%';
EOF
echo

# 2. Check latest sync log
echo "2️⃣  Latest Sync Results:"
echo "-------------------------------------------"
sqlite3 ../VaultwardenK8sSync/data/sync.db <<EOF
.mode column
.headers on
SELECT Id, Status, TotalItems, ProcessedItems, CreatedSecrets, UpdatedSecrets, DeletedSecrets, StartTime
FROM SyncLogs
ORDER BY Id DESC
LIMIT 1;
EOF
echo

# 3. Check if BW CLI is available and authenticated
echo "3️⃣  Vaultwarden CLI Status:"
echo "-------------------------------------------"
if command -v bw &> /dev/null; then
    echo "✓ bw-cli found"
    if [ -n "$BW_SESSION" ]; then
        echo "✓ BW_SESSION is set"
        echo "  Attempting to fetch item..."
        bw list items --session "$BW_SESSION" 2>&1 | jq '.[] | select(.name == "immich-secrets-ton") | {name, id, fields: [.fields[]? | select(.name == "namespaces") | {name, value}]}' 2>&1 || echo "❌ Failed to fetch or item not found"
    else
        echo "❌ BW_SESSION not set in current shell"
        echo "   Run: export BW_SESSION=\$(bw unlock --raw)"
    fi
else
    echo "❌ bw-cli not found"
fi
echo

# 4. Check API discovery endpoint
echo "4️⃣  Discovery API Status:"
echo "-------------------------------------------"
if curl -s http://localhost:8080/health &>/dev/null; then
    echo "✓ API is running"
    DISCOVERY=$(curl -s http://localhost:8080/api/discovery 2>&1)
    VW_ITEMS=$(echo "$DISCOVERY" | jq '.vaultwardenItems | length' 2>/dev/null || echo "error")
    SYNCED=$(echo "$DISCOVERY" | jq '.syncedSecrets | length' 2>/dev/null || echo "error")
    echo "  Vaultwarden Items: $VW_ITEMS"
    echo "  Synced Secrets: $SYNCED"
    
    if [ "$VW_ITEMS" = "0" ]; then
        echo "  ⚠️  API cannot fetch VW items (BW_SESSION not set for API)"
    fi
else
    echo "❌ API not running"
fi
echo

# 5. Check K8s secret
echo "5️⃣  Kubernetes Secret Status:"
echo "-------------------------------------------"
if command -v kubectl &> /dev/null; then
    if kubectl get secret -n immich immich-secrets-ton &>/dev/null; then
        echo "✓ Secret exists in K8s"
        kubectl get secret -n immich immich-secrets-ton -o jsonpath='{.metadata.labels}' | jq '.' 2>/dev/null || echo "  (no labels)"
    else
        echo "❌ Secret does NOT exist in K8s"
    fi
else
    echo "⚠️  kubectl not available"
fi
echo

# 6. Recommendations
echo "==============================================="
echo "📋 Recommendations:"
echo "==============================================="

# Check if immich secret exists in DB
IMMICH_COUNT=$(sqlite3 ../VaultwardenK8sSync/data/sync.db "SELECT COUNT(*) FROM SecretStates WHERE namespace='immich';")

if [ "$IMMICH_COUNT" -gt 0 ]; then
    echo
    echo "🔍 The immich secret is still in the database."
    echo
    echo "To verify if the 'namespaces' field was removed from Vaultwarden:"
    echo "  1. Log into your Vaultwarden web vault"
    echo "  2. Open the 'immich-secrets-ton' item"
    echo "  3. Check the Custom Fields section"
    echo "  4. Verify that 'namespaces' field is completely removed (not just empty)"
    echo
    echo "If the field IS removed:"
    echo "  • Run a manual sync: cd ../VaultwardenK8sSync && dotnet run"
    echo "  • Check if orphan cleanup is enabled: SYNC__DELETEORPHANS=true (default)"
    echo "  • Check sync logs for orphan detection"
    echo
    echo "If the field is NOT removed:"
    echo "  • Remove the 'namespaces' custom field completely"
    echo "  • Save the item in Vaultwarden"
    echo "  • Wait for next sync or trigger manually"
fi

echo
echo "To manually trigger cleanup:"
echo "  cd ../VaultwardenK8sSync && dotnet run"
echo
echo "To set BW_SESSION:"
echo "  export BW_SESSION=\$(bw unlock --raw)"
echo "==============================================="
