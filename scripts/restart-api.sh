#!/bin/bash

# Script to restart API with latest code

set -e

echo "=========================================="
echo "🔄 Restarting API with Latest Code"
echo "=========================================="
echo

# 1. Kill existing API
echo "1️⃣  Stopping existing API..."
pkill -f "VaultwardenK8sSync.Api" 2>/dev/null || true
pkill -f "dotnet.*Api" 2>/dev/null || true
sleep 2
echo "✓ Stopped"
echo

# 2. Build API
echo "2️⃣  Building API..."
cd /home/tonio/Documentos/GitHub/vaultwarden-kubernetes-secrets/VaultwardenK8sSync.Api
dotnet build --verbosity minimal
if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi
echo "✓ Built successfully"
echo

# 3. Start API
echo "3️⃣  Starting API..."
dotnet run &
API_PID=$!
echo "✓ API started (PID: $API_PID)"
echo

# 4. Wait for API to be ready
echo "4️⃣  Waiting for API to be ready..."
for i in {1..30}; do
    if curl -s http://localhost:8080/health > /dev/null 2>&1; then
        echo "✓ API is healthy!"
        break
    fi
    sleep 1
    if [ $i -eq 30 ]; then
        echo "❌ API failed to start after 30 seconds"
        exit 1
    fi
    echo -n "."
done
echo
echo

# 5. Test endpoints
echo "5️⃣  Testing endpoints..."
echo

echo "📍 Testing Data Keys endpoint..."
KEYS=$(curl -s http://localhost:8080/api/secrets/default/custom-secret-name/keys)
if echo "$KEYS" | grep -q "mysecurepass"; then
    echo "✅ Data Keys: WORKING - Shows actual key names"
    echo "   Keys: $(echo $KEYS | jq -r 'join(", ")')"
elif echo "$KEYS" | grep -q "key1"; then
    echo "⚠️  Data Keys: Still showing generic names (key1, key2)"
    echo "   This might be because the secret doesn't exist in K8s"
else
    echo "⚠️  Data Keys: $KEYS"
fi
echo

echo "📍 Testing Discovery endpoint..."
DISCOVERY=$(curl -s http://localhost:8080/api/discovery)
VW_COUNT=$(echo "$DISCOVERY" | jq '.vaultwardenItems | length')
if [ "$VW_COUNT" -gt 0 ]; then
    echo "✅ Discovery: WORKING - Fetching Vaultwarden items"
    echo "   Items: $VW_COUNT"
    echo "   First item: $(echo "$DISCOVERY" | jq -r '.vaultwardenItems[0].name')"
else
    echo "❌ Discovery: NOT WORKING - 0 Vaultwarden items"
    echo "   This means authentication is failing"
    echo "   Check that environment variables are set:"
    echo "     - BW_CLIENTID"
    echo "     - BW_CLIENTSECRET"
    echo "     - BW_PASSWORD"
    echo "     - VAULTWARDEN_SERVER"
fi
echo

# 6. Summary
echo "=========================================="
echo "📊 Summary"
echo "=========================================="
echo
echo "API is running at: http://localhost:8080"
echo "Swagger UI: http://localhost:8080/swagger"
echo "Health check: http://localhost:8080/health"
echo
echo "Process ID: $API_PID"
echo "To stop: kill $API_PID"
echo
echo "Next steps:"
echo "  1. Start dashboard: cd dashboard && bun run dev"
echo "  2. Open http://localhost:3000"
echo "  3. Test Data Keys modal"
echo "  4. Test Discovery page"
echo
echo "=========================================="
