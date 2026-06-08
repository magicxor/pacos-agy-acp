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
    && apt-get install -y --no-install-recommends ca-certificates curl tini procps ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Install agy (Google Antigravity CLI) — pinned to a known-good version
ENV AGY_VERSION=1.0.4
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

# The agy security policy (deny all commands, restrict file access to the
# per-chat workspace) is enforced in code at startup — see
# AgySecurityPolicyHostedService — so it can never be forgotten or shadowed.

USER agent
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD pgrep -f Pacos.dll || exit 1
ENTRYPOINT ["tini", "--", "dotnet", "Pacos.dll"]
