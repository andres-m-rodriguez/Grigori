# =============================================================================
# Grigori MCP Server - Multi-stage Dockerfile
# =============================================================================
# Builds two variants:
#   - grigori:slim  - Small image, downloads model on first run
#   - grigori:full  - Larger image with model baked in (zero startup delay)
#
# Usage:
#   docker build --target slim -t grigori:slim .
#   docker build --target full -t grigori:full .
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Build
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy solution and project files first (better layer caching)
COPY Grigori.slnx ./
COPY src/Grigori.Contracts/Grigori.Contracts.csproj src/Grigori.Contracts/
COPY src/Grigori.Database/Grigori.Database.csproj src/Grigori.Database/
COPY src/Grigori.DataAccess/Grigori.DataAccess.csproj src/Grigori.DataAccess/
COPY src/Grigori.Infrastructure/Grigori.Infrastructure.csproj src/Grigori.Infrastructure/
COPY src/Grigori.Mcp/Grigori.Mcp.csproj src/Grigori.Mcp/

# Restore dependencies (without runtime identifier for Blazor static assets)
RUN dotnet restore src/Grigori.Mcp/Grigori.Mcp.csproj

# Copy source code
COPY src/ src/

# Build and publish - framework-dependent to include Blazor static files
RUN dotnet publish src/Grigori.Mcp/Grigori.Mcp.csproj \
    -c Release \
    -o /app/publish \
    -p:SelfContained=false \
    -p:PublishSingleFile=false

# -----------------------------------------------------------------------------
# Stage 2: Download Model (for full image)
# -----------------------------------------------------------------------------
FROM alpine:3.19 AS model-downloader

RUN apk add --no-cache curl

WORKDIR /models

# Download all-MiniLM-L6-v2 ONNX model and vocab
RUN curl -L -o model.onnx \
    "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx" && \
    curl -L -o vocab.txt \
    "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt" && \
    echo "Model size: $(du -h model.onnx | cut -f1)" && \
    echo "Vocab size: $(du -h vocab.txt | cut -f1)"

# -----------------------------------------------------------------------------
# Stage 3: Runtime Base
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base

# Install native dependencies for ONNX Runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user for security with home directory
RUN groupadd -r grigori && useradd -r -g grigori -m -d /home/grigori grigori

# Set up directories
WORKDIR /app
RUN mkdir -p /data/models /data/index && \
    chown -R grigori:grigori /app /data /home/grigori

# Environment configuration
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/home/grigori/.net \
    GRIGORI__ONNX__MODELPATH=/data/models/model.onnx \
    GRIGORI__ONNX__VOCABPATH=/data/models/vocab.txt \
    GRIGORI__INDEXPATH=/data/index/grigori.db \
    ASPNETCORE_URLS=http://+:8080

# Expose default port (can be overridden with -p)
EXPOSE 8080

# -----------------------------------------------------------------------------
# Stage 4: Slim Image (no model, downloads on first run)
# -----------------------------------------------------------------------------
FROM runtime-base AS slim

LABEL org.opencontainers.image.title="Grigori MCP Server (Slim)" \
      org.opencontainers.image.description="Semantic code search MCP server - downloads model on first run" \
      org.opencontainers.image.version="1.0.0" \
      org.opencontainers.image.vendor="Grigori"

# Copy published app (all files since not using single-file publish)
COPY --from=build --chown=grigori:grigori /app/publish /app

# Volume for persistent data (models + index)
VOLUME ["/data"]

USER grigori

ENTRYPOINT ["dotnet", "/app/Grigori.Mcp.dll"]

# -----------------------------------------------------------------------------
# Stage 5: Full Image (model baked in)
# -----------------------------------------------------------------------------
FROM runtime-base AS full

LABEL org.opencontainers.image.title="Grigori MCP Server (Full)" \
      org.opencontainers.image.description="Semantic code search MCP server - model included" \
      org.opencontainers.image.version="1.0.0" \
      org.opencontainers.image.vendor="Grigori"

# Copy published app (all files since not using single-file publish)
COPY --from=build --chown=grigori:grigori /app/publish /app

# Copy pre-downloaded model
COPY --from=model-downloader --chown=grigori:grigori /models/model.onnx /data/models/model.onnx
COPY --from=model-downloader --chown=grigori:grigori /models/vocab.txt /data/models/vocab.txt

# Volume for persistent index data only
VOLUME ["/data/index"]

USER grigori

ENTRYPOINT ["dotnet", "/app/Grigori.Mcp.dll"]
