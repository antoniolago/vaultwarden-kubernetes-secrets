#!/bin/bash
# Note: Not using 'set -e' to allow graceful error handling and reporting

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
SYNC_PID=""
VALKEY_STARTED=false

# Cleanup function
cleanup() {
    echo -e "\n${YELLOW}Stopping all services...${NC}"
    [ -n "$SYNC_PID" ] && kill $SYNC_PID 2>/dev/null || true
    [ -n "$DASHBOARD_PID" ] && kill $DASHBOARD_PID 2>/dev/null || true
    [ -n "$API_PID" ] && kill $API_PID 2>/dev/null || true
    
    # Stop Valkey if we started it
    if [ "$VALKEY_STARTED" = true ]; then
        echo "Stopping Valkey..."
        # Try container runtimes first
        if command -v podman > /dev/null && podman ps -q --filter name=vaultwarden-valkey 2>/dev/null | grep -q .; then
            podman stop vaultwarden-valkey 2>/dev/null || true
        elif command -v docker > /dev/null && docker ps -q --filter name=vaultwarden-valkey 2>/dev/null | grep -q .; then
            docker stop vaultwarden-valkey 2>/dev/null || true
        else
            # Native valkey-server (or redis-cli for compatibility)
            redis-cli shutdown 2>/dev/null || valkey-cli shutdown 2>/dev/null || true
        fi
    fi
    
    # Kill any remaining processes on the ports
    lsof -ti:8080 | xargs kill -9 2>/dev/null || true
    lsof -ti:3000 | xargs kill -9 2>/dev/null || true
    lsof -ti:9090 | xargs kill -9 2>/dev/null || true
    
    echo -e "${GREEN}All services stopped${NC}"
}

trap cleanup EXIT INT TERM

echo -e "${BLUE}🚀 Starting Vaultwarden K8s Sync Stack...${NC}"
echo ""

# Kill any existing processes on the ports to prevent duplicates
echo -e "${YELLOW}🧹 Cleaning up any existing processes...${NC}"
lsof -ti:8080 | xargs kill -9 2>/dev/null || true
lsof -ti:3000 | xargs kill -9 2>/dev/null || true
lsof -ti:9090 | xargs kill -9 2>/dev/null || true

# Clean up process lock file that may be left from crashed sync service
LOCK_FILE="/tmp/vaultwarden-sync.lock"
if [ -f "$LOCK_FILE" ]; then
    echo "Removing orphaned process lock file..."
    rm -f "$LOCK_FILE" || true
fi

# Give processes time to die
sleep 1
echo -e "${GREEN}✓ Cleanup complete${NC}"
echo ""

# Check and start Valkey for WebSocket sync output
echo -e "${BLUE}🔴 Checking Valkey...${NC}"
if ! redis-cli ping > /dev/null 2>&1 && ! valkey-cli ping > /dev/null 2>&1; then
    echo "Valkey not running, attempting to start..."
    
    # Try Podman first
    if command -v podman > /dev/null; then
        echo "Starting Valkey with Podman..."
        
        # Remove any existing stopped container
        podman rm -f vaultwarden-valkey 2>/dev/null || true
        
        # Try to start Valkey and capture any errors
        # Use fully qualified image name to avoid Podman short-name resolution issues
        PODMAN_ERROR=$(podman run -d \
            --name vaultwarden-valkey \
            --rm \
            -p 6379:6379 \
            docker.io/valkey/valkey:alpine 2>&1)
        PODMAN_EXIT=$?
        
        if [ $PODMAN_EXIT -eq 0 ]; then
            sleep 2
            # Verify container is actually running
            if podman ps --filter name=vaultwarden-valkey --format "{{.Names}}" 2>/dev/null | grep -q vaultwarden-valkey; then
                echo -e "${GREEN}✓ Valkey started successfully (Podman)${NC}"
                VALKEY_STARTED=true
                
                # Optional: Try to verify with valkey-cli or redis-cli if available
                if command -v valkey-cli > /dev/null && valkey-cli ping > /dev/null 2>&1; then
                    echo -e "${GREEN}  Valkey is responding to PING${NC}"
                elif command -v redis-cli > /dev/null && redis-cli ping > /dev/null 2>&1; then
                    echo -e "${GREEN}  Valkey is responding to PING (via redis-cli)${NC}"
                fi
            else
                echo -e "${YELLOW}⚠️  Container started but not running${NC}"
            fi
        else
            echo -e "${YELLOW}⚠️  Could not start Valkey via Podman${NC}"
            echo -e "${YELLOW}   Error: ${PODMAN_ERROR}${NC}"
        fi
    # Try Docker as fallback
    elif command -v docker > /dev/null; then
        echo "Starting Valkey with Docker..."
        
        # Remove any existing stopped container
        docker rm -f vaultwarden-valkey 2>/dev/null || true
        
        # Try to start Valkey and capture any errors
        DOCKER_ERROR=$(docker run -d \
            --name vaultwarden-valkey \
            --rm \
            -p 6379:6379 \
            valkey/valkey:alpine 2>&1)
        DOCKER_EXIT=$?
        
        if [ $DOCKER_EXIT -eq 0 ]; then
            sleep 2
            # Verify container is actually running
            if docker ps --filter name=vaultwarden-valkey --format "{{.Names}}" 2>/dev/null | grep -q vaultwarden-valkey; then
                echo -e "${GREEN}✓ Valkey started successfully (Docker)${NC}"
                VALKEY_STARTED=true
                
                # Optional: Try to verify with valkey-cli or redis-cli if available
                if command -v valkey-cli > /dev/null && valkey-cli ping > /dev/null 2>&1; then
                    echo -e "${GREEN}  Valkey is responding to PING${NC}"
                elif command -v redis-cli > /dev/null && redis-cli ping > /dev/null 2>&1; then
                    echo -e "${GREEN}  Valkey is responding to PING (via redis-cli)${NC}"
                fi
            else
                echo -e "${YELLOW}⚠️  Container started but not running${NC}"
            fi
        else
            echo -e "${YELLOW}⚠️  Could not start Valkey via Docker${NC}"
            echo -e "${YELLOW}   Error: ${DOCKER_ERROR}${NC}"
        fi
    # Try native valkey-server or redis-server as last resort
    elif command -v valkey-server > /dev/null || command -v redis-server > /dev/null; then
        echo "Starting Valkey natively..."
        if command -v valkey-server > /dev/null; then
            valkey-server --daemonize yes --port 6379 > /dev/null 2>&1
        else
            redis-server --daemonize yes --port 6379 > /dev/null 2>&1
        fi
        sleep 1
        if valkey-cli ping > /dev/null 2>&1 || redis-cli ping > /dev/null 2>&1; then
            echo -e "${GREEN}✓ Valkey started successfully${NC}"
            VALKEY_STARTED=true
        else
            echo -e "${YELLOW}⚠️  Could not start Valkey - WebSocket output will be unavailable${NC}"
        fi
    else
        echo -e "${YELLOW}⚠️  No container runtime found - WebSocket output will be unavailable${NC}"
        echo -e "${YELLOW}   Install Podman: sudo apt-get install podman${NC}"
        echo -e "${YELLOW}   Or Docker: sudo apt-get install docker.io${NC}"
    fi
