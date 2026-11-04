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

The container listens for plain HTTP on **port 80** (`ASPNETCORE_HTTP_PORTS=80`). TLS terminates upstream at
Application Gateway and the Container Apps ingress.

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

### Target architecture

The Terraform project in `infra/` provisions the shared infrastructure. The app expects:

| Resource | Requirements for this app |
| --- | --- |
| Virtual network + subnets | A subnet delegated to the Container Apps environment; a subnet for Application Gateway; private endpoints for PostgreSQL and Key Vault (or VNet integration) |
| Container Apps environment | **Internal** (`internal_load_balancer_enabled = true`): no public IP; reachable only from the VNet |
| Azure Database for PostgreSQL – Flexible Server | PostgreSQL 16 or 17, private access; a database `loginapp` owned by a dedicated login role |
| Azure Key Vault | RBAC authorization; holds the two app secrets below |
| Azure Container Registry | Holds the `login-app` images |
| User-assigned managed identity | Roles **AcrPull** on the registry and **Key Vault Secrets User** on the vault |
| Application Gateway | The entry point for users; forwards to the Container App over HTTPS |

### Variables used below

```bash
RESOURCE_GROUP=rg-login-app
REGISTRY=acrloginapp                  # registry name, without .azurecr.io
KEY_VAULT=kv-login-app
CONTAINERAPPS_ENV=cae-login-app       # the internal Container Apps environment
APP_NAME=ca-login-app
IDENTITY=id-login-app                 # user-assigned managed identity
POSTGRES_SERVER=psql-login-app
IMAGE_TAG="$(git rev-parse --short HEAD)"

IDENTITY_ID="$(az identity show -g "$RESOURCE_GROUP" -n "$IDENTITY" --query id -o tsv)"
IDENTITY_CLIENT_ID="$(az identity show -g "$RESOURCE_GROUP" -n "$IDENTITY" --query clientId -o tsv)"
KEY_VAULT_URI="$(az keyvault show -n "$KEY_VAULT" --query properties.vaultUri -o tsv)"   # ends with "/"
```

### Step 1: build and push the image

`az acr build` builds the Dockerfile inside Azure, so no local Docker is needed:

```bash
az acr build --registry "$REGISTRY" --image "login-app:$IMAGE_TAG" --file app/Dockerfile app
```

With Docker locally instead:

```bash
az acr login --name "$REGISTRY"
docker build --tag "$REGISTRY.azurecr.io/login-app:$IMAGE_TAG" app
docker push "$REGISTRY.azurecr.io/login-app:$IMAGE_TAG"
```

Tag images immutably (commit SHA) rather than `latest`, so every revision maps to exactly one build.

### Step 2: prepare the database role (once)

Connect to the server from inside the VNet (a jump box, Azure Bastion, or a Cloud Shell attached to the VNet) as the
server admin. Then create a login that owns the app's database. The app applies its own migrations, so it needs
to own its schema, but it never needs server-level rights.

```sql
CREATE ROLE loginapp_app WITH LOGIN PASSWORD '<generate a long random password>';
CREATE DATABASE loginapp OWNER loginapp_app;
```

### Step 3: store the secrets in Key Vault

| Secret name | Becomes setting | Value |
| --- | --- | --- |
| `ConnectionStrings--DefaultConnection` | `ConnectionStrings:DefaultConnection` | `Host=<server>.postgres.database.azure.com;Port=5432;Database=loginapp;Username=loginapp_app;Password=<password>;SSL Mode=VerifyFull` |
| `Jwt--SigningKey` | `Jwt:SigningKey` | `openssl rand -base64 48` |

```bash
az keyvault secret set --vault-name "$KEY_VAULT" --name "ConnectionStrings--DefaultConnection" \
  --value "Host=$POSTGRES_SERVER.postgres.database.azure.com;Port=5432;Database=loginapp;Username=loginapp_app;Password=<password>;SSL Mode=VerifyFull"
az keyvault secret set --vault-name "$KEY_VAULT" --name "Jwt--SigningKey" --value "$(openssl rand -base64 48)"
```

