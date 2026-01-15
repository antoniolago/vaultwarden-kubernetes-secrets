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

# Install Bitwarden CLI via npm for all architectures so NODE_OPTIONS is honored by Node runtime
# Update Node.js to version 20+ to fix ES module compatibility issues
RUN apt-get update && apt-get install -y \
    curl \
    unzip \
    ca-certificates \
    gnupg \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/* \
    && npm install -g @bitwarden/cli@2025.12.0

# Change ownership to non-root user
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose port (if needed for health checks)
EXPOSE 8080

# Set entry point
ENTRYPOINT ["dotnet", "VaultwardenK8sSync.dll"] 