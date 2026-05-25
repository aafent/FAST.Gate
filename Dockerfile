# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first (layer cache for restore)
COPY FAST.Gate.sln ./
COPY FAST.Gate/FAST.Gate.csproj             FAST.Gate/
COPY FAST.Gate.Client/FAST.Gate.Client.csproj FAST.Gate.Client/

RUN dotnet restore FAST.Gate/FAST.Gate.csproj

# Copy all source and build
COPY FAST.Gate/       FAST.Gate/
COPY FAST.Gate.Client/ FAST.Gate.Client/

RUN dotnet publish FAST.Gate/FAST.Gate.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Stage 2: Runtime ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Non-root user for security
RUN addgroup --system fastgate && adduser --system --ingroup fastgate fastgate
USER fastgate

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FAST.Gate.dll"]
