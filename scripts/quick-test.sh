#!/bin/bash
# Quick test script - just verifies the core functionality without browser tests

set -e

GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

echo -e "${BLUE}🚀 Quick E2E Test${NC}"
echo ""

# 1. Check database
echo -e "${BLUE}1. Checking database...${NC}"
DB_PATH="$PROJECT_ROOT/VaultwardenK8sSync/data/sync.db"
if [ -f "$DB_PATH" ]; then
    SECRET_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM SecretStates;" 2>/dev/null || echo "0")
    NAMESPACE_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(DISTINCT Namespace) FROM SecretStates;" 2>/dev/null || echo "0")
    echo -e "${GREEN}✓ Database exists${NC}"
    echo "  Secrets: $SECRET_COUNT"
    echo "  Namespaces: $NAMESPACE_COUNT"
else
    echo -e "${RED}✗ Database not found${NC}"
    echo "Run: cd VaultwardenK8sSync && dotnet run sync"
    exit 1
fi
echo ""

# 2. Test API
echo -e "${BLUE}2. Testing API...${NC}"
if curl -s -f http://localhost:8080/api/dashboard/overview > /dev/null 2>&1; then
    OVERVIEW=$(curl -s http://localhost:8080/api/dashboard/overview)
    ACTIVE=$(echo "$OVERVIEW" | jq -r '.activeSecretsCount // 0')
    NS_COUNT=$(echo "$OVERVIEW" | jq -r '.secretsByNamespace | length // 0')
    echo -e "${GREEN}✓ API responding${NC}"
    echo "  Active secrets: $ACTIVE"
    echo "  Namespaces: $NS_COUNT"
else
    echo -e "${RED}✗ API not responding${NC}"
    echo "Start API: cd VaultwardenK8sSync.Api && dotnet run"
    exit 1
fi
echo ""

# 3. Test Dashboard
echo -e "${BLUE}3. Testing Dashboard...${NC}"
if curl -s -f http://localhost:3000 > /dev/null 2>&1; then
    HTML=$(curl -s http://localhost:3000)
    if echo "$HTML" | grep -q "<!DOCTYPE html>"; then
        echo -e "${GREEN}✓ Dashboard responding${NC}"
        echo "  URL: http://localhost:3000"
    else
        echo -e "${YELLOW}⚠ Dashboard responding but HTML looks wrong${NC}"
    fi
else
    echo -e "${RED}✗ Dashboard not responding${NC}"
    echo "Start dashboard: cd dashboard && npm run dev"
    exit 1
fi
echo ""

# Summary
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}  ✓ Quick test passed!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""
echo "All services are working:"
echo "  📡 API:       http://localhost:8080"
echo "  🎨 Dashboard: http://localhost:3000"
echo ""
echo "Open dashboard in browser to verify visually"
