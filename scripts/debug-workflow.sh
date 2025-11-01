#!/bin/bash
# Complete diagnostic workflow for sync failures
# This script collects all information needed to debug the issue

set -e

OUTPUT_DIR="./debug-output"
mkdir -p "$OUTPUT_DIR"

echo "🔍 Starting Complete Diagnostic Workflow"
echo "========================================"
echo ""
echo "Output directory: $OUTPUT_DIR"
echo ""

# Configuration
API_URL="${API_URL:-http://localhost:5000}"
AUTH_TOKEN="${AUTH_TOKEN:-}"

if [ -n "$AUTH_TOKEN" ]; then
    AUTH_HEADER=(-H "Authorization: Bearer $AUTH_TOKEN")
else
    AUTH_HEADER=()
fi

echo "📊 STEP 1: Dashboard Overview"
echo "=============================="
echo ""
curl -s "${AUTH_HEADER[@]}" "$API_URL/api/dashboard/overview" | tee "$OUTPUT_DIR/01-dashboard-overview.json" | jq '.'
echo ""
echo "✅ Saved to: $OUTPUT_DIR/01-dashboard-overview.json"
echo ""

echo "📋 STEP 2: Recent Failed Syncs (Last 20)"
echo "========================================="
echo ""
curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=20" | tee "$OUTPUT_DIR/02-failed-syncs.json" | jq '.'
echo ""
echo "✅ Saved to: $OUTPUT_DIR/02-failed-syncs.json"
echo ""

echo "📋 STEP 3: All Recent Syncs (Last 10)"
echo "======================================"
echo ""
curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?limit=10" | tee "$OUTPUT_DIR/03-recent-syncs.json" | jq '.'
echo ""
echo "✅ Saved to: $OUTPUT_DIR/03-recent-syncs.json"
echo ""

echo "🔍 STEP 4: Error Message Analysis"
echo "=================================="
echo ""
echo "Grouping errors by type:"
curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=100" | \
    jq -r '.[] | .errorMessage' | \
    sort | uniq -c | sort -rn | tee "$OUTPUT_DIR/04-error-summary.txt"
echo ""
echo "✅ Saved to: $OUTPUT_DIR/04-error-summary.txt"
echo ""

echo "📊 STEP 5: Secret States"
echo "========================"
echo ""
curl -s "${AUTH_HEADER[@]}" "$API_URL/api/secret-states" | tee "$OUTPUT_DIR/05-secret-states.json" | jq '.'
echo ""
echo "Failed secrets only:"
curl -s "${AUTH_HEADER[@]}" "$API_URL/api/secret-states" | \
    jq '[.[] | select(.status == "Failed")] | .[] | {namespace, secretName, itemName, lastError}' | \
    tee "$OUTPUT_DIR/06-failed-secrets.json"
echo ""
echo "✅ Saved to: $OUTPUT_DIR/05-secret-states.json and 06-failed-secrets.json"
echo ""

echo "📈 STEP 6: Statistics"
echo "====================="
echo ""
STATS_FILE="$OUTPUT_DIR/07-statistics.txt"

# Calculate statistics from dashboard
DASHBOARD=$(curl -s "${AUTH_HEADER[@]}" "$API_URL/api/dashboard/overview")
TOTAL_SYNCS=$(echo "$DASHBOARD" | jq -r '.totalSyncs // 0')
SUCCESS_SYNCS=$(echo "$DASHBOARD" | jq -r '.successfulSyncs // 0')
FAILED_SYNCS=$((TOTAL_SYNCS - SUCCESS_SYNCS))

if [ "$TOTAL_SYNCS" -gt 0 ]; then
    SUCCESS_RATE=$(echo "scale=2; ($SUCCESS_SYNCS * 100) / $TOTAL_SYNCS" | bc)
    FAILURE_RATE=$(echo "scale=2; ($FAILED_SYNCS * 100) / $TOTAL_SYNCS" | bc)
else
    SUCCESS_RATE=0
    FAILURE_RATE=0
fi

cat > "$STATS_FILE" <<EOF
Sync Statistics
===============

Total Syncs: $TOTAL_SYNCS
Successful: $SUCCESS_SYNCS
Failed: $FAILED_SYNCS

Success Rate: $SUCCESS_RATE%
Failure Rate: $FAILURE_RATE%

Active Secrets: $(echo "$DASHBOARD" | jq -r '.activeSecrets // 0')
Namespaces: $(echo "$DASHBOARD" | jq -r '.namespaceCount // 0')
EOF

cat "$STATS_FILE"
echo ""
echo "✅ Saved to: $STATS_FILE"
echo ""

echo "🔎 STEP 7: Detailed Error Analysis"
echo "==================================="
echo ""
ANALYSIS_FILE="$OUTPUT_DIR/08-error-analysis.txt"

