#!/bin/bash
# Direct database analysis script
# Works with the SQLite database file directly

set -e

DB_FILE="${1:-./data/sync.db}"

if [ ! -f "$DB_FILE" ]; then
    echo "❌ Database file not found: $DB_FILE"
    echo ""
    echo "Usage: $0 [path-to-sync.db]"
    echo ""
    echo "To get database from Kubernetes:"
    echo "  kubectl cp default/vaultwarden-sync-pod:/data/sync.db ./sync.db"
    exit 1
fi

OUTPUT_DIR="./db-analysis-output"
mkdir -p "$OUTPUT_DIR"

echo "🔍 Analyzing Database: $DB_FILE"
echo "================================"
echo ""

echo "📊 Overall Statistics"
echo "===================="
sqlite3 "$DB_FILE" <<'EOF' | tee "$OUTPUT_DIR/01-statistics.txt"
.mode column
.headers on

SELECT 
    COUNT(*) as total_syncs,
    SUM(CASE WHEN Status = 'Success' THEN 1 ELSE 0 END) as successful,
    SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) as failed,
    printf('%.2f', (SUM(CASE WHEN Status = 'Success' THEN 1 ELSE 0 END) * 100.0 / COUNT(*))) as success_rate_pct,
    printf('%.2f', AVG(DurationSeconds)) as avg_duration_sec
FROM SyncLogs;
EOF
echo ""

echo "📋 Status Breakdown"
echo "==================" 
sqlite3 "$DB_FILE" <<'EOF' | tee "$OUTPUT_DIR/02-status-breakdown.txt"
.mode column
.headers on

SELECT 
    Status,
    COUNT(*) as count,
    printf('%.2f', (COUNT(*) * 100.0 / (SELECT COUNT(*) FROM SyncLogs))) as percentage
FROM SyncLogs
GROUP BY Status
ORDER BY count DESC;
EOF
echo ""

echo "❌ Failed Syncs Details (Last 20)"
echo "================================="
sqlite3 "$DB_FILE" <<'EOF' | tee "$OUTPUT_DIR/03-failed-syncs.txt"
.mode line
.headers on

SELECT 
    Id,
    datetime(StartTime, 'localtime') as StartTime,
    DurationSeconds,
    ProcessedItems,
    CreatedSecrets,
    UpdatedSecrets,
    FailedSecrets,
    SUBSTR(ErrorMessage, 1, 200) as ErrorMessage
FROM SyncLogs
WHERE Status = 'Failed'
ORDER BY StartTime DESC
LIMIT 20;
EOF
echo ""

echo "🔍 Error Message Analysis"
echo "========================="
sqlite3 "$DB_FILE" <<'EOF' | tee "$OUTPUT_DIR/04-error-patterns.txt"
.mode column
.headers on

SELECT 
    CASE 
        WHEN ErrorMessage LIKE '%null%' OR ErrorMessage LIKE '%NullReference%' THEN 'NULL_REFERENCE'
        WHEN ErrorMessage LIKE '%auth%' OR ErrorMessage LIKE '%401%' OR ErrorMessage LIKE '%unauthorized%' THEN 'AUTHENTICATION'
        WHEN ErrorMessage LIKE '%forbidden%' OR ErrorMessage LIKE '%403%' THEN 'PERMISSION'
        WHEN ErrorMessage LIKE '%timeout%' OR ErrorMessage LIKE '%timed out%' THEN 'TIMEOUT'
        WHEN ErrorMessage LIKE '%invalid%' OR ErrorMessage LIKE '%validation%' THEN 'VALIDATION'
        WHEN ErrorMessage LIKE '%namespace%' THEN 'NAMESPACE'
        ELSE 'OTHER'
    END as error_type,
    COUNT(*) as count,
    printf('%.1f', (COUNT(*) * 100.0 / (SELECT COUNT(*) FROM SyncLogs WHERE Status = 'Failed'))) as pct_of_failures
FROM SyncLogs
WHERE Status = 'Failed' AND ErrorMessage IS NOT NULL
GROUP BY error_type
ORDER BY count DESC;
EOF
echo ""

echo "📊 Failed Secrets by Namespace"
echo "=============================="
sqlite3 "$DB_FILE" <<'EOF' | tee "$OUTPUT_DIR/05-failed-by-namespace.txt"
.mode column
.headers on

SELECT 
    Namespace,
    Status,
    COUNT(*) as count
FROM SecretStates
WHERE Status = 'Failed'
GROUP BY Namespace, Status
ORDER BY count DESC;
EOF
echo ""

echo "🔴 Failed Secret Details"
echo "======================="
sqlite3 "$DB_FILE" <<'EOF' | tee "$OUTPUT_DIR/06-failed-secrets.txt"
.mode line
.headers on

SELECT 
    Namespace,
    SecretName,
    VaultwardenItemName,
    VaultwardenItemId,
    Status,
    LastSynced,
    SUBSTR(LastError, 1, 200) as LastError
FROM SecretStates
WHERE Status = 'Failed'
ORDER BY LastSynced DESC
LIMIT 30;
EOF
echo ""

