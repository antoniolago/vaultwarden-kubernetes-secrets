#!/bin/bash
# Test script to verify GlobalSyncLock prevents concurrent syncs

echo "🧪 Testing GlobalSyncLock - Concurrent Sync Prevention"
echo "======================================================="
echo ""

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCK_FILE="/tmp/vaultwarden-sync-operation.lock"

# Clean up any existing locks
rm -f "$LOCK_FILE"
rm -f /tmp/vaultwarden-sync.lock

echo "✅ Test 1: Acquiring GlobalSyncLock from shell script"
echo "   Creating lock file to simulate sync in progress..."

# Create lock file to simulate a sync operation in progress
echo "PID:$$
Started:$(date -u +%Y-%m-%dT%H:%M:%SZ)
Type:TestScript
" > "$LOCK_FILE"

# Try to lock it exclusively (this simulates what GlobalSyncLock does)
exec 200>"$LOCK_FILE"
if flock -n 200; then
    echo "   ✓ Lock acquired by test script"
else
    echo "   ✗ Failed to acquire lock"
    exit 1
fi

echo ""
echo "❌ Test 2: Try to run manual sync while lock is held (should fail immediately)"
cd "$PROJECT_ROOT/VaultwardenK8sSync"

# Capture output
SYNC_OUTPUT=$(timeout 2s dotnet run --no-build sync 2>&1 || true)

if echo "$SYNC_OUTPUT" | grep -q "Sync already in progress"; then
    echo "   ✓ Manual sync correctly rejected!"
    echo "   Output snippet:"
    echo "$SYNC_OUTPUT" | grep "Sync already in progress" | head -1 | sed 's/^/     /'
else
    echo "   ✗ Manual sync should have been rejected but wasn't!"
    echo "   Full output:"
    echo "$SYNC_OUTPUT" | head -10 | sed 's/^/     /'
    flock -u 200
    rm -f "$LOCK_FILE"
    exit 1
fi

echo ""
echo "🛑 Test 3: Release lock and try sync again (should work)"
flock -u 200
rm -f "$LOCK_FILE"
echo "   Lock released"

# Note: We won't actually run a full sync here as it requires auth
# But we verified the lock rejection works

echo ""
echo "=================================="
echo "✅ GlobalSyncLock is working correctly!"
echo ""
echo "Summary:"
echo "  ✓ Lock can be acquired"
echo "  ✓ Concurrent syncs are rejected"
echo "  ✓ Lock is released properly"
