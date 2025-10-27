#!/bin/bash

# Helper script to manage Redis with Podman for Vaultwarden K8s Sync

set -e

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

CONTAINER_NAME="vaultwarden-redis"
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

start_redis() {
    echo "Starting Redis with $CONTAINER_RUNTIME..."
    
    # Check if already running
    if $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME --format "{{.Names}}" 2>/dev/null | grep -q $CONTAINER_NAME; then
        echo -e "${YELLOW}Redis is already running${NC}"
        return 0
    fi
    
    # Remove any stopped container with the same name
    $CONTAINER_RUNTIME rm -f $CONTAINER_NAME 2>/dev/null || true
    
    # Start Redis
    # Use fully qualified image name for Podman compatibility
    if [ "$CONTAINER_RUNTIME" = "podman" ]; then
        IMAGE="docker.io/library/redis:alpine"
    else
        IMAGE="redis:alpine"
    fi
    
    $CONTAINER_RUNTIME run -d \
        --name $CONTAINER_NAME \
        --rm \
        -p 6379:6379 \
        $IMAGE
    
    echo -e "${GREEN}✓ Redis started successfully${NC}"
    echo "Connection: localhost:6379"
    echo ""
    echo "Set environment variable:"
    echo "  export REDIS_CONNECTION=localhost:6379"
}

stop_redis() {
    echo "Stopping Redis..."
    
    if $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME --format "{{.Names}}" 2>/dev/null | grep -q $CONTAINER_NAME; then
        $CONTAINER_RUNTIME stop $CONTAINER_NAME
        echo -e "${GREEN}✓ Redis stopped${NC}"
    else
        echo -e "${YELLOW}Redis is not running${NC}"
    fi
}

status_redis() {
    if $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME --format "{{.Names}}" 2>/dev/null | grep -q $CONTAINER_NAME; then
        echo -e "${GREEN}✓ Redis is running${NC}"
        
        # Try to ping
        if command -v redis-cli > /dev/null; then
            if redis-cli ping > /dev/null 2>&1; then
                echo -e "${GREEN}✓ Redis is responding to PING${NC}"
            else
                echo -e "${RED}✗ Redis container running but not responding${NC}"
            fi
        fi
        
        echo ""
        echo "Container details:"
        $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME
    else
        echo -e "${RED}✗ Redis is not running${NC}"
    fi
}

logs_redis() {
    if $CONTAINER_RUNTIME ps --filter name=$CONTAINER_NAME --format "{{.Names}}" 2>/dev/null | grep -q $CONTAINER_NAME; then
        $CONTAINER_RUNTIME logs -f $CONTAINER_NAME
    else
        echo -e "${RED}Redis is not running${NC}"
    fi
}

case "${1:-}" in
    start)
        start_redis
        ;;
    stop)
        stop_redis
        ;;
    restart)
        stop_redis
        sleep 1
        start_redis
        ;;
    status)
        status_redis
        ;;
    logs)
        logs_redis
        ;;
    *)
        echo "Usage: $0 {start|stop|restart|status|logs}"
        echo ""
        echo "Commands:"
        echo "  start    - Start Redis in a container"
        echo "  stop     - Stop Redis container"
        echo "  restart  - Restart Redis container"
        echo "  status   - Check if Redis is running"
        echo "  logs     - Show Redis logs (follow mode)"
        exit 1
        ;;
esac
