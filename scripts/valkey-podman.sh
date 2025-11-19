#!/bin/bash

# Helper script to manage Valkey with Podman/Docker for Vaultwarden K8s Sync
# Valkey is a Redis-compatible fork (https://valkey.io)

set -e

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

CONTAINER_NAME="vaultwarden-valkey"
CONTAINER_RUNTIME=""

# Detect container runtime
if command -v podman > /dev/null; then
    CONTAINER_RUNTIME="podman"
elif command -v docker > /dev/null; then
    CONTAINER_RUNTIME="docker"
else
    echo -e "${RED}Error: Neither Podman nor Docker found${NC}"
    echo "Install Podman: sudo apt-get install podman"
    exit 1
fi

start_valkey() {
    echo "Starting Valkey with $CONTAINER_RUNTIME..."
    
    # Check if already running
    if $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME --format "{{.Names}}" 2>/dev/null | grep -q $CONTAINER_NAME; then
        echo -e "${YELLOW}Valkey is already running${NC}"
        return 0
    fi
    
    # Remove any stopped container with the same name
    $CONTAINER_RUNTIME rm -f $CONTAINER_NAME 2>/dev/null || true
    
    # Start Valkey (Redis-compatible)
    # Use fully qualified image name for Podman compatibility
    if [ "$CONTAINER_RUNTIME" = "podman" ]; then
        IMAGE="docker.io/valkey/valkey:alpine"
    else
        IMAGE="valkey/valkey:alpine"
    fi
    
    $CONTAINER_RUNTIME run -d \
        --name $CONTAINER_NAME \
        --rm \
        -p 6379:6379 \
        $IMAGE
    
    echo -e "${GREEN}✓ Valkey started successfully${NC}"
    echo "Connection: localhost:6379"
    echo ""
    echo "Set environment variable:"
    echo "  export VALKEY_CONNECTION=localhost:6379"
}

stop_valkey() {
    echo "Stopping Valkey..."
    
    if $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME --format "{{.Names}}" 2>/dev/null | grep -q $CONTAINER_NAME; then
        $CONTAINER_RUNTIME stop $CONTAINER_NAME
        echo -e "${GREEN}✓ Valkey stopped${NC}"
    else
        echo -e "${YELLOW}Valkey is not running${NC}"
    fi
}

status_valkey() {
    if $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME --format "{{.Names}}" 2>/dev/null | grep -q $CONTAINER_NAME; then
        echo -e "${GREEN}✓ Valkey is running${NC}"
        
        # Try to ping (valkey-cli is compatible with redis-cli)
        if command -v valkey-cli > /dev/null; then
            if valkey-cli ping > /dev/null 2>&1; then
                echo -e "${GREEN}✓ Valkey is responding to PING${NC}"
            else
                echo -e "${RED}✗ Valkey container running but not responding${NC}"
            fi
        elif command -v redis-cli > /dev/null; then
            if redis-cli ping > /dev/null 2>&1; then
                echo -e "${GREEN}✓ Valkey is responding to PING (via redis-cli)${NC}"
            else
                echo -e "${RED}✗ Valkey container running but not responding${NC}"
            fi
        fi
        
        echo ""
        echo "Container details:"
        $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME
    else
        echo -e "${RED}✗ Valkey is not running${NC}"
    fi
}

logs_valkey() {
    if $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME --format "{{.Names}}" 2>/dev/null | grep -q $CONTAINER_NAME; then
        $CONTAINER_RUNTIME logs -f $CONTAINER_NAME
    else
        echo -e "${RED}Valkey is not running${NC}"
    fi
}

case "${1:-}" in
    start)
        start_valkey
        ;;
    stop)
        stop_valkey
        ;;
    restart)
        stop_valkey
        sleep 1
        start_valkey
        ;;
    status)
        status_valkey
        ;;
    logs)
        logs_valkey
        ;;
    *)
        echo "Usage: $0 {start|stop|restart|status|logs}"
        echo ""
        echo "Commands:"
        echo "  start    - Start Valkey in a container"
        echo "  stop     - Stop Valkey container"
        echo "  restart  - Restart Valkey container"
        echo "  status   - Check if Valkey is running"
        echo "  logs     - Show Valkey logs (follow mode)"
        exit 1
        ;;
esac
