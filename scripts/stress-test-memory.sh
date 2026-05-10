#!/bin/bash
set -e

# ============================================================================
# Memory Stress Test for Vaultwarden Kubernetes Secrets Sync
# ============================================================================
# Creates N vaultwarden items and runs the sync to measure memory usage at
# each phase via the [MEMORY] instrumentation logs.
#
# Usage:
#   ./scripts/stress-test-memory.sh [--items N] [--image TAG]
#
# Defaults: --items 100, uses local docker compose e2e stack
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Defaults
ITEM_COUNT=100
SYNC_IMAGE="vaultwarden-kubernetes-secrets:e2e-stress"
VW_VERSION="1.30.1"  # stable baseline
E2E_SSL_DIR="$PROJECT_ROOT/e2e-ssl"
MOCK_KUBECONFIG="$PROJECT_ROOT/mock-kubeconfig.yaml"
E2E_HELPER="$SCRIPT_DIR/e2e-helper.py"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

log()     { echo -e "${BLUE}[INFO]${NC} $1"; }
log_ok()  { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn(){ echo -e "${YELLOW}[WARN]${NC} $1"; }
log_err() { echo -e "${RED}[ERROR]${NC} $1"; }
header()  { echo -e "\n${CYAN}══════════════════════════════════════════$NC"; echo -e "${CYAN}  $1${NC}"; echo -e "${CYAN}══════════════════════════════════════════$NC"; }

# Parse args
while [[ $# -gt 0 ]]; do
  case "$1" in
    --items) ITEM_COUNT="$2"; shift 2 ;;
    --image) SYNC_IMAGE="$2"; shift 2 ;;
    --version) VW_VERSION="$2"; shift 2 ;;
    --help)
      echo "Usage: $0 [--items N] [--image TAG] [--version X.Y.Z]"
      echo "  --items N     Number of vault items to create (default: 100)"
      echo "  --image TAG   Docker image tag for sync service (default: vaultwarden-kubernetes-secrets:e2e-stress)"
      echo "  --version     Vaultwarden version (default: 1.30.1)"
      exit 0 ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
done

# ── Prerequisites ──────────────────────────────────────────────────────────
header "Checking prerequisites"

for cmd in docker python3 openssl; do
  command -v "$cmd" >/dev/null 2>&1 || { log_err "$cmd is required"; exit 1; }
done

# Check Python cryptography
python3 -c "from cryptography.hazmat.primitives.ciphers import Cipher" 2>/dev/null || {
  log_warn "Installing cryptography package..."
  pip3 install --user cryptography 2>/dev/null || {
    log_err "Failed to install cryptography. Run: pip3 install --user cryptography"
    exit 1
  }
}
log_ok "Prerequisites satisfied"

# ── Build sync image ──────────────────────────────────────────────────────
header "Building sync service image ($SYNC_IMAGE)"

cd "$PROJECT_ROOT"

# Check if image already exists
if docker image inspect "$SYNC_IMAGE" >/dev/null 2>&1; then
  log "Using existing image: $SYNC_IMAGE"
else
  log "Building sync image (this may take a minute)..."
  docker build -f VaultwardenK8sSync/Dockerfile -t "$SYNC_IMAGE" .
  log_ok "Sync image built"
fi

# ── Prepare TLS certs ──────────────────────────────────────────────────────
header "Preparing TLS certificates for Vaultwarden"

mkdir -p "$E2E_SSL_DIR"
openssl req -x509 -nodes -days 1 -newkey rsa:2048 \
  -keyout "$E2E_SSL_DIR/key.pem" \
  -out "$E2E_SSL_DIR/cert.pem" \
  -subj "/CN=vaultwarden-e2e" \
  -addext "subjectAltName=DNS:vaultwarden,DNS:localhost,IP:127.0.0.1" \
  2>/dev/null
log_ok "TLS certs generated"

# ── Cleanup function ───────────────────────────────────────────────────────
cleanup() {
  local exit_code=$?
  header "Cleaning up"
  
  # Stop docker compose services
  cd "$PROJECT_ROOT"
  VAULTWARDEN_VERSION="$VW_VERSION" \
  docker compose -f docker-compose.e2e.yml down -v 2>/dev/null || true
  
  # Clean TLS certs
  rm -rf "$E2E_SSL_DIR" 2>/dev/null || true
  
  # Remove stress-specific sync-data volume
  docker volume rm vw-e2e-sync-data 2>/dev/null || true
  
  log_ok "Cleanup complete"
  exit $exit_code
}
trap cleanup EXIT

# ── Start services ─────────────────────────────────────────────────────────
header "Starting e2e services (Vaultwarden $VW_VERSION + mock-k8s + tls-proxy)"

cd "$PROJECT_ROOT"
VAULTWARDEN_VERSION="$VW_VERSION" \
COMPOSE_PROFILES="" \
docker compose -f docker-compose.e2e.yml up -d vaultwarden tls-proxy mock-k8s

