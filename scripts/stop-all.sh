#!/bin/bash

echo "🛑 Stopping all Vaultwarden K8s Sync services..."

# Kill processes on ports
echo "Stopping API (port 8080)..."
lsof -ti:8080 | xargs kill -9 2>/dev/null || echo "No process on port 8080"

echo "Stopping Dashboard (port 3000)..."
lsof -ti:3000 | xargs kill -9 2>/dev/null || echo "No process on port 3000"

# Also kill by process name (backup)
pkill -f "dotnet.*VaultwardenK8sSync.Api" 2>/dev/null || true
pkill -f "vite.*dashboard" 2>/dev/null || true
pkill -f "bun.*dev" 2>/dev/null || true

echo "✅ All services stopped"