else
    echo -e "${GREEN}✓ Valkey is already running${NC}"
fi
echo ""

# Check if database exists, if not run sync first
DB_PATH="$PROJECT_ROOT/VaultwardenK8sSync/data/sync.db"
if [ ! -f "$DB_PATH" ]; then
    echo -e "${YELLOW}⚠️  Database not found, running initial sync...${NC}"
    cd "$PROJECT_ROOT/VaultwardenK8sSync"
    dotnet run sync
    echo ""
fi

# Build and start API
echo -e "${BLUE}🌐 Building and starting API...${NC}"
cd "$PROJECT_ROOT/VaultwardenK8sSync.Api"
echo "Building API with latest code..."
dotnet build -c Release > /dev/null 2>&1
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ API built successfully${NC}"
else
    echo -e "${RED}✗ API build failed${NC}"
    exit 1
fi

# Export Valkey connection for API
export VALKEY_CONNECTION="${VALKEY_CONNECTION:-localhost:6379}"
echo "Using Valkey at: $VALKEY_CONNECTION"

dotnet run -c Release --no-build > /tmp/vk8s-api.log 2>&1 &
API_PID=$!
echo "API started (PID: $API_PID)"

# Wait for API
echo -n "Waiting for API to be ready"
for i in {1..30}; do
    # Check if process is still running
    if ! kill -0 $API_PID 2>/dev/null; then
        echo -e " ${RED}✗${NC}"
        echo -e "${RED}API process died. Check logs: tail -f /tmp/vk8s-api.log${NC}"
        cat /tmp/vk8s-api.log
        exit 1
    fi
    
    # Try health endpoint first
    if curl -s -f http://localhost:8080/health > /dev/null 2>&1; then
        echo -e " ${GREEN}✓${NC}"
        break
    fi
    echo -n "."
    sleep 1
done

# Final check if we timed out
if ! curl -s -f http://localhost:8080/health > /dev/null 2>&1; then
    echo -e " ${RED}✗ Timeout${NC}"
    echo -e "${YELLOW}API logs:${NC}"
    tail -20 /tmp/vk8s-api.log
    echo ""
fi
echo ""

# Start sync service
echo -e "${BLUE}🔄 Starting Sync Service...${NC}"
cd "$PROJECT_ROOT/VaultwardenK8sSync"
echo "Building sync service..."
dotnet build -c Release > /dev/null 2>&1
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Sync service built successfully${NC}"
else
    echo -e "${RED}✗ Sync service build failed${NC}"
    exit 1
fi
dotnet run -c Release --no-build > /tmp/vk8s-sync.log 2>&1 &
SYNC_PID=$!
echo "Sync service started (PID: $SYNC_PID)"
echo ""

# Build and start dashboard
echo -e "${BLUE}💻 Building and starting Dashboard...${NC}"
cd "$PROJECT_ROOT/dashboard"
echo "Building dashboard with latest code..."
if [ -f "bun.lockb" ]; then
    bun run build > /dev/null 2>&1
else
    npm run build > /dev/null 2>&1
fi
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Dashboard built successfully${NC}"
else
    echo -e "${RED}✗ Dashboard build failed${NC}"
    exit 1
fi

# Start dev server
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
if redis-cli ping > /dev/null 2>&1; then
    echo -e "${BLUE}🔴 Redis:${NC}     localhost:6379 (running)"
fi
echo ""
echo -e "${YELLOW}Logs:${NC}"
echo -e "  Sync:      tail -f /tmp/vk8s-sync.log"
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
