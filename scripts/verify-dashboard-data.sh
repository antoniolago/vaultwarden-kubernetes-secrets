#!/bin/bash
# Quick verification script for dashboard data

API_URL="http://localhost:8080/api"

echo "🔍 Verifying Dashboard Data..."
echo ""

# Check if API is running
if ! curl -s -f "$API_URL/dashboard/overview" > /dev/null 2>&1; then
    echo "❌ API is not running at $API_URL"
    echo "   Start it with: cd VaultwardenK8sSync.Api && dotnet run"
    exit 1
fi

echo "✅ API is running"
echo ""

# Test overview endpoint
echo "📊 Testing /api/dashboard/overview"
OVERVIEW=$(curl -s "$API_URL/dashboard/overview")

ACTIVE_SECRETS=$(echo "$OVERVIEW" | jq -r '.activeSecrets // 0')
TOTAL_SYNCS=$(echo "$OVERVIEW" | jq -r '.totalSyncs // 0')
TOTAL_NAMESPACES=$(echo "$OVERVIEW" | jq -r '.totalNamespaces // 0')
SUCCESS_RATE=$(echo "$OVERVIEW" | jq -r '.successRate // 0')

echo "   Active Secrets: $ACTIVE_SECRETS"
echo "   Total Syncs: $TOTAL_SYNCS"
echo "   Namespaces: $TOTAL_NAMESPACES"
echo "   Success Rate: $SUCCESS_RATE%"

if [ "$ACTIVE_SECRETS" -gt "0" ] && [ "$TOTAL_NAMESPACES" -gt "0" ]; then
    echo "   ✅ Overview data looks good!"
else
    echo "   ⚠️  Warning: Some values are zero. Have you run a sync?"
fi
echo ""

# Test namespaces endpoint
echo "📁 Testing /api/dashboard/namespaces"
NAMESPACES=$(curl -s "$API_URL/dashboard/namespaces")
NS_COUNT=$(echo "$NAMESPACES" | jq '. | length')

echo "   Found $NS_COUNT namespaces"

if [ "$NS_COUNT" -gt "0" ]; then
    echo "   Namespace details:"
    echo "$NAMESPACES" | jq -r '.[] | "   • \(.namespace): \(.secretCount) total, \(.activeSecrets) active, \(.failedSecrets) failed, \(.successRate)% success"'
    echo "   ✅ Namespaces data looks good!"
    
    # Test new secrets endpoint
    FIRST_NS=$(echo "$NAMESPACES" | jq -r '.[0].namespace')
    echo ""
    echo "🔑 Testing /api/secrets/namespace/$FIRST_NS/status/Active"
    ACTIVE_NS_SECRETS=$(curl -s "$API_URL/secrets/namespace/$FIRST_NS/status/Active")
    ACTIVE_NS_COUNT=$(echo "$ACTIVE_NS_SECRETS" | jq '. | length')
    echo "   Found $ACTIVE_NS_COUNT active secrets in $FIRST_NS"
    
    if [ "$ACTIVE_NS_COUNT" -gt "0" ]; then
        echo "   Sample secrets:"
        echo "$ACTIVE_NS_SECRETS" | jq -r '.[:3][] | "   • \(.secretName) - \(.dataKeysCount) keys"'
        echo "   ✅ Secrets endpoint working!"
    else
        echo "   ⚠️  No active secrets found (may have only failed secrets)"
    fi
else
    echo "   ❌ No namespaces found. Run a sync first!"
fi
echo ""

# Check dashboard is running
if curl -s -f "http://localhost:3000" > /dev/null 2>&1; then
    echo "✅ Dashboard is running at http://localhost:3000"
    echo ""
    echo "🎯 Next steps:"
    echo "   1. Open http://localhost:3000 in your browser"
    echo "   2. Verify the stats match what you see above"
    echo "   3. Click on Active/Failed/Total chips to test modals"
else
    echo "⚠️  Dashboard is not running"
    echo "   Start it with: cd dashboard && npm run dev"
fi

echo ""
echo "✨ Verification complete!"