`SSL Mode=VerifyFull` encrypts the connection *and* verifies the server's certificate and host name. The runtime
image's CA bundle already trusts the roots that Azure Database for PostgreSQL certificates chain to. If the vault
only allows private access, run these commands from inside the VNet.

### Step 4: create the Container App

There are two supported ways to get the secrets into the app:

- **Option A (recommended): Container Apps Key Vault references.** The platform reads the secrets with the managed
  identity and exposes them as environment variables. No app code is involved.
- **Option B: the app reads Key Vault itself.** Set `KeyVault__Uri` (and `AZURE_CLIENT_ID` for a user-assigned
  identity). The app then loads every secret in the vault at startup with that managed identity, using
  `ManagedIdentityCredential` rather than `DefaultAzureCredential`, as the Azure SDK guidance recommends for
  production. It needs network access to the vault's private endpoint.

#### Option A

```bash
az containerapp create \
  --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --environment "$CONTAINERAPPS_ENV" \
  --image "$REGISTRY.azurecr.io/login-app:$IMAGE_TAG" \
  --registry-server "$REGISTRY.azurecr.io" --registry-identity "$IDENTITY_ID" \
  --user-assigned "$IDENTITY_ID" \
  --ingress external --target-port 80 --transport http \
  --min-replicas 1 --max-replicas 3 --cpu 0.5 --memory 1.0Gi \
  --secrets \
    "db-connection=keyvaultref:${KEY_VAULT_URI}secrets/ConnectionStrings--DefaultConnection,identityref:$IDENTITY_ID" \
    "jwt-signing-key=keyvaultref:${KEY_VAULT_URI}secrets/Jwt--SigningKey,identityref:$IDENTITY_ID" \
  --env-vars \
    "ConnectionStrings__DefaultConnection=secretref:db-connection" \
    "Jwt__SigningKey=secretref:jwt-signing-key"
```

#### Option B

Replace the `--secrets` and `--env-vars` arguments with:

```bash
  --env-vars "KeyVault__Uri=$KEY_VAULT_URI" "AZURE_CLIENT_ID=$IDENTITY_CLIENT_ID"
```

> **`--ingress external` on an internal environment does not mean public.** In an internal Container Apps
> environment, `external` means "reachable from the virtual network", which is what Application Gateway needs.
> `internal` would limit the app to other apps in the same environment. The environment itself has no public
> endpoint.

**Keep it VNet-only (defense in depth).** The app is unreachable from the internet because the environment is
internal. On top of that:

- Restrict ingress to the Application Gateway subnet:
  ```bash
  az containerapp ingress access-restriction set --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
    --rule-name allow-app-gateway --action Allow --ip-address <app-gateway-subnet-cidr>
  ```
- Keep network security groups on the subnets.
- The app refuses any peer outside `Network:InternalNetworks` (RFC 1918 and Azure platform ranges by default).
  Narrow that list to your VNet ranges if you like:
  `Network__InternalNetworks__0=10.20.0.0/16`, `Network__InternalNetworks__1=...`.

#### Health probes

Configure them in Terraform (`liveness_probe`, `readiness_probe` and `startup_probe` on the container) or in the
Container App YAML (`az containerapp update --yaml`):

| Probe | Path | Port | Purpose |
| --- | --- | --- | --- |
| Startup | `/health/live` | 80 | Allows time for migrations on first start: e.g. every 5 s, 36 failures (3 min) |
| Liveness | `/health/live` | 80 | Restarts a hung process. Deliberately ignores the database, so an outage doesn't cause restart loops |
| Readiness | `/health/ready` | 80 | Includes a database check; replicas that can't reach PostgreSQL get no traffic |

```yaml
# template.containers[0].probes in the Container App YAML
probes:
  - type: Startup
    httpGet: { path: /health/live, port: 80 }
    periodSeconds: 5
    failureThreshold: 36
  - type: Liveness
    httpGet: { path: /health/live, port: 80 }
    periodSeconds: 10
  - type: Readiness
    httpGet: { path: /health/ready, port: 80 }
    periodSeconds: 10
```

### Step 5: Application Gateway