{
    echo "Common Error Patterns:"
    echo "======================"
    echo ""
    
    # Check for authentication errors
    AUTH_COUNT=$(curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=100" | \
        jq -r '.[] | .errorMessage' | grep -ic "auth\|401\|unauthorized\|login" || echo "0")
    
    # Check for null/missing field errors
    NULL_COUNT=$(curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=100" | \
        jq -r '.[] | .errorMessage' | grep -ic "null\|not set\|not found\|nullreference" || echo "0")
    
    # Check for kubernetes errors
    K8S_COUNT=$(curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=100" | \
        jq -r '.[] | .errorMessage' | grep -ic "kubernetes\|forbidden\|403\|k8s" || echo "0")
    
    # Check for validation errors
    VALID_COUNT=$(curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=100" | \
        jq -r '.[] | .errorMessage' | grep -ic "invalid\|validation\|format" || echo "0")
    
    # Check for timeout errors
    TIMEOUT_COUNT=$(curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=100" | \
        jq -r '.[] | .errorMessage' | grep -ic "timeout\|timed out\|connection" || echo "0")
    
    echo "Authentication Errors: $AUTH_COUNT"
    echo "Null/Missing Field Errors: $NULL_COUNT"
    echo "Kubernetes API Errors: $K8S_COUNT"
    echo "Validation Errors: $VALID_COUNT"
    echo "Timeout/Network Errors: $TIMEOUT_COUNT"
    echo ""
    echo "Most Likely Root Cause:"
    echo "----------------------"
    
    if [ "$NULL_COUNT" -gt "$AUTH_COUNT" ] && [ "$NULL_COUNT" -gt "$K8S_COUNT" ]; then
        echo "⚠️  NULL/MISSING FIELD ERRORS (Most common)"
        echo ""
        echo "LIKELY CAUSE: Vaultwarden items missing required 'namespaces' custom field"
        echo ""
        echo "FIX:"
        echo "1. Log into Vaultwarden web interface"
        echo "2. For each Login item, add a custom field:"
        echo "   - Name: namespaces"
        echo "   - Value: default,production (comma-separated namespace list)"
        echo "3. Save the items"
        echo "4. Trigger a new sync"
    elif [ "$AUTH_COUNT" -gt 0 ]; then
        echo "⚠️  AUTHENTICATION ERRORS (Most common)"
        echo ""
        echo "LIKELY CAUSE: Invalid or expired Vaultwarden API credentials"
        echo ""
        echo "FIX:"
        echo "1. Verify VAULTWARDEN_CLIENT_ID and VAULTWARDEN_CLIENT_SECRET"
        echo "2. Check if credentials work in Vaultwarden CLI"
        echo "3. Update the Kubernetes secret with correct credentials"
    elif [ "$K8S_COUNT" -gt 0 ]; then
        echo "⚠️  KUBERNETES API ERRORS (Most common)"
        echo ""
        echo "LIKELY CAUSE: Missing RBAC permissions"
        echo ""
        echo "FIX:"
        echo "1. Apply ClusterRole with secret permissions"
        echo "2. Verify with: kubectl auth can-i create secrets --as=system:serviceaccount:default:vaultwarden-sync"
    else
        echo "ℹ️  Mixed or unclear error pattern"
        echo "Review the error messages in 02-failed-syncs.json for details"
    fi
    
} | tee "$ANALYSIS_FILE"

echo ""
echo "✅ Saved to: $ANALYSIS_FILE"
echo ""

echo "📋 STEP 8: Sample Failed Sync Details"
echo "======================================"
echo ""
echo "First 3 failed syncs with full details:"
curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=3" | \
    jq '.[] | {
        startTime, 
        duration: .durationSeconds, 
        itemsProcessed,
        secretsCreated,
        secretsUpdated,
        secretsFailed,
        errorMessage
    }' | tee "$OUTPUT_DIR/09-sample-failures.json"
echo ""
echo "✅ Saved to: $OUTPUT_DIR/09-sample-failures.json"
echo ""

echo "🎯 STEP 9: Recommendations"
echo "=========================="
echo ""
RECOMMENDATIONS_FILE="$OUTPUT_DIR/10-recommendations.txt"

{
    echo "Immediate Action Items:"
    echo "======================="
    echo ""
    
    if [ "$FAILURE_RATE" = "0" ]; then
        echo "✅ No failures detected! System is healthy."
    elif (( $(echo "$FAILURE_RATE < 5" | bc -l) )); then
        echo "ℹ️  Low failure rate (< 5%) - may be transient issues"
        echo "   → Monitor next few sync cycles"
        echo "   → Check if failures are for same items repeatedly"
    elif (( $(echo "$FAILURE_RATE < 20" | bc -l) )); then
        echo "⚠️  Moderate failure rate ($FAILURE_RATE%)"
        echo "   → Review error patterns in 08-error-analysis.txt"
        echo "   → Fix the most common error type first"
        echo "   → Test with manual sync after fixes"
    else
        echo "🚨 HIGH FAILURE RATE ($FAILURE_RATE%)!"
        echo "   → Critical issue - needs immediate attention"
        echo "   → Check application logs"
        echo "   → Verify configuration and credentials"
        echo "   → See detailed error analysis above"
    fi
    
    echo ""
    echo "Next Steps:"
    echo "----------"
    echo "1. Review: $OUTPUT_DIR/08-error-analysis.txt (error patterns)"
    echo "2. Check: $OUTPUT_DIR/02-failed-syncs.json (detailed error messages)"
    echo "3. Examine: $OUTPUT_DIR/06-failed-secrets.json (which secrets are failing)"
    echo "4. Apply fixes based on the most common error type"
    echo "5. Trigger manual sync: curl -X POST ${AUTH_HEADER[@]} $API_URL/api/sync"
    echo "6. Re-run this script to verify improvement"
    echo ""
    echo "Common Fixes:"
    echo "------------"
    echo "• Missing namespaces field: Add 'namespaces' custom field to Vaultwarden items"
    echo "• Auth errors: Update CLIENT_ID and CLIENT_SECRET in Kubernetes secret"
    echo "• RBAC errors: Apply ClusterRole with secret permissions"
    echo "• Invalid names: Rename items to use lowercase + hyphens only"
    echo ""
    
} | tee "$RECOMMENDATIONS_FILE"

echo "✅ Saved to: $RECOMMENDATIONS_FILE"
echo ""

echo "📦 STEP 10: Creating Summary Report"
echo "===================================="
echo ""
SUMMARY_FILE="$OUTPUT_DIR/00-SUMMARY.txt"

{
    echo "==============================================="
    echo "  SYNC FAILURE DIAGNOSTIC REPORT"
    echo "==============================================="
    echo ""
    echo "Generated: $(date)"
    echo "API URL: $API_URL"
    echo ""
    echo "OVERVIEW"
    echo "--------"
    echo "Total Syncs: $TOTAL_SYNCS"
    echo "Successful: $SUCCESS_SYNCS"
    echo "Failed: $FAILED_SYNCS"
    echo "Success Rate: $SUCCESS_RATE%"
    echo "Failure Rate: $FAILURE_RATE%"
    echo ""
    echo "FILES GENERATED"
    echo "---------------"
    echo "00-SUMMARY.txt           - This summary"
    echo "01-dashboard-overview.json"
    echo "02-failed-syncs.json     - Last 20 failed syncs"
    echo "03-recent-syncs.json     - Last 10 syncs (all statuses)"
    echo "04-error-summary.txt     - Error counts by type"
    echo "05-secret-states.json    - All secret states"
    echo "06-failed-secrets.json   - Failed secrets only"
    echo "07-statistics.txt        - Detailed statistics"
    echo "08-error-analysis.txt    - Root cause analysis"
    echo "09-sample-failures.json  - Sample failed syncs"
    echo "10-recommendations.txt   - Action items"
    echo ""
    echo "TOP ERROR MESSAGES"
    echo "------------------"
    curl -s "${AUTH_HEADER[@]}" "$API_URL/api/sync-logs?status=Failed&limit=100" 2>/dev/null | \
        jq -r '.[] | .errorMessage' | sort | uniq -c | sort -rn | head -5 || echo "Unable to fetch"
    echo ""
    echo "NEXT STEPS"
    echo "----------"
    echo "1. Review 08-error-analysis.txt for root cause"
    echo "2. Check 02-failed-syncs.json for specific errors"
    echo "3. Apply recommended fixes"
    echo "4. Run: curl -X POST $API_URL/api/sync"
    echo "5. Re-run this script to verify"
    echo ""
    echo "See TROUBLESHOOTING_SYNC_FAILURES.md for detailed fixes"
    echo ""
    
} | tee "$SUMMARY_FILE"

echo ""
echo "✅ =============================================="
echo "✅  DIAGNOSTIC WORKFLOW COMPLETE!"
echo "✅ =============================================="
echo ""
echo "📁 All output saved to: $OUTPUT_DIR/"
echo ""
echo "📄 START HERE: $OUTPUT_DIR/00-SUMMARY.txt"
echo ""
echo "🔍 Key files to review:"
echo "   1. 00-SUMMARY.txt - Quick overview"
echo "   2. 08-error-analysis.txt - Root cause analysis"
echo "   3. 02-failed-syncs.json - Detailed error messages"
echo ""
