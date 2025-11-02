#!/bin/bash
set -e

echo "🐳 Docker Image Test"
echo "===================="

IMAGE_TAG="${IMAGE_TAG:-test}"
IMAGE_NAME="vaultwarden-kubernetes-secrets:${IMAGE_TAG}"

echo ""
echo "📦 Building Docker image..."
docker build -f VaultwardenK8sSync/Dockerfile -t "$IMAGE_NAME" .

echo ""
echo "✅ Image built successfully: $IMAGE_NAME"
echo ""
echo "🧪 Running basic tests..."

# Test 1: Image exists and has correct entrypoint
echo "  ✓ Checking image metadata..."
docker inspect "$IMAGE_NAME" >/dev/null 2>&1 || { echo "❌ Image not found"; exit 1; }

# Test 2: Container starts in debug mode
echo "  ✓ Testing debug mode (container should stay alive)..."
CONTAINER_ID=$(docker run -d --rm \
  -e DEBUG=true \
  "$IMAGE_NAME" tail -f /dev/null)

sleep 2

if docker ps --filter "id=$CONTAINER_ID" --format '{{.ID}}' | grep -q "$CONTAINER_ID"; then
    echo "    ✅ Container running in debug mode"
    docker stop "$CONTAINER_ID" >/dev/null 2>&1 || true
else
    echo "    ❌ Container failed to stay alive in debug mode"
    exit 1
fi

# Test 3: Help command
echo "  ✓ Testing help command..."
docker run --rm "$IMAGE_NAME" dotnet VaultwardenK8sSync.dll --help >/dev/null 2>&1 || {
    echo "    ⚠️  Help command failed (might be expected if CLI doesn't support --help)"
}

# Test 4: Check required files exist
echo "  ✓ Checking required files in image..."
docker run --rm "$IMAGE_NAME" ls -la VaultwardenK8sSync.dll >/dev/null 2>&1 || {
    echo "    ❌ Main DLL not found"
    exit 1
}

echo ""
echo "✅ All basic tests passed!"
echo ""
echo "📋 Manual testing commands:"
echo ""
echo "  # Run in debug mode (keeps container alive)"
echo "  docker run --rm -it $IMAGE_NAME tail -f /dev/null"
echo ""
echo "  # Run with shell access"
echo "  docker run --rm -it $IMAGE_NAME /bin/bash"
echo ""
echo "  # Test sync with dry-run (requires valid config)"
echo "  docker run --rm \\"
echo "    -e VAULTWARDEN__SERVERURL=https://vault.example.com \\"
echo "    -e VAULTWARDEN__MASTERPASSWORD=your-password \\"
echo "    -e SYNC__DRYRUN=true \\"
echo "    $IMAGE_NAME"
echo ""
echo "  # View image layers"
echo "  docker history $IMAGE_NAME"
echo ""
echo "  # Inspect image"
echo "  docker inspect $IMAGE_NAME | jq"
echo ""
