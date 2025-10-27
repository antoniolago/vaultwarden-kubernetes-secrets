#!/bin/bash
# Test script to verify only one sync service process can run at a time

echo "🧪 Testing Process Lock Mechanism"
echo "=================================="
echo ""

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Clean up any existing lock
rm -f /tmp/vaultwarden-sync.lock

echo "✅ Test 1: Starting first sync service instance..."
cd "$PROJECT_ROOT/VaultwardenK8sSync"
timeout 3s dotnet run > /tmp/test-sync1.log 2>&1 &
PID1=$!
sleep 1

if kill -0 $PID1 2>/dev/null; then
    echo "   ✓ First instance started (PID: $PID1)"
else
    echo "   ✗ First instance failed to start"
    cat /tmp/test-sync1.log
    exit 1
fi

echo ""
echo "❌ Test 2: Attempting to start second instance (should fail)..."
timeout 2s dotnet run > /tmp/test-sync2.log 2>&1
EXIT_CODE=$?

if [ $EXIT_CODE -eq 1 ]; then
    echo "   ✓ Second instance correctly rejected!"
    echo "   Output:"
    head -5 /tmp/test-sync2.log | sed 's/^/     /'
else
    echo "   ✗ Second instance should have failed but didn't (exit code: $EXIT_CODE)"
    cat /tmp/test-sync2.log
    kill $PID1 2>/dev/null
    exit 1
fi

echo ""
echo "🛑 Test 3: Stopping first instance..."
kill $PID1 2>/dev/null
wait $PID1 2>/dev/null
sleep 1
echo "   ✓ First instance stopped"

echo ""
echo "✅ Test 4: Starting new instance after cleanup (should succeed)..."
timeout 3s dotnet run > /tmp/test-sync3.log 2>&1 &
PID3=$!
sleep 1

if kill -0 $PID3 2>/dev/null; then
    echo "   ✓ New instance started successfully (PID: $PID3)"
    kill $PID3 2>/dev/null
else
    echo "   ✗ New instance failed to start"
    cat /tmp/test-sync3.log
    exit 1
fi

echo ""
echo "🧹 Cleanup..."
rm -f /tmp/test-sync*.log
rm -f /tmp/vaultwarden-sync.lock

echo ""
echo "=================================="
echo "✅ ALL TESTS PASSED!"
echo "Process lock is working correctly."