Point a backend pool at the Container App's FQDN:
`az containerapp show -n "$APP_NAME" -g "$RESOURCE_GROUP" --query properties.configuration.ingress.fqdn -o tsv`.
It resolves privately through the environment's private DNS zone.

- **Backend settings:** HTTPS on port 443, with *Override with new host name → Pick host name from backend target*.
  The Container Apps ingress routes by host name.
- **Health probe:** HTTPS, path `/health/ready`, host name picked from the backend settings.
- **Listener:** HTTPS with your certificate (from Key Vault). For an intranet-only site, use the gateway's private
  frontend IP.

Application Gateway and the Container Apps ingress both add `X-Forwarded-For` and `X-Forwarded-Proto`. The app trusts
those headers only from the internal networks, and walks back through both hops to find the real client address,
which drives per-client rate limiting and logging. It also emits HSTS for HTTPS requests.

### Step 6: verify

From a machine in the VNet:

```bash
FQDN="$(az containerapp show -n "$APP_NAME" -g "$RESOURCE_GROUP" --query properties.configuration.ingress.fqdn -o tsv)"
curl -sS "https://$FQDN/health/ready"          # Healthy
curl -sS -o /dev/null -w '%{http_code}\n' "https://$FQDN/"   # 200 (the SPA)
az containerapp logs show -n "$APP_NAME" -g "$RESOURCE_GROUP" --tail 50   # JSON logs; look for "Database schema is up to date"
```

Then open the Application Gateway URL in a browser, register, sign out and sign in.

### Updating

```bash
az acr build --registry "$REGISTRY" --image "login-app:$IMAGE_TAG" --file app/Dockerfile app
az containerapp update --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
  --image "$REGISTRY.azurecr.io/login-app:$IMAGE_TAG"
```

Each update creates a new revision. Readiness probes keep traffic on the old revision until the new one is healthy.

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

| Task | How |
| --- | --- |
| **Roll back** | Re-deploy the previous image tag with `az containerapp update --image ...`, or with multiple-revision mode, shift traffic: `az containerapp ingress traffic set --revision-weight <previous-revision>=100` |
| **Rotate the database password** | `ALTER ROLE loginapp_app PASSWORD '...'`, update the Key Vault secret, then `az containerapp revision restart --revision <active-revision>` |
| **Rotate the JWT signing key** | Set a new `Jwt--SigningKey` secret and restart the revision. Every existing session ends and users sign in again |
| **Scale** | `az containerapp update --min-replicas N --max-replicas M`. The app is stateless; sessions live in the cookie |
| **Logs** | `az containerapp logs show` or Log Analytics. Logs are JSON, one event per line, and never contain passwords, tokens or email addresses |

Restart after changing a secret: running replicas only read their configuration at startup.

## 6. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| Container exits with `SocketException (13): Permission denied` | The platform refused the port-80 capability (section 2). Listen on 8080 (`ASPNETCORE_HTTP_PORTS=8080` + target port 8080) or run as root |
| Startup: `Jwt:SigningKey is not configured` / `ConnectionStrings:DefaultConnection is not configured` | The secrets didn't reach the app: check the `--secrets`/`--env-vars` names, or `KeyVault__Uri` and the identity's role |
| Startup: `403 Forbidden` from Key Vault | The managed identity lacks **Key Vault Secrets User**, or `AZURE_CLIENT_ID` points at another identity |
| Startup times out connecting to PostgreSQL | Private DNS or VNet integration for the server is missing, or the firewall blocks the environment's subnet |
| `The remote certificate is invalid` from Npgsql | The host name in the connection string doesn't match the server certificate; use the `*.postgres.database.azure.com` name |
| Every request gets `403` and the log says `Refused a request from ...` | The ingress or a proxy connects from a range missing in `Network:InternalNetworks`; add that CIDR |
| Users share one rate-limit bucket | A proxy in front of the app is outside `Network:InternalNetworks`, so its `X-Forwarded-For` is ignored; add its range |
| Readiness probe failing, liveness fine | The database is unreachable from the replica (see above); the app stays up and recovers by itself |
| `az acr build` fails with a network error | The registry blocks public access; allow trusted Azure services or use an ACR Tasks agent pool in the VNet |
