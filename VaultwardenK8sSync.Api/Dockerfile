FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj", "VaultwardenK8sSync.Api/"]
COPY ["VaultwardenK8sSync.Database/VaultwardenK8sSync.Database.csproj", "VaultwardenK8sSync.Database/"]
RUN dotnet restore "VaultwardenK8sSync.Api/VaultwardenK8sSync.Api.csproj"
COPY . .
WORKDIR "/src/VaultwardenK8sSync.Api"
RUN dotnet build "VaultwardenK8sSync.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VaultwardenK8sSync.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create data directory for SQLite
RUN mkdir -p /data && chown -R app:app /data

USER app
ENTRYPOINT ["dotnet", "VaultwardenK8sSync.Api.dll"]
