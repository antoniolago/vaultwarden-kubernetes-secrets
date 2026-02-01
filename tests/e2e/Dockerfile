# E2E Test Environment for Vaultwarden Kubernetes Secrets
# This container includes everything needed to run the full e2e test suite:
# - Docker (Docker-in-Docker)
# - Kind (Kubernetes in Docker)
# - kubectl, helm
# - .NET SDK for C# tests
# - Bitwarden CLI

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview-alpine AS base

# Install Docker CLI and dependencies
RUN apk add --no-cache \
    bash \
    curl \
    git \
    jq \
    openssl \
    docker-cli \
    && rm -rf /var/cache/apk/*

# Install kubectl
RUN curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl" \
    && chmod +x kubectl \
    && mv kubectl /usr/local/bin/

# Install kind
RUN curl -Lo ./kind https://kind.sigs.k8s.io/dl/v0.20.0/kind-linux-amd64 \
    && chmod +x ./kind \
    && mv ./kind /usr/local/bin/kind

# Install helm
RUN curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash

# Install Bitwarden CLI
RUN curl -L "https://github.com/bitwarden/clients/releases/download/cli-v2024.2.0/bw-linux-2024.2.0.zip" -o bw.zip \
    && unzip bw.zip \
    && chmod +x bw \
    && mv bw /usr/local/bin/ \
    && rm bw.zip

# Set working directory
WORKDIR /workspace

# Copy project files
COPY . /workspace/

# Restore and build the E2E test project
RUN dotnet restore VaultwardenK8sSync.E2ETests/VaultwardenK8sSync.E2ETests.csproj \
    && dotnet build VaultwardenK8sSync.E2ETests/VaultwardenK8sSync.E2ETests.csproj -c Release --no-restore

# Make scripts executable
RUN chmod +x /workspace/tests/e2e/*.sh

# Environment variables
ENV E2E_CLUSTER_NAME="vks-e2e"
ENV E2E_KEEP_CLUSTER="false"

# Run tests
CMD ["dotnet", "test", "VaultwardenK8sSync.E2ETests/VaultwardenK8sSync.E2ETests.csproj", "-c", "Release", "--no-build", "--logger", "console;verbosity=detailed"]
