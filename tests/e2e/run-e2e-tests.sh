#!/bin/bash
set -e

# ============================================================================
# Vaultwarden Kubernetes Secrets - End-to-End Test Suite
# ============================================================================
# This script runs the C# E2E tests using dotnet test.
# The tests handle all setup (kind cluster, vaultwarden, operator) internally.
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Configuration
export E2E_KEEP_CLUSTER="${E2E_KEEP_CLUSTER:-false}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }

echo ""
echo "╔══════════════════════════════════════════════════════════════════╗"
echo "║     Vaultwarden Kubernetes Secrets - E2E Test Suite (C#)         ║"
echo "╚══════════════════════════════════════════════════════════════════╝"
echo ""

# Check prerequisites
log_info "Checking prerequisites..."
command -v docker >/dev/null 2>&1 || { log_error "docker is required"; exit 1; }
command -v dotnet >/dev/null 2>&1 || { log_error "dotnet is required"; exit 1; }
command -v kind >/dev/null 2>&1 || { log_error "kind is required"; exit 1; }
command -v kubectl >/dev/null 2>&1 || { log_error "kubectl is required"; exit 1; }
command -v helm >/dev/null 2>&1 || { log_error "helm is required"; exit 1; }
command -v bw >/dev/null 2>&1 || { log_error "bw (Bitwarden CLI) is required"; exit 1; }
log_success "All prerequisites found"

# Build the E2E test project
log_info "Building E2E test project..."
cd "$PROJECT_ROOT"
dotnet build VaultwardenK8sSync.E2ETests/VaultwardenK8sSync.E2ETests.csproj -c Release

# Run E2E tests
log_info "Running E2E tests..."
dotnet test VaultwardenK8sSync.E2ETests/VaultwardenK8sSync.E2ETests.csproj \
    -c Release \
    --no-build \
    --logger "console;verbosity=detailed" \
    --logger "trx;LogFileName=e2e-results.trx"

EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
    log_success "All E2E tests passed!"
else
    log_error "Some E2E tests failed (exit code: $EXIT_CODE)"
fi

exit $EXIT_CODE
