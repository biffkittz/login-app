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

The Vue SPA is compiled to static files and **served by the ASP.NET Core API itself**, so the browser only ever
talks to one origin. That single origin is what allows the session cookie to be `HttpOnly` and `SameSite=Strict`
without any CORS configuration (see [ARCHITECTURE.md](ARCHITECTURE.md)).

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

### Kestrel on port 80, as a non-root user

The container listens for plain HTTP on **port 80** (`ASPNETCORE_HTTP_PORTS=80`). TLS terminates upstream, at
Cloudflare's edge and at Application Gateway, which forwards plain HTTP to the Container Apps ingress inside the VNet
(section 3).

Since .NET 8, the official images provide an unprivileged `app` user, and the image runs as that user. Linux does
not normally let unprivileged processes bind ports below 1024. Some container runtimes lift that restriction
(Docker does by default) and some don't: users have reported `Permission denied` for exactly this setup on Azure
Container Instances. Rather than run as root, the Dockerfile grants the `dotnet` host binary **one** capability,
`CAP_NET_BIND_SERVICE`, using `setcap`.

Trade-off: Linux won't let other processes ptrace a process started from a binary with file capabilities. That means
you can't attach a debugger to the container and can't collect `createdump` crash dumps; `dotnet-counters` and
`dotnet-trace` still work. If you ever need those diagnostics, or a platform refuses file capabilities, use one of
these alternatives:

- **Preferred:** listen on 8080 instead. Set `ASPNETCORE_HTTP_PORTS=8080` and the Container App's target port to
  8080. Clients are unaffected, because the ingress still serves 80/443.
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
covers the network, Application Gateway, the internal Container Apps environment and app, PostgreSQL, Key Vault, the
registry and the Cloudflare DNS record. **[infra/docs/DEPLOYMENT.md](../infra/docs/DEPLOYMENT.md) is the step-by-step
guide**; this section describes what the app expects from that infrastructure.

### Request path

```text
visitor ──HTTPS──► Cloudflare (app.biffkittz.com, proxied)
        ──HTTPS──► Application Gateway (public IP; Cloudflare origin certificate from Key Vault; only Cloudflare may connect)
        ──HTTP :80, inside the VNet──► Container Apps ingress (internal environment; only the gateway subnet)
        ──HTTP :80──► Kestrel ──TLS──► PostgreSQL flexible server (private access, no public endpoint)
```

### What the infrastructure provides to the app

| The app needs | How `infra/` provides it |
| --- | --- |
| The image | `<registry>.azurecr.io/login-app:<tag>`, pulled with the app's user-assigned identity (AcrPull). `app_image_tag` in `infra/environments/demo.tfvars` selects the tag |
| `ConnectionStrings__DefaultConnection` | Key Vault secret `ConnectionStrings--DefaultConnection` (as a Container Apps Key Vault reference): `Host=<server>.postgres.database.azure.com;Port=5432;Database=loginapp;Username=loginapp_app;Password=…;SSL Mode=VerifyFull` |
| `Jwt__SigningKey` | Key Vault secret `Jwt--SigningKey`: 64 random bytes, Base64 |
| A database role that owns its schema | The bootstrap job `caj-loginapp-demo-dbinit` creates `loginapp_app` with only `CONNECT` on `loginapp` and `CREATE` on schema `public`. The app then applies its own migrations |
| Port 80 | Ingress target port 80 and `ASPNETCORE_HTTP_PORTS=80`. Set `app_target_port = 8080` if the platform refuses the port-80 capability (section 2) |
| Real client IPs for rate limiting | Cloudflare's ranges are added as `Network__InternalNetworks__100+`, so the app trusts the Cloudflare → gateway → ingress hops in `X-Forwarded-For` |
| One replica, 0.5 vCPU, 1 GiB | `app_min_replicas` / `app_max_replicas` = 1 (a demonstration environment) |

Secrets reach the app as Container Apps Key Vault references. The app could also load Key Vault itself (set
`KeyVault__Uri` and `AZURE_CLIENT_ID`), but that provider reads *every* secret in the vault, including the
PostgreSQL administrator password, so the infrastructure doesn't use it.

### Health probes

Terraform configures these on the container:

| Probe | Path | Port | Purpose |
| --- | --- | --- | --- |
| Startup | `/health/live` | 80 | Allows time for migrations on first start: every 5 s, 36 failures (3 min) |
| Liveness | `/health/live` | 80 | Restarts a hung process. Deliberately ignores the database, so an outage doesn't cause restart loops |
| Readiness | `/health/ready` | 80 | Includes a database check; replicas that can't reach PostgreSQL get no traffic |

Application Gateway probes `/health/ready` too, so the public site reports unavailability rather than errors while
the database is unreachable.

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
| **Scale** | `app_min_replicas` / `app_max_replicas` in the tfvars file. The app is stateless; sessions live in the cookie |
| **Logs** | `az containerapp logs show --name ca-loginapp-demo --resource-group rg-loginapp-demo`, or Log Analytics (`ContainerAppConsoleLogs_CL`). Logs are JSON, one event per line, and never contain passwords, tokens or email addresses |

Running replicas read their configuration only at startup. With versionless Key Vault references, Container Apps
picks up a new secret version within 30 minutes and restarts the revision itself.

## 6. Troubleshooting

App-level symptoms are below. Infrastructure symptoms (Cloudflare 52x errors, certificate imports, role propagation,
the bootstrap job) are in [infra/docs/DEPLOYMENT.md](../infra/docs/DEPLOYMENT.md#troubleshooting).

| Symptom | Cause and fix |
| --- | --- |
| Container exits with `SocketException (13): Permission denied` | The platform refused the port-80 capability (section 2). Set `app_target_port = 8080` and apply |
| Startup: `Jwt:SigningKey is not configured` / `ConnectionStrings:DefaultConnection is not configured` | The secrets didn't reach the app: check the app's Key Vault references and its identity's **Key Vault Secrets User** role on both secrets |
| Startup times out connecting to PostgreSQL | Private DNS for the server isn't linked to the VNet, or the NSG on the PostgreSQL subnet blocks the Container Apps subnet |
| `password authentication failed for user "loginapp_app"` | The bootstrap job hasn't run since the password was created or rotated |
| `The remote certificate is invalid` from Npgsql | The host name in the connection string doesn't match the server certificate; use the `*.postgres.database.azure.com` name |
| Every request gets `403` and the log says `Refused a request from ...` | The ingress connects from a range missing in `Network:InternalNetworks`; add that CIDR |
| Users share one rate-limit bucket | A proxy in front of the app is outside `Network:InternalNetworks`, so the app stops walking `X-Forwarded-For` at that proxy. Cloudflare's ranges are added by `infra/`; add any other proxy's range |
| Readiness probe failing, liveness fine | The database is unreachable from the replica (see above); the app stays up and recovers by itself |
