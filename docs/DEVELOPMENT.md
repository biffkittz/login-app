# Development guide

This guide takes you from a fresh Windows machine to running, debugging and testing Login App locally. The team
develops in **VS Code** connected to **WSL (Ubuntu)**: all code, tools and builds live inside Linux, and VS Code
on Windows is the editor.

- [1. What you need](#1-what-you-need)
- [2. Windows setup](#2-windows-setup)
- [3. Ubuntu (WSL) tooling](#3-ubuntu-wsl-tooling)
- [4. Local database](#4-local-database)
- [5. Get the code and open the workspace](#5-get-the-code-and-open-the-workspace)
- [6. Run the app](#6-run-the-app)
- [7. Debugging](#7-debugging)
- [8. Tests and quality checks](#8-tests-and-quality-checks)
- [9. Database migrations](#9-database-migrations)
- [10. Configuration reference](#10-configuration-reference)
- [11. Project conventions](#11-project-conventions)
- [12. Troubleshooting](#12-troubleshooting)

## 1. What you need

| Tool | Version | Used for |
| --- | --- | --- |
| Windows 10 22H2+ or Windows 11 with WSL 2 | Ubuntu 24.04 LTS or newer | The Linux environment everything runs in |
| VS Code (Windows) + WSL extension | Current | Editing and debugging inside WSL |
| .NET SDK | 10.0.x (pinned by `global.json`) | Building, testing and running the API |
| Node.js + npm | Node 24 LTS (≥ 24.15) or 22 (≥ 22.22.2) | Building and testing the Vue frontend |
| PostgreSQL | 16 or newer (17 matches Azure) | The local database |
| Docker Desktop with WSL integration | Current (optional) | Building and running the container image locally |
| Azure CLI | Current (optional) | Deploying to Azure |

## 2. Windows setup

1. **Install WSL 2 with Ubuntu.** In an elevated PowerShell, run the command below, restart when prompted, then
   open *Ubuntu* from the Start menu and create your Linux user.

   ```powershell
   wsl --install -d Ubuntu-24.04
   ```

2. **Install VS Code for Windows** from <https://code.visualstudio.com>, then install the **WSL** extension
   (`ms-vscode-remote.remote-wsl`). All other extensions are installed *into WSL* later (section 5).
3. **Optional: install Docker Desktop** and enable *Settings → Resources → WSL integration* for your Ubuntu distro.
   You only need it to build or run the container image locally (`docker` and `docker compose` then work inside
   WSL).

## 3. Ubuntu (WSL) tooling

Run everything below in the Ubuntu terminal.

### Base packages and Git

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install -y git curl ca-certificates openssl
git config --global user.name  "Your Name"
git config --global user.email "you@example.com"
# Never convert line endings: shell scripts with CRLF fail to run in Linux.
git config --global core.autocrlf input
```

### .NET 10 SDK

```bash
# Ubuntu 26.04 and newer ship .NET 10 in the standard repositories:
sudo apt install -y dotnet-sdk-10.0

# Ubuntu 24.04: get it from the Ubuntu .NET backports archive instead:
sudo add-apt-repository ppa:dotnet/backports
sudo apt update && sudo apt install -y dotnet-sdk-10.0

dotnet --list-sdks   # expect 10.0.x
```

If neither package source suits you, Microsoft's [install script](https://learn.microsoft.com/dotnet/core/install/linux-scripted-manual)
works on any distribution: `./dotnet-install.sh --channel 10.0`, then add `~/.dotnet` to your `PATH`.

### Node.js 24 LTS (via nvm)

Ubuntu's own `nodejs`/`npm` packages are usually too old for Vite 8, so use [nvm](https://github.com/nvm-sh/nvm). Check
the nvm README for its newest version number.

```bash
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.3/install.sh | bash
source ~/.bashrc
nvm install 24
nvm alias default 24
node --version   # v24.15 or newer
```

### Optional tools

```bash
sudo apt install -y postgresql-client shellcheck      # psql/pg_isready; linting for buildAndRunSite.sh
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash  # Azure CLI, for deployments
```

## 4. Local database

The API expects PostgreSQL on `localhost:5432` with database `loginapp`, user `loginapp` and password
`loginapp_dev`, as set in `app/backend/appsettings.Development.json`. These are throwaway local credentials. The
API creates its tables itself on startup by applying the EF Core migrations, so you only create the empty database.

**Option A: PostgreSQL installed in WSL (simplest)**

```bash
sudo apt install -y postgresql
sudo service postgresql start          # or, with systemd enabled: sudo systemctl enable --now postgresql
sudo -u postgres psql -c "CREATE ROLE loginapp WITH LOGIN PASSWORD 'loginapp_dev' CREATEDB;"
sudo -u postgres createdb --owner=loginapp loginapp
```

WSL does not start services automatically unless systemd is enabled (it is by default in recent Ubuntu WSL
images). If `pg_isready -h localhost` reports "no response", run `sudo service postgresql start` again.

**Option B: PostgreSQL in Docker**

```bash
docker run --detach --name loginapp-postgres --restart unless-stopped \
  --env POSTGRES_USER=loginapp --env POSTGRES_PASSWORD=loginapp_dev --env POSTGRES_DB=loginapp \
  --publish 127.0.0.1:5432:5432 --volume loginapp-pgdata:/var/lib/postgresql/data \
  postgres:17
```

**Different credentials?** Don't edit `appsettings.Development.json`. Override the connection string in your
personal [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) instead:

```bash
dotnet user-secrets --project app/backend set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=loginapp;Username=me;Password=my-password"
```

## 5. Get the code and open the workspace

Clone into the Linux file system (for example `~/src`), **not** under `/mnt/c`. Windows drives are much slower from
WSL and break file watching for hot reload.

```bash
mkdir -p ~/src && cd ~/src
git clone <repository-url> login-app
cd login-app

dotnet tool restore                   # installs dotnet-ef at the version pinned in dotnet-tools.json
dotnet restore app/LoginApp.slnx
(cd app/frontend && npm ci)

# Optional: a persistent JWT signing key. Without one, Development generates a random key at every start,
# which signs you out whenever the API restarts.
dotnet user-secrets --project app/backend set "Jwt:SigningKey" "$(openssl rand -base64 48)"

code login-app.code-workspace
```

VS Code reopens in WSL and offers to install the workspace's **recommended extensions**. Accept, or install them from
the Extensions view (filter: `@recommended`):

| Extension | ID | Why |
| --- | --- | --- |
| WSL | `ms-vscode-remote.remote-wsl` | Runs VS Code's server inside Ubuntu (install on Windows) |
| C# Dev Kit | `ms-dotnettools.csdevkit` | Solution explorer, test explorer, debugging. Free for individuals, open source and small teams; organizations need a Visual Studio subscription, otherwise use the C# extension alone |
| C# | `ms-dotnettools.csharp` | C# language service, `.editorconfig` formatting, debugger |
| Vue (Official) | `Vue.volar` | Vue + TypeScript language support (disable Vetur if installed) |
| ESLint | `dbaeumer.vscode-eslint` | Lint errors in the editor, fix on save |
| Prettier | `esbenp.prettier-vscode` | Formats frontend files on save |
| Oxc | `oxc.oxc-vscode` | oxlint diagnostics (fast first-pass linter) |
| Tailwind CSS IntelliSense | `bradlc.vscode-tailwindcss` | Class completion, design-token previews, `@theme` support |
| Vitest | `vitest.explorer` | Run and debug frontend tests from the Test view |
| EditorConfig | `EditorConfig.EditorConfig` | Applies `.editorconfig` to every file type |
| REST Client | `humao.rest-client` | Send the requests in `app/backend/LoginApp.Api.http` |
| PostgreSQL | `ms-ossdata.vscode-pgsql` | Browse the local or Azure database |
| Container Tools | `ms-azuretools.vscode-containers` | Dockerfile/compose IntelliSense, image and container management |
| Azure Container Apps | `ms-azuretools.vscode-azurecontainerapps` | Inspect and deploy the Container App |
| ShellCheck | `timonwong.shellcheck` | Lints `buildAndRunSite.sh` (needs the `shellcheck` package) |

### The workspace layout

`login-app.code-workspace` is a multi-root workspace with five folders:

| Folder | Path | Contents |
| --- | --- | --- |
| `login-app` | repository root | Docs, Dockerfile, scripts (the four folders below are hidden here to avoid duplicates) |
| `backend` | `app/backend` | The ASP.NET Core API |
| `backend.tests` | `app/backend.tests` | xUnit tests |
| `frontend` | `app/frontend` | The Vue SPA |
| `infra` | `infra` | The Terraform project; its tools and setup are in [infra/docs/DEVELOPMENT.md](../infra/docs/DEVELOPMENT.md) |

It also defines shared settings (format on save, LF line endings), debug configurations (section 7) and tasks
(*Terminal → Run Task…*):

- build, test, watch, lint and run the site;
- add or apply database migrations;
- check or plan the infrastructure.

## 6. Run the app

There are two ways to run locally. Both need the database from section 4.

### A. Day-to-day development (hot reload)

The Vite dev server serves the SPA with instant hot module replacement and **proxies `/api` to the API**, so the
browser sees a single origin, exactly like production.

```bash
# Terminal 1: the API on http://localhost:5082 (applies database migrations on startup)
dotnet watch run --project app/backend --launch-profile http

# Terminal 2: the SPA on http://localhost:5173
cd app/frontend && npm run dev
```

Browse to **<http://localhost:5173>**. In VS Code you can start both at once: *Run and Debug* (Ctrl+Shift+D) →
**Full stack: API + Vite** → F5.

The OpenAPI document is available in Development at <http://localhost:5082/openapi/v1.json>, and
`app/backend/LoginApp.Api.http` contains ready-made requests.

### B. The complete, production-like site

```bash
./app/buildAndRunSite.sh            # see --help for options (port, environment, skipping tests, ...)
```

The script installs dependencies, runs every test, builds the SPA, copies it into the API's `wwwroot`, publishes the
API in Release mode and starts it at **<http://localhost:8080>**. It is the same flow the Dockerfile uses; see
[BUILD_AND_DEPLOY.md](BUILD_AND_DEPLOY.md#1-buildandrunsitesh).

## 7. Debugging

| Launch configuration | What it does |
| --- | --- |
| **Backend: API (Development)** | Builds and starts the API under the .NET debugger: breakpoints in C# |
| **Frontend: Vite dev server** | Runs `npm run dev` in a debug terminal |
| **Frontend: debug in Edge** | Opens Edge on Windows against the Vite server: breakpoints in `.vue`/`.ts` files |
| **Full stack: API + Vite** | The first two together; stopping one stops both |

The [Vue.js devtools](https://devtools.vuejs.org/) browser extension shows component state and the Pinia store. The
Vite dev server also exposes Vue DevTools in-page (`vite-plugin-vue-devtools`): press Alt+Shift+D.

## 8. Tests and quality checks

Run these before pushing; CI should run the same commands. Run `dotnet` commands from inside the repository so the
SDK picks up `global.json`.

```bash
# Backend
dotnet test --solution app/LoginApp.slnx                   # xUnit v3 on Microsoft.Testing.Platform
dotnet build app/LoginApp.slnx --configuration Release     # analyzer and style warnings are errors in Release
dotnet format app/LoginApp.slnx --verify-no-changes        # formatting per .editorconfig (drop the flag to fix)
dotnet list app/LoginApp.slnx package --vulnerable --include-transitive

# Frontend (from app/frontend)
npm run test          # Vitest, single run (npm run test:unit watches)
npm run type-check    # vue-tsc, strict mode, including the tests
npm run lint          # oxlint, then ESLint (auto-fixes what it can)
npm run format        # Prettier
npm audit --omit=dev  # vulnerabilities in what ships to browsers
```

**What the tests cover**

- **Backend (`app/backend.tests`):**
  - Integration tests run the real middleware pipeline in memory (`WebApplicationFactory`), with SQLite standing
    in for PostgreSQL.
  - They cover registration, sign-in, sign-out and `/me`; cookie attributes; validation; lockout; rate limiting;
    CSRF-relevant content-type rejection; forged tokens; security headers; SPA routing; and health probes.
  - Unit tests cover token issuing, options validation and the internal-network middleware.
- **Frontend (`src/**/__tests__`):**
  - Validation rules and error normalization.
  - The composables (form validation, async actions, redirect safety).
  - The router guard and the auth store, including session expiry.
  - The field components' `v-model` and accessibility wiring.
  - The login view end to end.

The real EF Core migrations target PostgreSQL. They are exercised whenever the API starts against a real database,
either locally or by `buildAndRunSite.sh`.

## 9. Database migrations

The schema is owned by EF Core migrations in `app/backend/Infrastructure/Data/Migrations`. After changing an entity:

```bash
dotnet ef migrations add AddSomething --project app/backend --output-dir Infrastructure/Data/Migrations
dotnet ef migrations script --idempotent --project app/backend   # review the SQL it will run
```

Start the API (or run `dotnet ef database update --project app/backend`) and the migration is applied. The VS Code
tasks **db: add migration** and **db: apply migrations to the local database** do the same. To start over locally:
`dotnet ef database drop --project app/backend`, then start the API again.

## 10. Configuration reference

Configuration uses standard ASP.NET Core sources. Later sources win:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User secrets (Development only)
4. Environment variables
5. Azure Key Vault, when `KeyVault:Uri` is set

In environment variables `:` becomes `__`, for example `Jwt__SigningKey`. In Key Vault secret names it becomes `--`,
for example `Jwt--SigningKey`.

| Setting | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | none (required) | Npgsql connection string. Development: local database. Azure: from Key Vault |
| `Jwt:SigningKey` | none (required) | Base64 key of at least 32 bytes: `openssl rand -base64 48`. Development generates a temporary one if missing |
| `Jwt:Issuer`, `Jwt:Audience` | `login-app` | Written to and required on every token |
| `Jwt:ExpiryMinutes` | `60` | Session length (1–1440). Tokens can't be revoked, so keep it short |
| `AuthCookie:Name` | `__Host-LoginApp.Auth` | Development uses `LoginApp.Auth` (the `__Host-` prefix requires `Secure`) |
| `AuthCookie:Secure` | `true` | `false` in Development only (plain-HTTP localhost) |
| `Database:MigrateOnStartup` | `true` | Apply pending migrations when the app starts |
| `Database:MigrationTimeout` | `00:05:00` | Gives up (and fails startup) after this long |
| `RateLimiting:Authentication:PermitLimit` / `:Window` | `10` / `00:01:00` | Login and registration requests per client IP per window |
| `Network:RestrictToInternalNetworks` | `true` | Refuse (403) peers outside `Network:InternalNetworks` |
| `Network:InternalNetworks` | RFC 1918, 100.64/10, link-local, loopback, IPv6 ULA | CIDR list; also the proxies trusted for `X-Forwarded-*` |
| `KeyVault:Uri` | empty (off) | e.g. `https://kv-login-app.vault.azure.net/`; loads all vault secrets at startup |
| `KeyVault:ManagedIdentityClientId` | empty | User-assigned identity; falls back to `AZURE_CLIENT_ID`, then the system-assigned identity |

The API validates these at startup and refuses to start with a clear message if something required is missing or
unsafe, for example a short signing key or a `__Host-` cookie without `Secure`.

## 11. Project conventions

### Backend (C#)

The rules live in `.editorconfig` and `app/Directory.Build.props`. They are enforced during every build, and Release
builds (`buildAndRunSite.sh`, the Dockerfile) treat violations as errors.

- **Namespaces and layout:**
  - File-scoped namespaces; usings outside the namespace, with `System` first.
  - One type per file, with the file named after the type.
  - Code is organized by feature (`Features/Auth`) and infrastructure concern (`Infrastructure/*`).
- **Types and syntax:**
  - `var` only when the type is apparent from the right-hand side; explicit types everywhere else.
  - Target-typed `new()`.
  - Expression-bodied members for one-liners.
- **Formatting and naming:**
  - Braces on new lines (Allman style), always used.
  - Explicit access modifiers on every member.
  - Fields named `_camelCase` (`s_camelCase` when static).
- **Async:** async all the way down; every async method takes and honors a `CancellationToken`.
  `ConfigureAwait(false)` is unnecessary in ASP.NET Core app code (CA2007 is disabled with a justification).
- **Visibility and docs:** types are `internal` (least privilege) unless a framework needs them public, such as the
  request contracts used by the validation source generator. XML documentation is required on all types; comments
  explain *why*, not *what*.
- **Error handling:** expected failures (wrong password, duplicate email) are result values, not exceptions.
  Configuration problems fail fast at startup through validated options.
- **Logging:** source-generated `[LoggerMessage]` methods. Never log passwords, tokens or email addresses.

### Frontend (Vue 3 + TypeScript)

- **Components and API style:**
  - `<script setup lang="ts">` everywhere; the Options API is banned by ESLint (`vue/component-api-style`,
    `vue/block-lang`).
  - `defineModel()` for two-way binding.
  - Type-based `defineProps<Props>()`, `defineEmits<Emits>()` and `defineSlots()`, also enforced by ESLint.
- **Logic and state:**
  - Reusable logic lives in `src/composables/use*.ts`.
  - Global state is in Pinia setup stores (`src/stores`), and API access only in `src/services`.
- **Naming and layout:** component **files are kebab-case** (`text-field.vue`) while **component names are
  PascalCase** (`<TextField>`). Views (route-level containers) are in `src/views`; presentational components are in
  `src/components`.
- **Styling:** Tailwind utilities only. The design tokens (colors, fonts) are defined once in
  `src/assets/main.css` via `@theme`; every text/background pairing was checked against WCAG 2.2 AA contrast.
- **Accessibility:**
  - Every input has a label and its errors are linked with `aria-describedby`.
  - Failed submits move focus to the first invalid field.
  - Route changes focus the new page's heading or first field.
  - The layout provides a skip link, honors `prefers-reduced-motion`, and keeps focus rings visible.
- **Security:** no `v-html`, and no tokens in JavaScript: the session lives in an HttpOnly cookie.
- **TypeScript:** strict mode plus `noUncheckedIndexedAccess`, `noUnusedLocals` and friends.

## 12. Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform` | Run `dotnet test` from inside the repository so `global.json` applies, and use `dotnet test --solution app/LoginApp.slnx` |
| API log: `The WebRootPath was not found` | Expected when only the API is running: the SPA is served by Vite (6A) or built by `buildAndRunSite.sh` (6B) |
| Signed out every time the API restarts | Development is using a temporary signing key; set one with `dotnet user-secrets` (section 5) |
| API fails at startup: connection refused / timeout | PostgreSQL isn't running: `sudo service postgresql start`, then check `pg_isready -h localhost` |
| `password authentication failed for user "loginapp"` | Recreate the role with password `loginapp_dev` (section 4) or set your own connection string in user secrets |
| Startup error mentioning `Jwt:SigningKey` or `ConnectionStrings:DefaultConnection` | A required secret is missing outside Development: supply it via environment variable or Key Vault |
| `/usr/bin/env: 'bash\r': No such file or directory` | A Windows editor saved the script with CRLF line endings. Convert it back with `sed -i 's/\r$//' app/buildAndRunSite.sh` (`.gitattributes` keeps LF in the repository) |
| Hot reload doesn't notice changes; npm is very slow | The repository is under `/mnt/c`; clone it into the Linux file system instead |
| `npm WARN EBADENGINE` | Your Node.js is older than the toolchain needs: `nvm install 24` |
| `Address already in use` on port 5082, 5173 or 8080 | Find the process with `ss -ltnp \| grep :5082` and stop it, or pass `--port` to `buildAndRunSite.sh` |
| Sign-in works in Chrome/Edge but not Safari on `http://localhost:8080` in Production mode | Production cookies are `Secure`; Safari refuses them over plain HTTP even on localhost. Use the Development environment locally |
| Workspace settings show "Value is not accepted" for formatters | The recommended extensions aren't installed yet (section 5) |