echo "📈 Sync History (Last 10)"
echo "========================"
sqlite3 "$DB_FILE" <<'EOF' | tee "$OUTPUT_DIR/07-recent-history.txt"
.mode column
.headers on

SELECT 
    Id,
    datetime(StartTime, 'localtime') as Time,
    Status,
    DurationSeconds as Duration,
    ProcessedItems as Items,
    CreatedSecrets as Created,
    UpdatedSecrets as Updated,
    FailedSecrets as Failed
FROM SyncLogs
ORDER BY StartTime DESC
LIMIT 10;
EOF
echo ""

echo "🎯 Recommendations"
echo "=================="
RECOMMENDATIONS_FILE="$OUTPUT_DIR/08-recommendations.txt"

{
    echo "Based on database analysis:"
    echo ""
    
    # Count error types
    NULL_COUNT=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM SyncLogs WHERE Status='Failed' AND (ErrorMessage LIKE '%null%' OR ErrorMessage LIKE '%NullReference%');")
    AUTH_COUNT=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM SyncLogs WHERE Status='Failed' AND (ErrorMessage LIKE '%auth%' OR ErrorMessage LIKE '%401%');")
    PERM_COUNT=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM SyncLogs WHERE Status='Failed' AND (ErrorMessage LIKE '%forbidden%' OR ErrorMessage LIKE '%403%');")
    NAMESPACE_COUNT=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM SyncLogs WHERE Status='Failed' AND ErrorMessage LIKE '%namespace%';")
    
    TOTAL_FAILED=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM SyncLogs WHERE Status='Failed';")
    
    echo "Error Breakdown:"
    echo "  NULL/Missing Fields: $NULL_COUNT"
    echo "  Authentication: $AUTH_COUNT"
    echo "  Permissions: $PERM_COUNT"
    echo "  Namespace Related: $NAMESPACE_COUNT"
    echo ""
    
    if [ "$NULL_COUNT" -gt 0 ] && [ "$NULL_COUNT" = "$TOTAL_FAILED" ]; then
        echo "🎯 PRIMARY ISSUE: NULL/Missing Field Errors"
        echo ""
        echo "ROOT CAUSE: Vaultwarden items missing required 'namespaces' custom field"
        echo ""
        echo "FIX:"
        echo "1. Log into your Vaultwarden web interface"
        echo "2. For EACH Login item that should sync:"
        echo "   a. Edit the item"
        echo "   b. Add a Custom Field:"
        echo "      - Name: namespaces"
        echo "      - Type: Text"
        echo "      - Value: default,production (your target namespaces)"
        echo "   c. Save"
        echo "3. Wait for next sync or trigger manually"
        echo ""
    elif [ "$AUTH_COUNT" -gt "$NULL_COUNT" ]; then
        echo "🎯 PRIMARY ISSUE: Authentication Errors"
        echo ""
        echo "ROOT CAUSE: Invalid or expired Vaultwarden API credentials"
        echo ""
        echo "FIX:"
        echo "1. Verify credentials in Kubernetes:"
        echo "   kubectl get secret vaultwarden-sync-secret -o yaml"
        echo "2. Update if needed:"
        echo "   kubectl create secret generic vaultwarden-sync-secret \\"
        echo "     --from-literal=clientId=YOUR_ID \\"
        echo "     --from-literal=clientSecret=YOUR_SECRET \\"
        echo "     --dry-run=client -o yaml | kubectl apply -f -"
        echo "3. Restart deployment:"
        echo "   kubectl rollout restart deployment/vaultwarden-sync"
        echo ""
    elif [ "$PERM_COUNT" -gt 0 ]; then
        echo "🎯 PRIMARY ISSUE: Permission Errors"
        echo ""
        echo "ROOT CAUSE: ServiceAccount lacks RBAC permissions"
        echo ""
        echo "FIX:"
        echo "1. Apply ClusterRole with secret permissions (see TROUBLESHOOTING_SYNC_FAILURES.md)"
        echo "2. Verify: kubectl auth can-i create secrets --as=system:serviceaccount:default:vaultwarden-sync"
        echo ""
    else
        echo "ℹ️  Mixed error types - review detailed logs above"
        echo ""
        echo "Check files in $OUTPUT_DIR/ for specifics"
    fi
    
    echo "Next Steps:"
    echo "1. Review error details in: $OUTPUT_DIR/03-failed-syncs.txt"
    echo "2. Check failed secrets: $OUTPUT_DIR/06-failed-secrets.txt"
    echo "3. Apply recommended fix above"
    echo "4. Monitor next sync cycle"
    
} | tee "$RECOMMENDATIONS_FILE"

echo ""
echo "✅ Analysis Complete!"
echo ""
echo "📁 Output directory: $OUTPUT_DIR/"
echo ""
echo "Key files:"
echo "  01-statistics.txt - Overall stats"
echo "  03-failed-syncs.txt - Failed sync details"
echo "  04-error-patterns.txt - Error type analysis"
echo "  06-failed-secrets.txt - Which secrets are failing"
echo "  08-recommendations.txt - What to do next"
echo ""
