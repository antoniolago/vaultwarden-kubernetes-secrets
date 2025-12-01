#!/bin/bash

# Quick verification script for all fixes

set -e

echo "=========================================="
echo "🔍 Verifying All Fixes"
echo "=========================================="
echo

# 1. Check database
echo "1️⃣  Database Status:"
echo "-------------------------------------------"
DB_PATH="../VaultwardenK8sSync/data/sync.db"
IMMICH_STATUS=$(sqlite3 "$DB_PATH" "SELECT status FROM SecretStates WHERE namespace='immich' AND secretName='immich-secrets-ton';" 2>/dev/null || echo "NOT_FOUND")

if [ "$IMMICH_STATUS" = "Deleted" ]; then
    echo "✅ Immich secret status: Deleted"
else
    echo "❌ Immich secret status: $IMMICH_STATUS (expected: Deleted)"
    echo "   Run: sqlite3 $DB_PATH \"UPDATE SecretStates SET Status='Deleted', LastError='Secret manually cleaned' WHERE namespace='immich';\""
fi
echo

# 2. Check if API is running
echo "2️⃣  API Status:"
echo "-------------------------------------------"
if curl -s http://localhost:8080/health &>/dev/null; then
    echo "✅ API is running"
    
    # Check discovery endpoint
    DISCOVERY=$(curl -s http://localhost:8080/api/discovery 2>&1)
    VW_ITEMS=$(echo "$DISCOVERY" | jq '.vaultwardenItems | length' 2>/dev/null || echo "0")
    SYNCED=$(echo "$DISCOVERY" | jq '.syncedSecrets | length' 2>/dev/null || echo "0")
    
    echo "   Vaultwarden Items: $VW_ITEMS"
    echo "   Synced Secrets: $SYNCED"
    
    if [ "$VW_ITEMS" -gt 0 ]; then
        echo "✅ Discovery API fetching VW items"
    else
        echo "⚠️  Discovery API not fetching VW items (VaultwardenService may not be running)"
    fi
    
    # Check if immich secret is in synced list
    IMMICH_IN_SYNCED=$(echo "$DISCOVERY" | jq '.syncedSecrets[] | select(.namespace == "immich")' 2>/dev/null)
    if [ -z "$IMMICH_IN_SYNCED" ]; then
        echo "✅ Immich secret NOT in synced list"
    else
        echo "❌ Immich secret still in synced list"
    fi
else
    echo "❌ API is not running"
    echo "   Start with: cd ../VaultwardenK8sSync.Api && dotnet run"
fi
echo

# 3. Check dashboard build
echo "3️⃣  Dashboard Status:"
echo "-------------------------------------------"
if [ -f "../dashboard/dist/index.html" ]; then
    echo "✅ Dashboard built"
    BUILD_TIME=$(stat -c %y "../dashboard/dist/index.html" 2>/dev/null | cut -d' ' -f1,2 | cut -d'.' -f1)
    echo "   Last build: $BUILD_TIME"
else
    echo "⚠️  Dashboard not built"
    echo "   Build with: cd ../dashboard && bun run build"
fi

if curl -s http://localhost:3000 &>/dev/null; then
    echo "✅ Dashboard is running"
else
    echo "❌ Dashboard is not running"
    echo "   Start with: cd ../dashboard && bun run dev"
fi
echo

# 4. Check code changes
echo "4️⃣  Code Changes:"
echo "-------------------------------------------"

# Check Discovery.tsx for filter
if grep -q "filter(s => s.status !== 'Deleted')" ../dashboard/src/pages/Discovery.tsx; then
    echo "✅ Discovery.tsx filters deleted secrets"
else
    echo "❌ Discovery.tsx missing deleted filter"
fi

# Check DiscoveryController for VaultwardenService
if grep -q "IVaultwardenService" ../VaultwardenK8sSync.Api/Controllers/DiscoveryController.cs; then
    echo "✅ DiscoveryController uses IVaultwardenService"
else
    echo "❌ DiscoveryController not using IVaultwardenService"
fi

# Check Program.cs for service registration
if grep -q "AddScoped<IVaultwardenService" ../VaultwardenK8sSync.Api/Program.cs; then
    echo "✅ API Program.cs registers VaultwardenService"
else
    echo "❌ API Program.cs missing VaultwardenService registration"
fi
echo

# 5. Summary
echo "=========================================="
echo "📊 Summary"
echo "=========================================="
echo

ISSUES=0

if [ "$IMMICH_STATUS" != "Deleted" ]; then
    echo "❌ Database needs update"
    ((ISSUES++))
fi

if ! curl -s http://localhost:8080/health &>/dev/null; then
    echo "⚠️  API not running (start to test Discovery)"
    ((ISSUES++))
fi

if ! curl -s http://localhost:3000 &>/dev/null; then
    echo "⚠️  Dashboard not running (start to test UI)"
    ((ISSUES++))
fi

if [ $ISSUES -eq 0 ]; then
    echo "✅ All fixes verified!"
    echo
    echo "🧪 Ready to test:"
    echo "   1. Open http://localhost:3000/discovery"
    echo "   2. Check 'Synced' tab - immich should NOT be there"
    echo "   3. Run E2E tests: cd scripts && ./e2e-test.sh"
else
    echo "⚠️  $ISSUES issue(s) found - see above for details"
fi

echo "=========================================="
