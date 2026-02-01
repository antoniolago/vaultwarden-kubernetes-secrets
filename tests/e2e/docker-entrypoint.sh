#!/bin/bash
set -e

# Start Docker daemon in background
echo "Starting Docker daemon..."
dockerd-entrypoint.sh &

# Wait for Docker to be ready
echo "Waiting for Docker daemon to be ready..."
max_wait=60
waited=0
while ! docker info >/dev/null 2>&1; do
    if [ $waited -ge $max_wait ]; then
        echo "ERROR: Docker daemon failed to start within ${max_wait}s"
        exit 1
    fi
    sleep 1
    waited=$((waited + 1))
done

echo "Docker daemon is ready (waited ${waited}s)"

# Execute the command passed to the container
exec "$@"
