# ── Build .NET API ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore dependencies (cached layer)
COPY src/CodeFlow.Core/CodeFlow.Core.csproj           src/CodeFlow.Core/
COPY src/CodeFlow.Crypto/CodeFlow.Crypto.csproj       src/CodeFlow.Crypto/
COPY src/CodeFlow.Storage/CodeFlow.Storage.csproj     src/CodeFlow.Storage/
COPY src/CodeFlow.API/CodeFlow.API.csproj             src/CodeFlow.API/
RUN dotnet restore src/CodeFlow.API/CodeFlow.API.csproj

COPY src/ src/

RUN dotnet publish src/CodeFlow.API/CodeFlow.API.csproj \
    -c Release -o /app/publish \
    --no-restore

# ── Runtime ────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /app/data/repos

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=10000
ENV Repos__Root=/app/data/repos

EXPOSE 10000

ENTRYPOINT ["dotnet", "CodeFlow.API.dll"]