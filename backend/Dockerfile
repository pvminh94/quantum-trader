# ═══════════════════════════════════════════════════════════════════
# Dockerfile - Backend .NET 9 Trading Engine API
#
# Multi-stage build:
#   1. build    - restore + compile in SDK container
#   2. publish  - publish self-contained, trimmed, AOT-ready
#   3. runtime  - minimal ASP.NET runtime image (~200MB)
# ═══════════════════════════════════════════════════════════════════

# ── Stage 1: Build ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first (layer caching)
COPY src/TradingEngine.Api/TradingEngine.Api.csproj src/TradingEngine.Api/
COPY src/TradingEngine.Application/TradingEngine.Application.csproj src/TradingEngine.Application/
COPY src/TradingEngine.Domain/TradingEngine.Domain.csproj src/TradingEngine.Domain/
COPY src/TradingEngine.Infrastructure/TradingEngine.Infrastructure.csproj src/TradingEngine.Infrastructure/
COPY src/TradingEngine.Shared/TradingEngine.Shared.csproj src/TradingEngine.Shared/

# Restore (cached unless packages change)
WORKDIR /src/src/TradingEngine.Api
RUN dotnet restore

# Copy all source code
WORKDIR /src
COPY src/ ./src/

# Build & publish to /app directory
WORKDIR /src/src/TradingEngine.Api
RUN dotnet publish -c Release -o /app \
    --self-contained false \
    --no-restore

# ── Stage 2: Runtime ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Optional: install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create non-root user (security)
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

# Copy published app from build stage
COPY --from=build /app .

# Expose API port
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=15s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "TradingEngine.Api.dll"]