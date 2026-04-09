# ── Stage 1: Build React frontend ─────────────────────────────────────────────
FROM node:20-alpine AS web-build
WORKDIR /web

COPY CodeFlow_web/package*.json ./
RUN npm ci

COPY CodeFlow_web/ ./

# VITE_API_URL is intentionally left empty here — the browser hits /api/*
# which Nginx/Kestrel proxies internally. No cross-origin needed.
ARG VITE_API_URL=""
ENV VITE_API_URL=$VITE_API_URL

# Output directly into the API's wwwroot so dotnet publish picks it up
RUN npm run build -- --outDir /wwwroot

# ── Stage 2: Build .NET API ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api-build
WORKDIR /src

# Restore dependencies (cached layer)
COPY src/CodeFlow.Core/CodeFlow.Core.csproj           src/CodeFlow.Core/
COPY src/CodeFlow.Crypto/CodeFlow.Crypto.csproj       src/CodeFlow.Crypto/
COPY src/CodeFlow.Storage/CodeFlow.Storage.csproj     src/CodeFlow.Storage/
COPY src/CodeFlow.API/CodeFlow.API.csproj             src/CodeFlow.API/
RUN dotnet restore src/CodeFlow.API/CodeFlow.API.csproj

# Copy source and built frontend
COPY src/ src/
COPY --from=web-build /wwwroot src/CodeFlow.API/wwwroot/

RUN dotnet publish src/CodeFlow.API/CodeFlow.API.csproj \
    -c Release -o /app/publish \
    --no-restore

# ── Stage 3: Runtime ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=api-build /app/publish .

RUN mkdir -p /app/data/repos

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Repos__Root=/app/data/repos

EXPOSE 8080

ENTRYPOINT ["dotnet", "CodeFlow.API.dll"]