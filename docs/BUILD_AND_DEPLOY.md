# Build and deploy

How the two projects become one deployable site, how that site becomes a container image, and how the image runs
in Azure Container Apps with secrets from Azure Key Vault and data in Azure Database for PostgreSQL.

- [1. buildAndRunSite.sh](#1-buildandrunsitesh)
- [2. The container image](#2-the-container-image)
- [3. Deploying to Azure Container Apps](#3-deploying-to-azure-container-apps)
- [4. Database migrations in Azure](#4-database-migrations-in-azure)
- [5. Day-2 operations](#5-day-2-operations)
- [6. Troubleshooting](#6-troubleshooting)

## How the site is put together

The Vue SPA is compiled to static files and **served by the ASP.NET Core API itself**, together with the grocery
list's realtime hub (SignalR over WebSockets), so the browser only ever talks to one origin. That single origin is what
allows the session cookie to be `HttpOnly` and `SameSite=Strict`, and to authenticate the WebSocket too, without any
CORS configuration (see [ARCHITECTURE.md](ARCHITECTURE.md)).

```text
app/frontend ──npm run build──► frontend/dist ──copy──► app/backend/wwwroot ──dotnet publish──► site/
                                                                                (API + SPA, run by Kestrel)
```

Order matters: the SPA must be in `wwwroot` *before* `dotnet publish`. At publish time `MapStaticAssets` records
every file, gives it an ETag and creates gzip and Brotli variants. `backend/wwwroot` is generated: never edit or
commit it (it is git-ignored).

## 1. buildAndRunSite.sh

`app/buildAndRunSite.sh` performs the whole pipeline locally and starts the result with Kestrel:

| Step | Command(s) | Output |
| --- | --- | --- |
| Prerequisites | checks for the .NET 10 SDK and Node 22+ | |
| Frontend | `npm ci`, `npm run test`, `npm run build` (vue-tsc + Vite) | `app/frontend/dist` |
| Combine | copy `dist/` into `backend/wwwroot/` | `app/backend/wwwroot` |
| Backend | `dotnet test`, then `dotnet publish -c Release` | `app/artifacts/site` |
| Run | `dotnet LoginApp.Api.dll` | <http://localhost:8080> |

```bash
./app/buildAndRunSite.sh                         # everything, then run on port 8080 (Development)
./app/buildAndRunSite.sh --port 9000             # another port
./app/buildAndRunSite.sh --skip-tests --skip-npm-ci   # faster rebuild after a small change
./app/buildAndRunSite.sh --no-run                # build only (e.g. in CI); the site is in app/artifacts/site
./app/buildAndRunSite.sh --environment Production     # production settings: needs Jwt__SigningKey and
                                                      # ConnectionStrings__DefaultConnection in the environment
```

Release builds treat compiler, analyzer and code-style warnings as **errors**, so the script doubles as the quality
gate. Run it before opening a pull request. It can run from any directory and stops at the first failing step.

By default the script runs in the **Development** environment, so it uses the local database from
`appsettings.Development.json` (see [DEVELOPMENT.md](DEVELOPMENT.md#4-local-database)). It binds to `localhost`
only; WSL forwards that port to Windows, so browse to <http://localhost:8080> from Windows.

## 2. The container image

`app/Dockerfile` repeats the same pipeline in three stages:

| Stage | Base image | Does |
| --- | --- | --- |
| `frontend-build` | `node:24-slim` | `npm ci` (cached by lockfile), `npm run build` |
| `backend-build` | `mcr.microsoft.com/dotnet/sdk:10.0` | `dotnet restore` (cached by project files), copies the SPA into `wwwroot`, `dotnet publish -c Release` |
| `final` | `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu 24.04) | Only the published site; runs `dotnet LoginApp.Api.dll` |

The runtime image contains no SDK, no Node.js, no source code, no tests and no development settings. `.dockerignore`
excludes `appsettings.Development.json`, so local credentials can never ship.

### Kestrel on port 80 or 443, as a non-root user

By default the container listens for plain HTTP on **port 80** (`ASPNETCORE_HTTP_PORTS=80`), which is how
`compose.yaml` and local runs use it. In Azure, Terraform sets `ASPNETCORE_URLS=https://+:443` and the `OriginTls`
settings, so Kestrel listens for **HTTPS on port 443** only and terminates TLS itself (section 3).

Since .NET 8, the official images provide an unprivileged `app` user, and the image runs as that user. Linux does
not normally let unprivileged processes bind ports below 1024. Some container runtimes lift that restriction
(Docker does by default) and some don't: users have reported `Permission denied` for exactly this setup on Azure
Container Instances. Rather than run as root, the Dockerfile grants the `dotnet` host binary **one** capability,
`CAP_NET_BIND_SERVICE`, using `setcap`.

Trade-off: Linux won't let other processes ptrace a process started from a binary with file capabilities. That means
you can't attach a debugger to the container and can't collect `createdump` crash dumps; `dotnet-counters` and
`dotnet-trace` still work. If you ever need those diagnostics, or a platform refuses file capabilities, use one of
these alternatives:

- **Preferred:** listen on a high port instead. In Azure, set `app_target_port = 8443` in Terraform; it updates
  `ASPNETCORE_URLS`, the ingress target port and the probes. Clients are unaffected, because the ingress's
  exposed port (8443) doesn't change.
- Run as root by removing `USER $APP_UID`. This is simplest, but weakens container isolation.

### Build and run the image locally

Needs Docker Desktop with WSL integration (see [DEVELOPMENT.md](DEVELOPMENT.md#2-windows-setup)). From the
repository root:

```bash
docker build --tag login-app:local app             # the build context is app/

export LOGINAPP_JWT_SIGNING_KEY="$(openssl rand -base64 48)"
docker compose --file app/compose.yaml up --build  # the image + a throwaway PostgreSQL 17
# browse to http://localhost:8080   (Ctrl+C to stop; `docker compose --file app/compose.yaml down --volumes` to reset)
```

The compose file runs the image exactly as in production: Production environment, secure `__Host-` cookie, JSON
logs and the internal-network check. Chrome, Edge and Firefox accept `Secure` cookies on `http://localhost`;
Safari does not.

## 3. Deploying to Azure Container Apps

The Terraform project in [`infra/`](../infra/README.md) creates the Azure infrastructure and deploys the image. It
covers the network, the Container Apps environment and app, PostgreSQL, Key Vault, the registry, and the Cloudflare
DNS record and Authenticated Origin Pulls. **[infra/docs/DEPLOYMENT.md](../infra/docs/DEPLOYMENT.md) is the step-by-step
guide**; this section describes what the app expects from that infrastructure.

### Request path

```text
visitor ──HTTPS──► Cloudflare (app.biffkittz.com, proxied)
        ──TLS :8443 (Origin Rule), with Cloudflare's client certificate──► Container Apps environment (public IP;
          TCP ingress passes the connection through without decrypting it; IP restrictions allow Cloudflare's ranges)
        ──TLS :443──► Kestrel (origin certificate from Key Vault; validates Cloudflare's client certificate)
        ──TLS──► PostgreSQL flexible server (private access, no public endpoint)
```

The grocery list's WebSocket (`wss://app.biffkittz.com/api/grocery-list/hub`) takes the same path. Cloudflare proxies
WebSockets when the zone's **WebSockets** setting is on (the default), and the TCP ingress passes them through like
any other connection. SignalR's keep-alive pings every 15 seconds stop either from closing an idle connection.

### What the infrastructure provides to the app

| The app needs | How `infra/` provides it |
| --- | --- |
| The image | `<registry>.azurecr.io/login-app:<tag>`, pulled with the app's user-assigned identity (AcrPull). `app_image_tag` in `infra/environments/demo.tfvars` selects the tag |
| `ConnectionStrings__DefaultConnection` | Key Vault secret `ConnectionStrings--DefaultConnection` (as a Container Apps Key Vault reference): `Host=<server>.postgres.database.azure.com;Port=5432;Database=loginapp;Username=loginapp_app;Password=…;SSL Mode=VerifyFull` |
| `Jwt__SigningKey` | Key Vault secret `Jwt--SigningKey`: 64 random bytes, Base64 |
| A database role that owns its schema | The bootstrap job `caj-loginapp-demo-dbinit` creates `loginapp_app` with only `CONNECT` on `loginapp` and `CREATE` on schema `public`. The app then applies its own migrations |
| `OriginTls__CertificatePfxBase64` | The Key Vault certificate's secret (the origin certificate as a passwordless PFX, Base64) |
| `OriginTls__RequireCloudflareClientCertificate` | `true` (Terraform's `require_cloudflare_client_certificate`), together with Authenticated Origin Pulls in Cloudflare |
| Port 443 | TCP ingress exposing 8443 (Cloudflare's Origin Rule points there; Azure keeps 443 for HTTP ingress) and targeting 443, and `ASPNETCORE_URLS=https://+:443`. Set `app_target_port = 8443` if the platform refuses the low-port capability (section 2) |
| Real client IPs for rate limiting | The app's TCP peer is the ingress proxy (an internal address), so it trusts the `X-Forwarded-For` entry Cloudflare appended. Cloudflare's ranges are also added as `Network__InternalNetworks__100+` |
| Exactly one replica, 0.5 vCPU, 1 GiB | `app_min_replicas` / `app_max_replicas` = 1. A second replica isn't allowed (validation): realtime updates are broadcast in-process, with no SignalR backplane, so users on different replicas wouldn't see each other's changes |
| WebSockets end to end | Cloudflare's WebSockets setting (on by default, see [infra/docs/AUTHENTICATION.md](../infra/docs/AUTHENTICATION.md#zone-settings-to-check-once)); the TCP ingress passes them through |

Secrets reach the app as Container Apps Key Vault references. The app could also load Key Vault itself (set
`KeyVault__Uri` and `AZURE_CLIENT_ID`), but that provider reads *every* secret in the vault, including the
PostgreSQL administrator password, so the infrastructure doesn't use it.

### Health probes

Terraform configures these on the container:

| Probe | Path | Port | Purpose |
| --- | --- | --- | --- |
| Startup | `/health/live` | 443 (HTTPS) | Allows time for migrations on first start: every 5 s, 36 failures (3 min) |
| Liveness | `/health/live` | 443 (HTTPS) | Restarts a hung process. Deliberately ignores the database, so an outage doesn't cause restart loops |
| Readiness | `/health/ready` | 443 (HTTPS) | Includes a database check; replicas that can't reach PostgreSQL get no traffic |

The probes connect to the container directly and present no client certificate, so these two paths are the only
ones the app serves without Cloudflare's certificate. They reveal only whether the app and its database are up.

### Releasing a new version

```bash
TAG="$(git rev-parse --short HEAD)"
az acr build --registry "$(terraform -chdir=infra output -raw container_registry_name)" \
  --image "login-app:$TAG" --file app/Dockerfile app
# set app_image_tag = "<TAG>" in infra/environments/demo.tfvars, then from infra/:
terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan && terraform apply demo.tfplan
```

Tag images immutably (commit SHA) rather than `latest`, so every revision maps to exactly one build. Don't change the
image with `az containerapp update`: Terraform owns the Container App, and the next apply would revert it.

## 4. Database migrations in Azure

By default the app applies pending EF Core migrations at startup (`Database__MigrateOnStartup=true`). This keeps
deployment to one step, which matters because the database is private: a CI agent outside the VNet couldn't reach
it. When several replicas start at once, EF Core 9+ takes a database lock (implemented by the Npgsql provider), so
each migration runs exactly once. Startup fails if migrating takes longer than `Database__MigrationTimeout`.

Prefer a separate migration step? Set `Database__MigrateOnStartup=false` and run an EF Core **migration bundle** from
anywhere with VNet access, for example a Container Apps job:

```bash
dotnet ef migrations bundle --project app/backend --configuration Release \
  --target-runtime linux-x64 --self-contained --output efbundle
./efbundle --connection "<connection string>"
```

Migrations only move forward. To roll back a release that changed the schema, ship a new migration that reverses
it; don't point an older image at a newer schema unless the change was backward compatible.

## 5. Day-2 operations

[infra/docs/DEPLOYMENT.md](../infra/docs/DEPLOYMENT.md#day-2-operations) has the full list. In short:

| Task | How |
| --- | --- |
| **Roll back** | Set `app_image_tag` back to the previous tag and apply. Mind schema changes (section 4) |
| **Rotate the database password** | `terraform apply -replace=random_password.postgresql_app`, then run the bootstrap job and restart the revision |
| **Rotate the JWT signing key** | `terraform apply -replace=random_bytes.jwt_signing_key`. Every session ends and users sign in again |
| **Scale** | Up, not out: raise `app_cpu` / `app_memory`. The app must stay one replica, because the realtime hub has no backplane (see ARCHITECTURE.md). Scaling out would first need one, such as Redis |
| **Logs** | `az containerapp logs show --name ca-loginapp-demo --resource-group rg-loginapp-demo`, or Log Analytics (`ContainerAppConsoleLogs_CL`). Logs are JSON, one event per line, and never contain passwords, tokens, email addresses or what is on the list |

Running replicas read their configuration only at startup. With versionless Key Vault references, Container Apps
picks up a new secret version within 30 minutes and restarts the revision itself.

## 6. Troubleshooting

App-level symptoms are below. Infrastructure symptoms (Cloudflare 52x errors, certificate imports, role propagation,
the bootstrap job) are in [infra/docs/DEPLOYMENT.md](../infra/docs/DEPLOYMENT.md#troubleshooting).

| Symptom | Cause and fix |
| --- | --- |
| Container exits with `SocketException (13): Permission denied` | The platform refused the low-port capability (section 2). Set `app_target_port = 8443` and apply |
| Startup: `Jwt:SigningKey is not configured` / `ConnectionStrings:DefaultConnection is not configured` | The secrets didn't reach the app: check the app's Key Vault references and its identity's **Key Vault Secrets User** role on both secrets |
| Startup times out connecting to PostgreSQL | Private DNS for the server isn't linked to the VNet, or the NSG on the PostgreSQL subnet blocks the Container Apps subnet |
| `password authentication failed for user "loginapp_app"` | The bootstrap job hasn't run since the password was created or rotated |
| `The remote certificate is invalid` from Npgsql | The host name in the connection string doesn't match the server certificate; use the `*.postgres.database.azure.com` name |
| Every request gets `403` and the log says `Refused a request from ...` | The ingress connects from a range missing in `Network:InternalNetworks`; add that CIDR |
| Users share one rate-limit bucket | A proxy in front of the app is outside `Network:InternalNetworks`, so the app stops walking `X-Forwarded-For` at that proxy. Cloudflare's ranges are added by `infra/`; add any other proxy's range |
| Readiness probe failing, liveness fine | The database is unreachable from the replica (see above); the app stays up and recovers by itself |
| The list's status pill stays on "Offline", and other people's changes appear only every 30 seconds | WebSockets don't get through: Cloudflare's **WebSockets** setting is off, or the phone's network blocks them. The list falls back to reloading over HTTP every 30 seconds. The browser's developer tools show the failing `wss://.../api/grocery-list/hub` request |
| Log warning `Refused a realtime connection opened by a page on another site` | A page on another origin tried to open the hub with a user's cookie (cross-site WebSocket hijacking), or a proxy rewrote the `Host` header so it no longer matches the browser's `Origin`. The first is refused correctly; for the second, make the proxy preserve `Host` |
