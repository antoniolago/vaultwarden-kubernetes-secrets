# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["VaultwardenK8sSync.csproj", "./"]
RUN dotnet restore "VaultwardenK8sSync.csproj"

# Copy source code
COPY . .
RUN dotnet build "VaultwardenK8sSync.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "VaultwardenK8sSync.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published application
COPY --from=publish /app/publish .

# Install Bitwarden CLI
RUN apt-get update && apt-get install -y \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Download and install Bitwarden CLI
RUN curl -L "https://vault.bitwarden.com/download/?app=cli&platform=linux" -o bw.zip \
    && unzip bw.zip -d /usr/local/bin \
    && chmod +x /usr/local/bin/bw \
    && rm bw.zip

# Change ownership to non-root user
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose port (if needed for health checks)
EXPOSE 8080

# Set entry point
ENTRYPOINT ["dotnet", "VaultwardenK8sSync.dll"] 