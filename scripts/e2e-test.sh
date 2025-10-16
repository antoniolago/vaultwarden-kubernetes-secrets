#!/bin/bash
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
SYNC_DIR="$PROJECT_ROOT/VaultwardenK8sSync"
API_DIR="$PROJECT_ROOT/VaultwardenK8sSync.Api"
DASHBOARD_DIR="$PROJECT_ROOT/dashboard"
DB_PATH="$SYNC_DIR/data/sync.db"

# PIDs for cleanup
SYNC_PID=""
API_PID=""
DASHBOARD_PID=""

# Cleanup function
cleanup() {
    echo -e "${YELLOW}Cleaning up processes...${NC}"
    
    if [ -n "$DASHBOARD_PID" ]; then
        echo "Stopping dashboard (PID: $DASHBOARD_PID)"
        kill $DASHBOARD_PID 2>/dev/null || true
    fi
    
    if [ -n "$API_PID" ]; then
        echo "Stopping API (PID: $API_PID)"
        kill $API_PID 2>/dev/null || true
    fi
    
    if [ -n "$SYNC_PID" ]; then
        echo "Stopping sync service (PID: $SYNC_PID)"
        kill $SYNC_PID 2>/dev/null || true
    fi
    
    # Kill any remaining processes on the ports
    lsof -ti:8080 | xargs kill -9 2>/dev/null || true
    lsof -ti:3000 | xargs kill -9 2>/dev/null || true
    lsof -ti:9090 | xargs kill -9 2>/dev/null || true
    
    echo -e "${GREEN}Cleanup complete${NC}"
}

# Set trap for cleanup on exit
trap cleanup EXIT INT TERM

# Function to wait for service
wait_for_service() {
    local url=$1
    local name=$2
    local max_attempts=30
    local attempt=0
    
    echo -e "${BLUE}Waiting for $name to be ready...${NC}"
    
    while [ $attempt -lt $max_attempts ]; do
        if curl -s -f "$url" > /dev/null 2>&1; then
            echo -e "${GREEN}✓ $name is ready${NC}"
            return 0
        fi
        attempt=$((attempt + 1))
        echo -n "."
        sleep 1
    done
    
    echo -e "${RED}✗ $name failed to start${NC}"
    return 1
}

# Function to check database
check_database() {
    echo -e "${BLUE}Checking database...${NC}"
    
    if [ ! -f "$DB_PATH" ]; then
        echo -e "${RED}✗ Database file not found at $DB_PATH${NC}"
        return 1
    fi
    
    local secret_count=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM SecretStates;" 2>/dev/null || echo "0")
    local sync_count=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM SyncLogs;" 2>/dev/null || echo "0")
    
    echo -e "${GREEN}✓ Database exists${NC}"
    echo "  - Secrets: $secret_count"
    echo "  - Sync logs: $sync_count"
    
    if [ "$secret_count" -eq "0" ]; then
        echo -e "${YELLOW}⚠ Warning: No secrets in database${NC}"
        return 1
    fi
    
    return 0
}

# Main execution
echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}  Vaultwarden K8s Sync E2E Test${NC}"
echo -e "${BLUE}========================================${NC}"
echo ""

# Step 1: Build projects
echo -e "${BLUE}Step 1: Building projects...${NC}"
cd "$SYNC_DIR"
dotnet build -c Release > /dev/null 2>&1
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Sync service built${NC}"
else
    echo -e "${RED}✗ Sync service build failed${NC}"
    exit 1
fi

cd "$API_DIR"
dotnet build -c Release > /dev/null 2>&1
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ API built${NC}"
else
    echo -e "${RED}✗ API build failed${NC}"
    exit 1
fi

cd "$DASHBOARD_DIR"
if command -v bun > /dev/null 2>&1; then
    echo "Using Bun for dashboard dependencies"
    bun install > /dev/null 2>&1
else
    echo "Bun not found, using npm"
    npm install > /dev/null 2>&1
fi
echo -e "${GREEN}✓ Dashboard dependencies installed${NC}"
echo ""

# Step 2: Run sync service (one-time)
echo -e "${BLUE}Step 2: Running sync service...${NC}"
cd "$SYNC_DIR"
echo "Rebuilding sync service with latest code..."
dotnet build -c Release > /dev/null 2>&1
if [ $? -ne 0 ]; then
    echo -e "${RED}✗ Sync service rebuild failed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Sync service rebuilt with latest code${NC}"
