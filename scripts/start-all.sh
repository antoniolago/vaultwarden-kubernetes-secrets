#!/bin/bash
set -e

# Colors
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# PIDs for cleanup
API_PID=""
DASHBOARD_PID=""

# Cleanup function
cleanup() {
    echo -e "\n${YELLOW}Stopping all services...${NC}"
    [ -n "$DASHBOARD_PID" ] && kill $DASHBOARD_PID 2>/dev/null || true
    [ -n "$API_PID" ] && kill $API_PID 2>/dev/null || true
    
    # Kill any remaining processes on the ports
    lsof -ti:8080 | xargs kill -9 2>/dev/null || true
    lsof -ti:3000 | xargs kill -9 2>/dev/null || true
    lsof -ti:9090 | xargs kill -9 2>/dev/null || true
    
    echo -e "${GREEN}All services stopped${NC}"
}

trap cleanup EXIT INT TERM

echo -e "${BLUE}🚀 Starting Vaultwarden K8s Sync Stack...${NC}"
echo ""

# Check if database exists, if not run sync first
DB_PATH="$PROJECT_ROOT/VaultwardenK8sSync/data/sync.db"
if [ ! -f "$DB_PATH" ]; then
    echo -e "${YELLOW}⚠️  Database not found, running initial sync...${NC}"
    cd "$PROJECT_ROOT/VaultwardenK8sSync"
    dotnet run sync
    echo ""
fi

# Start API
echo -e "${BLUE}🌐 Starting API...${NC}"
cd "$PROJECT_ROOT/VaultwardenK8sSync.Api"
dotnet run > /tmp/vk8s-api.log 2>&1 &
API_PID=$!
echo "API started (PID: $API_PID)"

# Wait for API
echo -n "Waiting for API to be ready"
for i in {1..30}; do
    if curl -s -f http://localhost:8080/api/dashboard/overview > /dev/null 2>&1; then
        echo -e " ${GREEN}✓${NC}"
        break
    fi
    echo -n "."
    sleep 1
done
echo ""

# Start dashboard
echo -e "${BLUE}💻 Starting Dashboard...${NC}"
cd "$PROJECT_ROOT/dashboard"
if [ -f "bun.lockb" ]; then
    bun run dev > /tmp/vk8s-dashboard.log 2>&1 &
else
    npm run dev > /tmp/vk8s-dashboard.log 2>&1 &
fi
DASHBOARD_PID=$!
echo "Dashboard started (PID: $DASHBOARD_PID)"

# Wait for dashboard
echo -n "Waiting for dashboard to be ready"
for i in {1..30}; do
    if curl -s -f http://localhost:3000 > /dev/null 2>&1; then
        echo -e " ${GREEN}✓${NC}"
        break
    fi
    echo -n "."
    sleep 1
done
echo ""

echo -e "${GREEN}✅ All services started!${NC}"
echo ""
echo -e "${BLUE}📊 Dashboard:${NC} http://localhost:3000"
echo -e "${BLUE}🔌 API:${NC}       http://localhost:8080"
echo -e "${BLUE}📈 Swagger:${NC}   http://localhost:8080/swagger"
echo ""
echo -e "${YELLOW}Logs:${NC}"
echo -e "  API:       tail -f /tmp/vk8s-api.log"
echo -e "  Dashboard: tail -f /tmp/vk8s-dashboard.log"
echo ""
echo -e "${YELLOW}Press Ctrl+C to stop all services${NC}"
echo ""

# Open browser
if command -v xdg-open > /dev/null; then
    xdg-open http://localhost:3000 2>/dev/null &
elif command -v open > /dev/null; then
    open http://localhost:3000 2>/dev/null &
fi

# Wait for all background processes
wait
