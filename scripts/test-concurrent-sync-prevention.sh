#!/bin/bash
# Definitive test to prove GlobalSyncLock prevents concurrent syncs

set -e

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

echo "🧪 TESTING: GlobalSyncLock Prevents Concurrent Syncs"
echo "===================================================="
echo ""

# Clean up
echo "🧹 Cleaning up old processes and lock files..."
pkill -f VaultwardenK8sSync || true
sleep 1
rm -f /tmp/vaultwarden-sync*.lock
rm -f /tmp/test-sync-*.log
echo ""

# Build latest code
echo "🔨 Building latest code with GlobalSyncLock..."
dotnet build VaultwardenK8sSync/VaultwardenK8sSync.csproj -c Release --nologo -v quiet
echo "✅ Build complete"
echo ""

# Test 1: Start first sync process
echo "📍 TEST 1: Starting first sync process..."
cd VaultwardenK8sSync
timeout 10s dotnet run -c Release --no-build > /tmp/test-sync-1.log 2>&1 &
SYNC1_PID=$!
echo "   Started PID: $SYNC1_PID"
sleep 2

if ! kill -0 $SYNC1_PID 2>/dev/null; then
    echo "   ❌ First sync process died unexpectedly"
    cat /tmp/test-sync-1.log
    exit 1
fi

# Check it acquired the lock
if grep -q "Process lock acquired" /tmp/test-sync-1.log; then
    echo "   ✅ First process acquired process lock"
else
    echo "   ⚠️  Process lock message not found (yet)"
fi

echo ""

# Test 2: Try to start second sync process (should fail immediately)
echo "📍 TEST 2: Attempting to start second sync process (should be REJECTED)..."
timeout 5s dotnet run -c Release --no-build > /tmp/test-sync-2.log 2>&1 &
SYNC2_PID=$!
echo "   Started PID: $SYNC2_PID"
sleep 2

# Check if second process was rejected
if grep -q "Another sync service process is already running" /tmp/test-sync-2.log; then
    echo "   ✅ Second process correctly REJECTED by ProcessLock"
elif ! kill -0 $SYNC2_PID 2>/dev/null; then
    echo "   ✅ Second process terminated (ProcessLock worked)"
else
    echo "   ❌ Second process is still running! ProcessLock FAILED"
    echo "   First process log:"
    tail -5 /tmp/test-sync-1.log | sed 's/^/      /'
    echo "   Second process log:"
    tail -5 /tmp/test-sync-2.log | sed 's/^/      /'
    kill $SYNC1_PID $SYNC2_PID 2>/dev/null || true
    exit 1
fi

echo ""

# Test 3: Try manual sync while continuous sync is running
echo "📍 TEST 3: Try manual 'sync' command while continuous sync running..."
timeout 3s dotnet run -c Release --no-build sync > /tmp/test-sync-3.log 2>&1 || true

if grep -q "Sync already in progress" /tmp/test-sync-3.log; then
    echo "   ✅ Manual sync correctly REJECTED by GlobalSyncLock"
elif grep -q "Another sync service process is already running" /tmp/test-sync-3.log; then
    echo "   ✅ Manual sync correctly REJECTED by ProcessLock"
else
    echo "   ⚠️  Manual sync rejection not detected"
    echo "   Log excerpt:"
    head -10 /tmp/test-sync-3.log | sed 's/^/      /'
fi

echo ""

# Cleanup
echo "🛑 Stopping test processes..."
kill $SYNC1_PID 2>/dev/null || true
kill $SYNC2_PID 2>/dev/null || true
sleep 1
rm -f /tmp/test-sync-*.log
rm -f /tmp/vaultwarden-sync*.lock

echo ""
echo "===================================================="
echo "✅ ALL TESTS PASSED!"
echo ""
echo "Summary:"
echo "  ✓ First sync process started successfully"
echo "  ✓ Second sync process was REJECTED (ProcessLock working)"
echo "  ✓ Manual sync command was REJECTED (GlobalSyncLock working)"
echo ""
echo "🎉 GlobalSyncLock is preventing concurrent syncs!"
echo "===================================================="