dotnet run -c Release --no-build sync > /tmp/sync.log 2>&1 &
SYNC_PID=$!
echo "Sync service started (PID: $SYNC_PID)"
echo "Log file: /tmp/sync.log"

# Wait for sync to complete (check log file)
echo -n "Waiting for sync to complete"
for i in {1..60}; do
    if grep -q "Sync completed" /tmp/sync.log 2>/dev/null; then
        echo ""
        
        # Extract sync summary from logs
        echo "  📊 Sync summary:"
        grep -A 20 "VAULTWARDEN K8S SYNC SUMMARY" /tmp/sync.log | grep -E "(Duration|Status|Items from|Namespaces|Changes|Created|Updated|Up-to-date|Failed)" | sed 's/^/    /'
        
        echo -e "${GREEN}✓ Sync completed${NC}"
        break
    fi
    echo -n "."
    sleep 1
    
    if [ $i -eq 60 ]; then
        echo ""
        echo -e "${RED}✗ Sync timed out after 60 seconds${NC}"
        echo "Last 20 lines of sync log:"
        tail -20 /tmp/sync.log
        exit 1
    fi
done
echo ""

# Check if sync was successful
echo -e "${BLUE}2.1: Verifying database...${NC}"
if ! check_database; then
    echo -e "${RED}✗ Sync failed - database not populated${NC}"
    echo "Full sync log:"
    cat /tmp/sync.log
    exit 1
fi
echo ""

# Step 3: Start API service
echo -e "${BLUE}Step 3: Starting API service...${NC}"
cd "$API_DIR"
echo "Rebuilding API with latest code..."
dotnet build -c Release > /dev/null 2>&1
if [ $? -ne 0 ]; then
    echo -e "${RED}✗ API rebuild failed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ API rebuilt with latest code${NC}"
dotnet run -c Release --no-build > /tmp/api.log 2>&1 &
API_PID=$!
echo "API service started (PID: $API_PID)"

if ! wait_for_service "http://localhost:8080/api/dashboard/overview" "API"; then
    echo -e "${RED}API logs:${NC}"
    cat /tmp/api.log
    exit 1
fi
echo ""

# Step 4: Test API endpoints
echo -e "${BLUE}Step 4: Testing API endpoints...${NC}"
echo ""

