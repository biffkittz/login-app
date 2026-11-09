# syntax=docker/dockerfile:1
#
# Builds the whole Login App site into a single Linux container image:
#   1. frontend-build  compiles the Vue SPA (npm ci, type-check, Vite production build)
#   2. backend-build   publishes the ASP.NET Core API with the SPA copied into wwwroot
#   3. final           ASP.NET Core runtime only: Kestrel serves the API and the SPA (HTTP on port 80 by default;
#                      HTTPS on port 443 when OriginTls is configured, as in Azure)
#
# The build context is the app/ directory. From the repository root:
#   docker build --tag login-app:local app
#
# See docs/BUILD_AND_DEPLOY.md for running the image and deploying it to Azure Container Apps.

ARG NODE_VERSION=24
ARG DOTNET_VERSION=10.0

###############################################################################
# 1. Frontend
###############################################################################
FROM node:${NODE_VERSION}-slim AS frontend-build
WORKDIR /src/frontend

# Install dependencies in their own layer, so source-only changes reuse the cached node_modules.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY frontend/ ./
RUN npm run build

###############################################################################
# 2. Backend
###############################################################################
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS backend-build
WORKDIR /src

# Restore in its own layer, keyed only on the files that decide which packages are needed.
COPY Directory.Build.props Directory.Packages.props ./
COPY backend/LoginApp.Api.csproj backend/
RUN dotnet restore backend/LoginApp.Api.csproj

COPY backend/ backend/

# The SPA must be in wwwroot *before* publishing: MapStaticAssets fingerprints and pre-compresses
# (gzip/Brotli) everything in wwwroot at publish time.
COPY --from=frontend-build /src/frontend/dist/ backend/wwwroot/

# Release builds treat warnings as errors, so an analyzer violation fails here. The code-style rules are in the
# repository's root .editorconfig, outside this build context: buildAndRunSite.sh is what enforces those.
RUN dotnet publish backend/LoginApp.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false

###############################################################################
# 3. Runtime
###############################################################################
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

LABEL org.opencontainers.image.title="login-app" \
      org.opencontainers.image.description="Login App: Vue 3 SPA served by an ASP.NET Core API (Kestrel, port 80 or 443)"

# By default Kestrel listens for plain HTTP on port 80 (local runs, compose.yaml). In Azure, Terraform sets
# ASPNETCORE_URLS=https://+:443 and the OriginTls settings, so Kestrel terminates TLS itself with the Cloudflare
# origin certificate. Either way the app trusts X-Forwarded-For/-Proto only from the networks in appsettings.json.
ENV ASPNETCORE_HTTP_PORTS=80 \
    ASPNETCORE_ENVIRONMENT=Production

# The app runs as the image's unprivileged "app" user (USER below), and unprivileged processes may not normally bind
# ports below 1024. Instead of running as root, grant the dotnet host exactly one capability:
# CAP_NET_BIND_SERVICE. The setcap tool is removed again in the same layer.
# Trade-off: Linux does not allow ptrace into a process started from a binary with file capabilities, so debuggers
# cannot attach and createdump-based crash dumps are unavailable (docs/BUILD_AND_DEPLOY.md, "Port 80").
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libcap2-bin \
    && setcap 'cap_net_bind_service=+ep' "$(readlink --canonicalize /usr/bin/dotnet)" \
    && apt-get purge --yes --auto-remove libcap2-bin \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# The published files stay owned by root, so the app user can read but never modify its own code.
COPY --from=backend-build /app/publish ./

USER $APP_UID
EXPOSE 80 443

# No HEALTHCHECK instruction: Azure Container Apps ignores it and uses its own probes against /health/live and
# /health/ready (docs/BUILD_AND_DEPLOY.md), and the runtime image deliberately ships without curl.
ENTRYPOINT ["dotnet", "LoginApp.Api.dll"]