# Wait for vaultwarden to be ready
log "Waiting for Vaultwarden..."
for i in $(seq 1 30); do
  if curl -sf http://localhost:8080/api/alive > /dev/null 2>&1; then
    log_ok "Vaultwarden ready"
    break
  fi
  if [ "$i" -eq 30 ]; then
    log_err "Vaultwarden failed to start"
    docker compose -f docker-compose.e2e.yml logs vaultwarden
    exit 1
  fi
  sleep 2
done

# Verify mock-k8s is running
if ! curl -sf http://localhost:16443/version > /dev/null 2>&1; then
  log_err "Mock K8s is not running"
  docker compose -f docker-compose.e2e.yml logs mock-k8s
  exit 1
fi
log_ok "Mock K8s ready"

# ── Register user & get API key ─────────────────────────────────────────────
header "Setting up test user"

# Generate credentials
TEST_EMAIL="stress-test@vaultwarden.local"
TEST_PASSWORD="StressTestPassword123"
TEST_API_KEY="stress-test-api-key-12345"

# Install sqlite3 and seed user directly (avoids /api/accounts/register 404 on newer versions)
docker exec vw-e2e-vaultwarden sh -c "apt-get update -qq 2>/dev/null && apt-get install -y -qq sqlite3 2>/dev/null" || true

