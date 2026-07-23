# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# --- Build the agy-acp ACP adapter (Rust) ---
FROM rust:1-bookworm AS adapter-builder
WORKDIR /build
COPY agy-acp/Cargo.toml agy-acp/Cargo.lock ./
RUN mkdir src && echo 'fn main() {}' > src/main.rs && cargo build --release && rm -rf src
COPY agy-acp/src/ src/
RUN touch src/main.rs && cargo build --release

# --- Build & publish the .NET bot ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["Pacos/Pacos.csproj", "Pacos/"]
RUN dotnet restore "./Pacos/Pacos.csproj"
COPY . .
WORKDIR "/src/Pacos"
RUN dotnet build "./Pacos.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Pacos.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# --- Runtime image: .NET runtime + agy CLI + agy-acp adapter ---
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl jq tini procps ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Install agy (Google Antigravity CLI) — pinned to a known-good version
ENV AGY_VERSION=1.1.4
RUN ARCH=$(dpkg --print-architecture) && \
    case "$ARCH" in \
      amd64) ASSET="agy_cli_linux_x64.tar.gz" ;; \
      arm64) ASSET="agy_cli_linux_arm64.tar.gz" ;; \
      *) echo "unsupported arch: $ARCH" && exit 1 ;; \
    esac && \
    curl -fsSL "https://github.com/google-antigravity/antigravity-cli/releases/download/${AGY_VERSION}/${ASSET}" \
      | tar -xz -C /usr/local/bin && \
    mv /usr/local/bin/antigravity /usr/local/bin/agy && \
    chmod +x /usr/local/bin/agy

# The bot spawns `agy-acp`, which in turn spawns `agy`; both must be on PATH.
COPY --from=adapter-builder /build/target/release/agy-acp /usr/local/bin/agy-acp

# Install the GalleryDl MCP server (https://github.com/magicxor/GalleryDlManager),
# pinned to a released version. The framework-dependent build is arch-neutral IL;
# agy spawns it as `dotnet /opt/gallerydl-mcp/GalleryDl.McpServer.dll` according
# to the mcp_config.json written at startup by AgyMcpConfigHostedService.
# The shipped appsettings.json allows writing under /downloads and /tmp; empty the
# list so the baked-in baseline allows nothing and the single env override written
# by AgyMcpConfigHostedService fully defines the allow-list. Fail-closed: the
# server validates AllowedPathPrefixes with MinLength(1) on startup, so if the
# override ever goes missing the server refuses to start instead of widening.
ENV GALLERYDL_MCP_VERSION=0.2.3
RUN mkdir -p /opt/gallerydl-mcp && \
    curl -fsSL "https://github.com/magicxor/GalleryDlManager/releases/download/${GALLERYDL_MCP_VERSION}/GalleryDl.McpServer-${GALLERYDL_MCP_VERSION}.tar.gz" \
      | tar -xz -C /opt/gallerydl-mcp && \
    jq '.GalleryDlApi.AllowedPathPrefixes = []' /opt/gallerydl-mcp/appsettings.json > /opt/gallerydl-mcp/appsettings.json.tmp && \
    mv /opt/gallerydl-mcp/appsettings.json.tmp /opt/gallerydl-mcp/appsettings.json

# Install the FileMcp MCP server (https://github.com/magicxor/FileMcp), pinned to a
# released version. The framework-dependent build is arch-neutral IL; agy spawns it
# as `dotnet /opt/file-mcp/FileMcp.dll` according to the mcp_config.json written at
# startup by AgyMcpConfigHostedService. Unlike GalleryDl there is NO backend service
# and NO container image to pull: FileMcp is entirely local (stdio only). Empty both
# allow-list arrays in the shipped appsettings.json so the baked-in baseline allows
# nothing (FileMcp treats an empty pattern list as deny-all) and the env overrides
# written by AgyMcpConfigHostedService fully define the source/target allow-list.
# Fail-closed: if the overrides ever go missing the server still starts but every
# move is blocked.
ENV FILE_MCP_VERSION=0.0.1
RUN mkdir -p /opt/file-mcp && \
    curl -fsSL "https://github.com/magicxor/FileMcp/releases/download/${FILE_MCP_VERSION}/FileMcp-${FILE_MCP_VERSION}.tar.gz" \
      | tar -xz -C /opt/file-mcp && \
    jq '.FileMove.AllowedSourcePatterns = [] | .FileMove.AllowedTargetPatterns = []' /opt/file-mcp/appsettings.json > /opt/file-mcp/appsettings.json.tmp && \
    mv /opt/file-mcp/appsettings.json.tmp /opt/file-mcp/appsettings.json

# Pin the agy CLI to the installed version; never let it self-update at runtime.
ENV AGY_CLI_DISABLE_AUTO_UPDATE=true

# Run as a dedicated non-root user so agy can persist auth/state under HOME.
RUN useradd -m -s /bin/bash agent
ENV HOME=/home/agent

# Pre-create the agy state directory owned by the runtime user. A named volume
# mounted here (see deploy workflow) inherits this ownership on first creation,
# so the non-root `agent` user can write to it. Without this the volume mount
# point is created as root:root and agy fails with UnauthorizedAccessException.
RUN mkdir -p /home/agent/.gemini && chown -R agent:agent /home/agent/.gemini

WORKDIR /app
COPY --from=publish --chown=agent:agent /app/publish .

# The agy security policy (deny ALL shell commands, restrict file access to the
# per-chat workspace and brain dir) is enforced in code at startup — see
# AgySecurityPolicyHostedService — so it can never be forgotten or shadowed.
# File delivery goes through the filemcp MCP server, not the shell. Headless agy
# >= 1.1.3 is fail-closed: everything outside the allow list is auto-denied.

USER agent
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD pgrep -f Pacos.dll || exit 1
ENTRYPOINT ["tini", "--", "dotnet", "Pacos.dll"]
