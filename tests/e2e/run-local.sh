#!/bin/bash
set -e

# ============================================================================
# Local E2E Test Runner (without Docker container)
# ============================================================================
# Prerequisites:
# - Docker running
# - kind installed
# - kubectl installed
# - helm installed
# - Python 3.9+ with pip
# - Bitwarden CLI (bw) installed
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

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
echo "║     Vaultwarden Kubernetes Secrets - Local E2E Test Runner       ║"
echo "╚══════════════════════════════════════════════════════════════════╝"
echo ""

# Check prerequisites
log_info "Checking prerequisites..."

check_command() {
    if ! command -v "$1" &> /dev/null; then
        log_error "$1 is required but not installed"
        exit 1
    fi
    log_success "$1 found"
}

check_command docker
check_command kind
check_command kubectl
check_command helm
check_command python3
check_command bw

# Check Python dependencies
log_info "Checking Python dependencies..."
python3 -c "import requests" 2>/dev/null || {
    log_warn "Installing Python dependencies..."
    pip3 install --user requests pyyaml python-dateutil colorama
}

# Check if Docker is running
if ! docker info &> /dev/null; then
    log_error "Docker is not running"
    exit 1
fi
log_success "Docker is running"

# Install cryptography if needed (for user registration)
python3 -c "from cryptography.hazmat.primitives.ciphers import Cipher" 2>/dev/null || {
    log_warn "Installing cryptography package..."
    pip3 install --user cryptography
}

echo ""
log_info "All prerequisites satisfied!"
echo ""

# Set environment variables
export E2E_CLUSTER_NAME="${E2E_CLUSTER_NAME:-vks-e2e-local}"
export E2E_KEEP_CLUSTER="${E2E_KEEP_CLUSTER:-false}"
export E2E_VERBOSE="${E2E_VERBOSE:-false}"

# Run the main test script
exec "$SCRIPT_DIR/run-e2e-tests.sh" "$@"
