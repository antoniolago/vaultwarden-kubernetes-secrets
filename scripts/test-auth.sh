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

echo -e "${BLUE}╔════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║   Authentication Test Suite                           ║${NC}"
echo -e "${BLUE}╚════════════════════════════════════════════════════════╝${NC}"
echo ""

# Function to print section headers
print_section() {
    echo -e "\n${BLUE}▶ $1${NC}"
    echo -e "${BLUE}$(printf '─%.0s' {1..60})${NC}"
}

# Function to print test results
print_result() {
    if [ $1 -eq 0 ]; then
        echo -e "${GREEN}✓ $2${NC}"
    else
        echo -e "${RED}✗ $2${NC}"
        return 1
    fi
}

# Track overall status
OVERALL_STATUS=0

# 1. Unit Tests
print_section "Running Unit Tests"
echo -e "${YELLOW}Testing authentication middleware...${NC}"
cd "$PROJECT_ROOT"
if dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj \
    --filter "FullyQualifiedName~AuthenticationMiddlewareTests" \
    --verbosity minimal; then
    print_result 0 "Authentication middleware unit tests passed"
else
    print_result 1 "Authentication middleware unit tests failed"
    OVERALL_STATUS=1
fi

# 2. Integration Tests
print_section "Running Integration Tests"
echo -e "${YELLOW}Testing API authentication integration...${NC}"
if dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj \
    --filter "FullyQualifiedName~ApiAuthenticationIntegrationTests" \
    --verbosity minimal; then
    print_result 0 "API authentication integration tests passed"
else
    print_result 1 "API authentication integration tests failed"
    OVERALL_STATUS=1
fi

# 3. Helm Chart Tests
print_section "Running Helm Chart Tests"

# Check if helm-unittest plugin is installed
if ! helm plugin list | grep -q unittest; then
    echo -e "${YELLOW}Installing helm-unittest plugin...${NC}"
    helm plugin install https://github.com/helm-unittest/helm-unittest.git
fi

echo -e "${YELLOW}Testing authentication secret generation...${NC}"
cd "$PROJECT_ROOT/charts/vaultwarden-kubernetes-secrets"
if helm unittest . -f 'tests/auth-secret-test.yaml'; then
    print_result 0 "Auth secret Helm tests passed"
else
    print_result 1 "Auth secret Helm tests failed"
    OVERALL_STATUS=1
fi

echo -e "${YELLOW}Testing deployment authentication configuration...${NC}"
if helm unittest . -f 'tests/deployment-auth-test.yaml'; then
    print_result 0 "Deployment auth Helm tests passed"
else
    print_result 1 "Deployment auth Helm tests failed"
    OVERALL_STATUS=1
fi

# 4. Test Coverage Report
print_section "Test Coverage Summary"
cd "$PROJECT_ROOT"
echo -e "${YELLOW}Generating coverage report for authentication tests...${NC}"
dotnet test VaultwardenK8sSync.Tests/VaultwardenK8sSync.Tests.csproj \
    --filter "FullyQualifiedName~Authentication" \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    --verbosity quiet || true

if [ -f "VaultwardenK8sSync.Tests/coverage.cobertura.xml" ]; then
    echo -e "${GREEN}Coverage report generated: VaultwardenK8sSync.Tests/coverage.cobertura.xml${NC}"
fi

# 5. Test Scenarios Summary
print_section "Test Scenarios Covered"
cat << EOF
${GREEN}✓${NC} Health endpoint bypasses authentication
${GREEN}✓${NC} Loginless mode disables authentication
${GREEN}✓${NC} Valid token authentication
${GREEN}✓${NC} Invalid token rejection (401)
${GREEN}✓${NC} Missing authorization header rejection
${GREEN}✓${NC} Malformed authorization header rejection
${GREEN}✓${NC} Case-insensitive health endpoint
${GREEN}✓${NC} Configuration priority (health > loginless > token)
${GREEN}✓${NC} Concurrent requests with same token
${GREEN}✓${NC} Helm secret generation with auto-token
${GREEN}✓${NC} Helm secret generation with custom token
${GREEN}✓${NC} Helm deployment environment variable injection
${GREEN}✓${NC} Auth secret persistence across upgrades
${GREEN}✓${NC} Resource policy 'keep' annotation
EOF

# Final Results
print_section "Final Results"
if [ $OVERALL_STATUS -eq 0 ]; then
    echo -e "${GREEN}╔════════════════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║  ✓ All Authentication Tests Passed!                   ║${NC}"
    echo -e "${GREEN}╚════════════════════════════════════════════════════════╝${NC}"
    echo ""
    echo -e "${BLUE}Next steps:${NC}"
    echo -e "  • Run ${YELLOW}dotnet test${NC} to execute all tests"
    echo -e "  • Run ${YELLOW}helm install${NC} to deploy with auto-generated token"
    echo -e "  • Check ${YELLOW}kubectl get secret${NC} to view the auth token"
else
    echo -e "${RED}╔════════════════════════════════════════════════════════╗${NC}"
    echo -e "${RED}║  ✗ Some Authentication Tests Failed                    ║${NC}"
    echo -e "${RED}╚════════════════════════════════════════════════════════╝${NC}"
    echo ""
    echo -e "${YELLOW}Review the errors above and fix the failing tests.${NC}"
fi

exit $OVERALL_STATUS
