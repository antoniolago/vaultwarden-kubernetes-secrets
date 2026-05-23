#!/bin/bash

# Test script to verify all fixes are working

set -e

echo "=========================================="
echo "🧪 Testing All Fixes"
echo "=========================================="
echo

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# 1. Kill any existing API
echo "1️⃣  Stopping any running API..."
pkill -f "VaultwardenK8sSync.Api" 2>/dev/null || true
pkill -f "dotnet.*Api" 2>/dev/null || true
sleep 2
echo "✓ Old API stopped"
echo

# 2. Build and start new API
echo "2️⃣  Building and starting API with new code..."
cd /home/tonio/Documentos/GitHub/vaultwarden-kubernetes-secrets/VaultwardenK8sSync.Api

# Build
dotnet build --verbosity quiet > /dev/null 2>&1
if [ $? -ne 0 ]; then
    echo -e "${RED}✗ Build failed${NC}"
    exit 1
fi
echo "✓ Build successful"

# Start API in background
nohup dotnet run > /tmp/api-test.log 2>&1 &
API_PID=$!
echo "✓ API starting (PID: $API_PID)"

# Wait for API to be ready
echo "⏳ Waiting for API to be ready..."
for i in {1..30}; do
    if curl -s http://localhost:8080/health > /dev/null 2>&1; then
        echo "✓ API is ready!"
        break
    fi
    sleep 1
    if [ $i -eq 30 ]; then
        echo -e "${RED}✗ API failed to start${NC}"
        echo "Check logs: tail -50 /tmp/api-test.log"
        exit 1
    fi
done
echo

# 3. Test Data Keys
echo "3️⃣  Testing Data Keys..."
KEYS_RESPONSE=$(curl -s http://localhost:8080/api/secrets/default/custom-secret-name/keys)
echo "Response: $KEYS_RESPONSE"

if echo "$KEYS_RESPONSE" | grep -q "mysecurepass"; then
    echo -e "${GREEN}✅ Data Keys showing ACTUAL names!${NC}"
    echo "   Keys: $(echo $KEYS_RESPONSE | jq -r 'join(", ")')"
elif echo "$KEYS_RESPONSE" | grep -q "key1"; then
    echo -e "${RED}❌ Data Keys showing GENERIC names (key1, key2)${NC}"
    echo -e "${YELLOW}   This means the API is still running old code${NC}"
else
    echo -e "${YELLOW}⚠️  Unexpected response: $KEYS_RESPONSE${NC}"
fi
echo

# 4. Test Discovery
echo "4️⃣  Testing Discovery..."
DISCOVERY_RESPONSE=$(curl -s http://localhost:8080/api/discovery)
VW_ITEMS=$(echo "$DISCOVERY_RESPONSE" | jq '.vaultwardenItems | length')
SYNCED=$(echo "$DISCOVERY_RESPONSE" | jq '.syncedSecrets | length')

echo "Vaultwarden Items: $VW_ITEMS"
echo "Synced Secrets: $SYNCED"

if [ "$VW_ITEMS" -gt 0 ]; then
    echo -e "${GREEN}✅ Discovery showing Vaultwarden items!${NC}"
    FIRST_ITEM=$(echo "$DISCOVERY_RESPONSE" | jq -r '.vaultwardenItems[0].name')
    echo "   First item: $FIRST_ITEM"
    
    # Calculate expected values
    ACTIVE_SECRETS=$(echo "$DISCOVERY_RESPONSE" | jq '[.syncedSecrets[] | select(.status == "Active")] | length')
    NOT_SYNCED=$((VW_ITEMS - ACTIVE_SECRETS))
    SYNC_RATE=$(echo "scale=2; $ACTIVE_SECRETS * 100 / $VW_ITEMS" | bc)
    
    echo "   Active Secrets: $ACTIVE_SECRETS"
    echo "   Not Synced: $NOT_SYNCED"
    echo "   Sync Rate: ${SYNC_RATE}%"
else
    echo -e "${RED}❌ Discovery showing 0 Vaultwarden items${NC}"
    echo -e "${YELLOW}   This means authentication is failing${NC}"
    echo "   Check API logs: tail -50 /tmp/api-test.log"
fi
echo

# 5. Test Namespace Count
echo "5️⃣  Testing Namespace Count..."
NAMESPACES_RESPONSE=$(curl -s http://localhost:8080/api/dashboard/namespaces)
TOTAL_NS=$(echo "$NAMESPACES_RESPONSE" | jq 'length')
FILTERED_NS=$(echo "$NAMESPACES_RESPONSE" | jq '[.[] | select(.activeSecrets > 0 or .failedSecrets > 0)] | length')

echo "Total Namespaces: $TOTAL_NS"
echo "Filtered (active/failed): $FILTERED_NS"

if [ "$TOTAL_NS" -eq "$FILTERED_NS" ]; then
    echo -e "${GREEN}✅ Namespace filtering working correctly!${NC}"
else
    echo -e "${YELLOW}⚠️  Dashboard should show: $FILTERED_NS namespaces${NC}"
fi
echo

# 6. Summary
echo "=========================================="
echo "📊 Test Summary"
echo "=========================================="
echo

PASSED=0
FAILED=0

# Check data keys
if echo "$KEYS_RESPONSE" | grep -q "mysecurepass"; then
    echo -e "${GREEN}✅ Data Keys: PASS${NC}"
    ((PASSED++))
else
    echo -e "${RED}❌ Data Keys: FAIL${NC}"
    ((FAILED++))
fi

# Check discovery
if [ "$VW_ITEMS" -gt 0 ]; then
    echo -e "${GREEN}✅ Discovery: PASS${NC}"
    ((PASSED++))
else
    echo -e "${RED}❌ Discovery: FAIL${NC}"
    ((FAILED++))
fi

# Check namespaces
if [ "$FILTERED_NS" -gt 0 ]; then
    echo -e "${GREEN}✅ Namespaces: PASS${NC}"
    ((PASSED++))
else
    echo -e "${RED}❌ Namespaces: FAIL${NC}"
    ((FAILED++))
fi

echo
echo "Results: $PASSED passed, $FAILED failed"
echo

if [ $FAILED -eq 0 ]; then
    echo -e "${GREEN}🎉 ALL TESTS PASSED!${NC}"
    echo
    echo "Next steps:"
    echo "  1. Start dashboard: cd dashboard && bun run dev"
    echo "  2. Open http://localhost:3000"
    echo "  3. Check Data Keys modal"
    echo "  4. Check Discovery page"
    echo
    echo "API is running in background (PID: $API_PID)"
    echo "Logs: tail -f /tmp/api-test.log"
else
    echo -e "${RED}⚠️  SOME TESTS FAILED${NC}"
    echo
    echo "Troubleshooting:"
    echo "  1. Check API logs: tail -50 /tmp/api-test.log"
    echo "  2. Verify environment variables are set"
    echo "  3. Check Kubernetes connection: kubectl get nodes"
    echo "  4. Check pod logs for Vaultwarden auth errors: kubectl logs -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets"
fi

echo
echo "To stop API: kill $API_PID"
echo "=========================================="
