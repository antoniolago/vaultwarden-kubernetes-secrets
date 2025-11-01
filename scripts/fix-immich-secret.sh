#!/bin/bash

# Fix script for immich-secrets-ton stale database entry

set -e

echo "==============================================="
echo "🔧 Immich Secret Database Fix"
echo "==============================================="
echo

# Check current state
echo "📊 Current State:"
sqlite3 ../VaultwardenK8sSync/data/sync.db <<EOF
.mode column
.headers on
SELECT namespace, secretName, status, vaultwardenItemId FROM SecretStates WHERE namespace='immich';
EOF
echo

# Ask for confirmation
echo "This script will:"
echo "  1. Mark the immich-secrets-ton as 'Deleted' in the database"
echo "  2. Set LastError to indicate it was manually cleaned"
echo "  3. Keep the record for audit purposes"
echo
read -p "Proceed with fix? (y/N): " -n 1 -r
echo

if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Aborted."
    exit 1
fi

echo
echo "🔧 Applying fix..."

# Update the database
sqlite3 ../VaultwardenK8sSync/data/sync.db <<EOF
UPDATE SecretStates
SET 
    Status = 'Deleted',
    LastError = 'Secret manually cleaned - no longer exists in K8s',
    LastSynced = datetime('now')
WHERE namespace = 'immich' AND secretName = 'immich-secrets-ton';
EOF

echo "✅ Database updated!"
echo

# Verify
echo "📊 New State:"
sqlite3 ../VaultwardenK8sSync/data/sync.db <<EOF
.mode column
.headers on
SELECT namespace, secretName, status, lastError FROM SecretStates WHERE namespace='immich';
EOF

echo
echo "==============================================="
echo "✅ Fix Complete!"
echo "==============================================="
echo
echo "Next steps:"
echo "  1. Check dashboard: http://localhost:3000"
echo "  2. Immich namespace should show 0 active secrets"
echo "  3. Go to Secrets page to verify 'Deleted' status"
echo "  4. Check Sync Logs - next sync won't show this as deleted"
echo
echo "To permanently remove from database (not recommended):"
echo "  sqlite3 ../VaultwardenK8sSync/data/sync.db 'DELETE FROM SecretStates WHERE namespace=\"immich\";'"
echo "==============================================="