# Generate akey (encrypted symmetric key that can be decrypted with master password)
# Using the Python helper to compute valid keys
AKEY_DATA=$(python3 -c "
import sys, hashlib, hmac, json, os, base64
sys.path.insert(0, '$SCRIPT_DIR')
from e2e_helper import pbkdf2_sha256, encrypt_bitwarden_v2

email = '$TEST_EMAIL'
password = '$TEST_PASSWORD'
iterations = 600000

# Derive master key (same as sync service)
master_key = pbkdf2_sha256(password.encode(), email.lower().encode(), iterations)

# Generate random symmetric key
sym_key = os.urandom(64)
enc_key = sym_key[:32]
mac_key = sym_key[32:]

# Encrypt the symmetric key with master key (format: 2.{iv}|{ct}|{mac})
akey = encrypt_bitwarden_v2(sym_key, master_key, master_key)

# Output as JSON
print(json.dumps({'akey': akey, 'enc_key_hex': enc_key.hex(), 'mac_key_hex': mac_key.hex(), 'master_key_hex': master_key.hex()}))
")

USER_UUID=$(uuidgen 2>/dev/null || python3 -c "import uuid; print(uuid.uuid4())")
SECURITY_STAMP=$(uuidgen 2>/dev/null || python3 -c "import uuid; print(uuid.uuid4())")
AKEY=$(echo "$AKEY_DATA" | python3 -c "import json,sys; print(json.load(sys.stdin)['akey'])")

# Insert user into vaultwarden database
docker exec vw-e2e-vaultwarden sh -c "sqlite3 /data/db.sqlite3 \\
  \"INSERT OR IGNORE INTO users \\
    (uuid,created_at,updated_at,email,name,password_hash,salt,password_iterations, \\
     akey,security_stamp,equivalent_domains,excluded_globals, \\
     client_kdf_type,client_kdf_iter,api_key,enabled) \\
   VALUES (\\
     '${USER_UUID}',datetime('now'),datetime('now'),'${TEST_EMAIL}','Stress Test User', \\
     'x','x',600000, \\
     '${AKEY}','${SECURITY_STAMP}','[]','[]', \\
     0,600000,'${TEST_API_KEY}',1\"" 2>&1 || log_warn "User may already exist, continuing..."

log_ok "Test user registered with UUID: $USER_UUID"

# ── Verify API key login ──────────────────────────────────────────────────
header "Verifying API key login"

TOKEN_RESPONSE=$(curl -sf -X POST http://localhost:8080/identity/connect/token \
  -d "grant_type=client_credentials&client_id=user.${USER_UUID}&client_secret=${TEST_API_KEY}&scope=api&deviceType=8&deviceIdentifier=stress-device-001&deviceName=stress-test")

ACCESS_TOKEN=$(echo "$TOKEN_RESPONSE" | python3 -c "import json,sys; print(json.load(sys.stdin)['access_token'])" 2>/dev/null)
if [ -z "$ACCESS_TOKEN" ]; then
  log_err "API key login failed"
  echo "$TOKEN_RESPONSE"
  exit 1
fi
log_ok "API key login successful"

# ── Create test items ─────────────────────────────────────────────────────
header "Creating $ITEM_COUNT test items with custom fields"

# Create items in batches to avoid overwhelming vaultwarden
BATCH_SIZE=50
CREATED=0

python3 -c "
import sys, os, json, uuid, time
sys.path.insert(0, '$SCRIPT_DIR')
from e2e_helper import *

server = 'http://localhost:8080'
email = '$TEST_EMAIL'
password = '$TEST_PASSWORD'
access_token = '$ACCESS_TOKEN'

# Parse encryption keys from akey data
akey_data = json.loads('''$(echo "$AKEY_DATA")''')
enc_key = bytes.fromhex(akey_data['enc_key_hex'])
mac_key = bytes.fromhex(akey_data['mac_key_hex'])

item_count = $ITEM_COUNT
batch_size = $BATCH_SIZE

for i in range(item_count):
    ns_num = (i % 10) + 1  # spread across 10 namespaces
    name = f'Stress-Item-{i:05d}'
    
    fields = [
        {'name': 'namespaces', 'value': f'stress-ns-{ns_num}'},
        {'name': 'description', 'value': f'Stress test item #{i} with many fields'},
        {'name': 'app_name', 'value': 'stress-test-app'},
        {'name': 'environment', 'value': 'test'},
        {'name': 'region', 'value': f'region-{ns_num}'},
        {'name': 'secret-key', 'value': f'custom-key-{i}'},
        {'name': 'ignore-field', 'value': 'internal_notes'},
        {'name': 'secret-annotation', 'value': f'stress/item={i}'},
        {'name': 'secret-label', 'value': 'stress-test=true'},
    ]
    
    ok = create_cipher(
        server, access_token, enc_key, mac_key,
        name,
        login_username=f'user{i:05d}',
        login_password=f'pass{i:05d}_with_special_chars!@#',
        fields=fields,
    )
    
    if not ok:
        print(f'ERROR creating item {i}', file=sys.stderr)
        sys.exit(1)
    
    if (i + 1) % 10 == 0 or i == item_count - 1:
        print(f'Created {i+1}/{item_count} items...', flush=True)
    
    # Small delay to avoid overwhelming vaultwarden
    if (i + 1) % batch_size == 0:
        time.sleep(1)

print(f'Successfully created {item_count} items', flush=True)
" 2>&1

log_ok "Items created"

# ── Run sync and capture memory logs ──────────────────────────────────────
header "Running sync service (measuring memory)"

RESULT_DIR="/tmp/vw-stress-test-$$"
mkdir -p "$RESULT_DIR"

# Set up env vars for the sync container
export E2E_BW_CLIENTID="user.$USER_UUID"
export E2E_BW_CLIENTSECRET="$TEST_API_KEY"
export VAULTWARDEN__MASTERPASSWORD="$TEST_PASSWORD"

cd "$PROJECT_ROOT"
log "Starting sync... (item count: $ITEM_COUNT)"

# Run sync once, capturing logs
VAULTWARDEN_VERSION="$VW_VERSION" \
E2E_BW_CLIENTID="$E2E_BW_CLIENTID" \
E2E_BW_CLIENTSECRET="$E2E_BW_CLIENTSECRET" \
docker compose -f docker-compose.e2e.yml run --rm sync 2>&1 | tee "$RESULT_DIR/sync-output.log" | grep --line-buffered -E "\[MEMORY\]|Database initialized|Database file size|Successfully authenticated|Vault unlocked|API key login|sync run #|item fetch|namespace sync|namespace group|Error|error|fail" || true

# ── Report results ────────────────────────────────────────────────────────
header "Memory Usage Report"

echo -e "${CYAN}Phase                        Memory${NC}"
echo -e "${CYAN}──────────────────────────────────────────${NC}"
grep "\[MEMORY\]" "$RESULT_DIR/sync-output.log" | while IFS= read -r line; do
  # Extract phase and MB from structured log
  PHASE=$(echo "$line" | python3 -c "
import json, sys
try:
    d = json.loads(sys.stdin.readline())
    print(d.get('@m', str(d)))
except:
    print(sys.stdin.readline().strip())
" 2>/dev/null || echo "$line")
  echo "  $PHASE"
done

echo ""
DB_SIZE=$(grep "Database file size" "$RESULT_DIR/sync-output.log" | tail -1 | python3 -c "import json,sys; d=json.loads(sys.stdin.readline()); print(d.get('@m','?'))" 2>/dev/null || echo "?")
echo -e "  ${BLUE}Items:${NC} $ITEM_COUNT"
echo -e "  ${BLUE}DB File Size:${NC} $DB_SIZE"
echo ""

# Check for errors
if grep -qi "error\|fail" "$RESULT_DIR/sync-output.log" 2>/dev/null | grep -v "0 Error" | head -5; then
  log_warn "Found potential errors in output (review above)"
fi

log "Full output saved to: $RESULT_DIR/sync-output.log"
log "Run 'cat $RESULT_DIR/sync-output.log | grep \"\\[MEMORY\\]\"' to see memory phases"
log_ok "Stress test complete!"
