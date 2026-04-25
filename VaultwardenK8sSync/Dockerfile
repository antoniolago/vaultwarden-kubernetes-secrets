# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and all project files
COPY ["VaultwardenK8sSync.sln", "./"]
COPY ["VaultwardenK8sSync/VaultwardenK8sSync.csproj", "VaultwardenK8sSync/"]
COPY ["VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj", "VaultwardenK8sSync.Database/"]
COPY ["VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj", "VaultwardenK8sSync.Api/"]
RUN dotnet restore "VaultwardenK8sSync/VaultwardenK8sSync.csproj"

# Copy all source code
COPY . .
RUN dotnet build "VaultwardenK8sSync/VaultwardenK8sSync.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "VaultwardenK8sSync/VaultwardenK8sSync.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published application
COPY --from=publish /app/publish .


# Install kubectl for applying Kubernetes YAML manifests from note items
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && \
    KUBECTL_VERSION=$(curl -L -s https://dl.k8s.io/release/stable.txt) && \
    case "$(uname -m)" in \
        x86_64)   ARCH="amd64";; \
        aarch64)  ARCH="arm64";; \
        armv7l)   ARCH="arm";; \
        *)        echo "Unsupported architecture"; exit 1;; \
    esac && \
    curl -fsSL "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/${ARCH}/kubectl" -o /usr/local/bin/kubectl && \
    curl -fsSL "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/${ARCH}/kubectl.sha256" -o /tmp/kubectl.sha256 && \
    echo "$(cat /tmp/kubectl.sha256)  /usr/local/bin/kubectl" | sha256sum --check && \
    chmod 0755 /usr/local/bin/kubectl && \
    rm -f /tmp/kubectl.sha256 && \
    apt-get purge -y curl && apt-get autoremove -y && rm -rf /var/lib/apt/lists/*

# Change ownership to non-root user
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose port (if needed for health checks)
EXPOSE 8080

# Set entry point
ENTRYPOINT ["dotnet", "VaultwardenK8sSync.dll"] 