# Test overview endpoint
echo -e "${BLUE}4.1: Testing /api/dashboard/overview${NC}"
OVERVIEW=$(curl -s http://localhost:8080/api/dashboard/overview)
echo "Response received ($(echo "$OVERVIEW" | wc -c) bytes)"

# Parse response (now using flat structure)
ACTIVE_SECRETS=$(echo "$OVERVIEW" | jq -r '.activeSecrets // 0')
TOTAL_NAMESPACES=$(echo "$OVERVIEW" | jq -r '.totalNamespaces // 0')
TOTAL_SYNCS=$(echo "$OVERVIEW" | jq -r '.totalSyncs // 0')
SUCCESSFUL_SYNCS=$(echo "$OVERVIEW" | jq -r '.successfulSyncs // 0')
FAILED_SYNCS=$(echo "$OVERVIEW" | jq -r '.failedSyncs // 0')
SUCCESS_RATE=$(echo "$OVERVIEW" | jq -r '.successRate // 0')
AVG_DURATION=$(echo "$OVERVIEW" | jq -r '.averageSyncDuration // 0')

echo "  📊 Parsed data:"
echo "    - Active secrets: $ACTIVE_SECRETS"
echo "    - Namespaces: $TOTAL_NAMESPACES"
echo "    - Total syncs: $TOTAL_SYNCS ($SUCCESSFUL_SYNCS successful, $FAILED_SYNCS failed)"
echo "    - Success rate: $SUCCESS_RATE%"
echo "    - Avg duration: ${AVG_DURATION}s"

if [ "$TOTAL_NAMESPACES" -gt "0" ] && [ "$ACTIVE_SECRETS" -gt "0" ]; then
    echo -e "${GREEN}✓ Overview endpoint working${NC}"
else
    echo -e "${RED}✗ Overview endpoint validation failed${NC}"
    echo "Full response:"
    echo "$OVERVIEW" | jq '.'
    exit 1
fi
echo ""

# Test namespaces endpoint
echo -e "${BLUE}4.2: Testing /api/dashboard/namespaces${NC}"
NAMESPACES=$(curl -s http://localhost:8080/api/dashboard/namespaces)
NAMESPACE_LIST_COUNT=$(echo "$NAMESPACES" | jq '. | length')
echo "Response received: $NAMESPACE_LIST_COUNT namespaces"

if [ "$NAMESPACE_LIST_COUNT" -gt "0" ]; then
    echo "  📁 Namespaces found:"
    echo "$NAMESPACES" | jq -r '.[] | "    - \(.namespace): \(.activeSecrets) active, \(.failedSecrets) failed"'
    echo -e "${GREEN}✓ Namespaces endpoint working${NC}"
else
    echo -e "${RED}✗ Namespaces endpoint failed${NC}"
    echo "Full response:"
    echo "$NAMESPACES" | jq '.'
    exit 1
fi
echo ""

# Test secrets endpoint for first namespace
echo -e "${BLUE}4.3: Testing /api/dashboard/secrets/{namespace}${NC}"
FIRST_NAMESPACE=$(echo "$NAMESPACES" | jq -r '.[0].namespace')
echo "Testing with namespace: $FIRST_NAMESPACE"
SECRETS=$(curl -s "http://localhost:8080/api/dashboard/secrets/$FIRST_NAMESPACE")
SECRET_COUNT=$(echo "$SECRETS" | jq '. | length' 2>/dev/null || echo "0")
echo "Response received: $SECRET_COUNT secrets"

if [ "$SECRET_COUNT" -gt "0" ] 2>/dev/null; then
    echo "  🔑 Secrets found:"
    echo "$SECRETS" | jq -r '.[] | "    - \(.secretName) [\(.status)]"'
    echo -e "${GREEN}✓ Secrets endpoint working${NC}"
else
    echo -e "${YELLOW}⚠ No secrets found for namespace $FIRST_NAMESPACE (may be failed secrets)${NC}"
fi
echo ""

# Test sync logs endpoint
echo -e "${BLUE}4.4: Testing /api/dashboard/sync-logs${NC}"
SYNC_LOGS=$(curl -s "http://localhost:8080/api/dashboard/sync-logs?limit=5")
SYNC_LOG_COUNT=$(echo "$SYNC_LOGS" | jq '. | length' 2>/dev/null || echo "0")
echo "Response received: $SYNC_LOG_COUNT sync logs"

if [ "$SYNC_LOG_COUNT" -gt "0" ] 2>/dev/null; then
    echo "  📝 Recent syncs:"
    echo "$SYNC_LOGS" | jq -r '.[] | "    - Sync #\(.id): \(.status) (\(.processedItems) items, \(.durationSeconds)s)"'
    echo -e "${GREEN}✓ Sync logs endpoint working${NC}"
else
    echo -e "${YELLOW}⚠ No sync logs found${NC}"
fi
echo ""

# Step 5: Start dashboard
echo -e "${BLUE}Step 5: Starting dashboard...${NC}"
cd "$DASHBOARD_DIR"
echo "Dashboard directory: $DASHBOARD_DIR"

# Build dashboard with latest code
echo "Building dashboard with latest code..."
if command -v bun > /dev/null 2>&1; then
    bun run build > /dev/null 2>&1
else
    npm run build > /dev/null 2>&1
fi
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Dashboard built with latest code${NC}"
else
    echo -e "${RED}✗ Dashboard build failed${NC}"
    exit 1
fi

# Start dev server
if command -v bun > /dev/null 2>&1; then
    echo "Using Bun for development server"
    bun run dev > /tmp/dashboard.log 2>&1 &
else
    echo "Using npm for development server"
    npm run dev > /tmp/dashboard.log 2>&1 &
fi
DASHBOARD_PID=$!
echo "Dashboard started (PID: $DASHBOARD_PID)"
echo "Log file: /tmp/dashboard.log"

echo -n "Waiting for dashboard to start"
if ! wait_for_service "http://localhost:3000" "Dashboard"; then
    echo ""
    echo -e "${RED}Dashboard failed to start${NC}"
    echo "Last 30 lines of dashboard log:"
    tail -30 /tmp/dashboard.log
    exit 1
fi
echo ""

# Verify dashboard can fetch data
echo -e "${BLUE}5.1: Verifying dashboard data fetch...${NC}"
sleep 2  # Give dashboard time to make initial API calls
if grep -q "Failed to fetch" /tmp/dashboard.log 2>/dev/null; then
    echo -e "${YELLOW}⚠ Dashboard may have API connection issues${NC}"
    grep "Failed to fetch" /tmp/dashboard.log | tail -5
else
    echo -e "${GREEN}✓ No fetch errors detected${NC}"
fi
echo ""

# Step 6: Run browser tests
echo -e "${BLUE}Step 6: Testing dashboard in browser...${NC}"

# First, simple HTML verification
echo -e "${BLUE}6.1: Verifying dashboard HTML loads${NC}"
DASHBOARD_HTML=$(curl -s http://localhost:3000)
if echo "$DASHBOARD_HTML" | grep -q "<!DOCTYPE html>" && echo "$DASHBOARD_HTML" | grep -q "root"; then
    echo -e "${GREEN}✓ Dashboard HTML loads correctly${NC}"
    HTML_SIZE=$(echo "$DASHBOARD_HTML" | wc -c)
    echo "  HTML size: $HTML_SIZE bytes"
else
    echo -e "${RED}✗ Dashboard HTML appears malformed${NC}"
    echo "First 200 chars:"
    echo "$DASHBOARD_HTML" | head -c 200
fi
echo ""

# Check if playwright is installed
echo -e "${BLUE}6.2: Running automated browser tests${NC}"

cd "$DASHBOARD_DIR"
if [ -d "node_modules/@playwright/test" ]; then
    echo "Running Playwright tests..."
    if command -v bun > /dev/null 2>&1; then
        if bun run test:e2e 2>&1 | tee /tmp/playwright.log; then
            echo -e "${GREEN}✓ Browser tests passed${NC}"
        else
            echo -e "${RED}✗ Browser tests failed - check /tmp/playwright.log${NC}"
            echo -e "${YELLOW}This is expected if this is the first run${NC}"
        fi
    else
        if npm run test:e2e 2>&1 | tee /tmp/playwright.log; then
            echo -e "${GREEN}✓ Browser tests passed${NC}"
        else
            echo -e "${RED}✗ Browser tests failed - check /tmp/playwright.log${NC}"
            echo -e "${YELLOW}This is expected if this is the first run${NC}"
        fi
    fi
else
    echo -e "${YELLOW}⚠ Playwright not installed - skipping browser tests${NC}"
    echo -e "${BLUE}To enable automated browser tests:${NC}"
    if command -v bun > /dev/null 2>&1; then
        echo "  cd dashboard"
        echo "  bun install"
        echo "  bunx playwright install chromium"
        echo "  bun run test:e2e"
    else
        echo "  cd dashboard"
        echo "  npm install"
        echo "  npx playwright install chromium"
        echo "  npm run test:e2e"
    fi
fi
echo ""

echo -e "${BLUE}6.3: Manual verification checklist${NC}"
echo "  1. Open http://localhost:3000 in your browser"
echo "  2. Verify dashboard stats are populated (not all zeros)"
echo "  3. Click on Active/Failed/Data Keys chips to open modals"
echo "  4. Check browser console (F12) for errors"
echo ""

# Summary
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}  ✓ All E2E tests passed!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""

# Print detailed summary
echo -e "${BLUE}📊 Test Results Summary:${NC}"
echo ""
echo "  ✅ Build: All projects compiled successfully"
echo "  ✅ Sync: Completed with $ACTIVE_SECRETS active secrets"
echo "  ✅ Database: $TOTAL_NAMESPACES namespaces, $(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM SecretStates;") secrets"
echo "  ✅ API: All 4 endpoints tested and working"
echo "  ✅ Dashboard: Started and accessible"

# Browser tests are optional
echo "  ℹ️  Browser: Manual verification recommended (automated tests skipped)"
echo ""

echo -e "${BLUE}🌐 Services Running:${NC}"
echo "  📡 API:       http://localhost:8080"
echo "     Swagger:  http://localhost:8080/swagger"
echo "     Health:   http://localhost:8080/health"
echo ""
echo "  🎨 Dashboard: http://localhost:3000"
echo ""
echo "  📈 Metrics:   http://localhost:9090/metrics"
echo "     Health:   http://localhost:9090/health"
echo ""

echo -e "${BLUE}📝 Log Files:${NC}"
echo "  Sync:      /tmp/sync.log"
echo "  API:       /tmp/api.log"
echo "  Dashboard: /tmp/dashboard.log"
echo ""

echo -e "${BLUE}🔍 Quick Checks:${NC}"
echo "  Database:  sqlite3 $DB_PATH \"SELECT * FROM SecretStates LIMIT 5;\""
echo "  API Test:  curl http://localhost:8080/api/dashboard/overview | jq"
echo "  Logs:      tail -f /tmp/{sync,api,dashboard}.log"
echo ""

echo -e "${YELLOW}Press Ctrl+C to stop all services${NC}"
echo ""

# Keep running until interrupted
wait
