# Development Guide

## Quick Start

Start all services (API, Sync, Dashboard, Valkey) with one command:

```bash
./scripts/start-all.sh
```

This will:
1. ✅ Check and start **Valkey** if not running (for WebSocket sync output)
2. ✅ Run initial sync if database doesn't exist
3. ✅ Build and start **API** on port 8080
4. ✅ Build and start **Sync Service** (continuous mode)
5. ✅ Build and start **Dashboard** on port 3000
6. ✅ Open browser to http://localhost:3000

## Services Started

| Service | Port | URL | Logs |
|---------|------|-----|------|
| Dashboard | 3000 | http://localhost:3000 | `tail -f /tmp/vk8s-dashboard.log` |
| API | 8080 | http://localhost:8080 | `tail -f /tmp/vk8s-api.log` |
| API Swagger | 8080 | http://localhost:8080/swagger | - |
| Sync Service | - | (background) | `tail -f /tmp/vk8s-sync.log` |
| Valkey | 6379 | localhost:6379 | - |

## Stop All Services

Press **Ctrl+C** in the terminal running `start-all.sh`

The cleanup function will:
- Stop Dashboard
- Stop API
- Stop Sync Service
- Stop Valkey (if started by the script)
- Kill any remaining processes on ports 3000, 8080, 9090

## Valkey

**Required for:** Real-time sync output in the Console Output tab of the Sync Output Modal.

Valkey is a Redis-compatible fork (https://valkey.io) and can be used as a drop-in replacement for Redis.

The `start-all.sh` script automatically:
- Checks if Valkey is running
- Tries to start Valkey using Podman/Docker (preferred)
- Falls back to native valkey-server or redis-server if container runtime not available
- Shows a warning if Valkey cannot be started

### Quick Valkey Setup with Podman/Docker

**Using the helper script (recommended):**
```bash
./scripts/valkey-podman.sh start
```

**Manual setup:**
```bash
# Using Podman (use fully qualified image name)
podman run -d --name vaultwarden-valkey --rm -p 6379:6379 docker.io/valkey/valkey:alpine

# Or using Docker
docker run -d --name vaultwarden-valkey --rm -p 6379:6379 valkey/valkey:alpine
```

**Note:** Podman requires fully qualified image names to avoid short-name resolution prompts in non-interactive contexts.

### Alternative: Native Installation

**Ubuntu/Debian:**
```bash
# Valkey (recommended)
sudo apt-get install valkey-server

# Or Redis (also compatible)
sudo apt-get install redis-server
```

**macOS:**
```bash
# Valkey
brew install valkey
brew services start valkey

# Or Redis
brew install redis
brew services start redis
```

### Helper Script Commands

```bash
./scripts/valkey-podman.sh start    # Start Valkey
./scripts/valkey-podman.sh stop     # Stop Valkey
./scripts/valkey-podman.sh status   # Check if running
./scripts/valkey-podman.sh logs     # View logs
./scripts/valkey-podman.sh restart  # Restart Valkey
```

## Manual Service Control

### Start Individual Services

**Sync Service:**
```bash
cd VaultwardenK8sSync
dotnet run
```

**API:**
```bash
cd VaultwardenK8sSync.Api
dotnet run
```

**Dashboard:**
```bash
cd dashboard
npm run dev
```

**Valkey:**
```bash
# Valkey
valkey-server --daemonize yes --port 6379

# Or Redis (if Valkey not installed)
redis-server --daemonize yes --port 6379
```

### Check Service Status

```bash
# API Health
curl http://localhost:8080/health

# Valkey (or Redis)
redis-cli ping

# Check running processes
lsof -ti:8080  # API
lsof -ti:3000  # Dashboard
lsof -ti:6379  # Valkey
```

## Configuration

All services read from `.env` file in the project root.

**Key settings for development:**
```bash
# Sync runs continuously every 30 seconds
SYNC__CONTINUOUSSYNC=true
SYNC__SYNCINTERVALSECONDS=30

# Valkey (Redis-compatible) for WebSocket output
VALKEY_CONNECTION=localhost:6379

# Disable dry run to actually sync
SYNC__DRYRUN=false
```

## Troubleshooting

### WebSocket connection failed

Check if Valkey is running:
```bash
redis-cli ping
```

Should return `PONG`. If not, start Valkey manually:
```bash
valkey-server --daemonize yes --port 6379

# Or Redis (if Valkey not installed)
redis-server --daemonize yes --port 6379
```

### Sync service not starting

Check logs:
```bash
tail -f /tmp/vk8s-sync.log
```

Common issues:
- `.env` file missing or misconfigured
- Vaultwarden credentials invalid
- Kubernetes config not accessible

### Port already in use

Kill existing processes:
```bash
lsof -ti:8080 | xargs kill -9  # API
lsof -ti:3000 | xargs kill -9  # Dashboard
```

Or run the cleanup manually:
```bash
./scripts/start-all.sh  # Then Ctrl+C immediately to run cleanup
```
