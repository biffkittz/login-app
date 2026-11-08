# app/: the Login App

ASP.NET Core 10 minimal API (`backend/`, project `LoginApp.Api`) that also serves the compiled Vue 3 SPA
(`frontend/`) from `wwwroot`. Tests: `backend.tests/` (xUnit v3) and Vitest in `frontend/`. Full detail in
`docs/DEVELOPMENT.md`, `docs/BUILD_AND_DEPLOY.md` and `docs/ARCHITECTURE.md` at the repo root.

## Commands (run from the repo root so `global.json` applies)

```bash
dotnet build app/LoginApp.slnx
dotnet test --solution app/LoginApp.slnx          # Microsoft.Testing.Platform: --solution is required
dotnet watch run --project app/backend --launch-profile http   # API on http://localhost:5082
cd app/frontend && npm run dev                     # SPA on http://localhost:5173 (proxies the API)
cd app/frontend && npm test && npm run type-check && npm run lint
./app/buildAndRunSite.sh                           # full build, tests, publish, run on :8080
dotnet ef migrations add <Name> --project app/backend --output-dir Infrastructure/Data/Migrations
```

`dotnet-ef` is a local tool: `dotnet tool restore` first.

## Backend conventions

- Feature folders: `Features/Auth/` holds the endpoints, service, options and log messages for auth.
  Cross-cutting code is under `Infrastructure/` (Configuration, Data, Hosting, Networking, Security).
- Central package versions in `Directory.Packages.props`; shared settings in `Directory.Build.props`. Analyzers
  and `.editorconfig` style are enforced: warnings are errors in Release, so build Release before calling it done.
- Options classes are validated at startup and fail fast. Logging uses source-generated `LoggerMessage` methods
  (e.g. `AuthLog`); never log passwords, tokens or emails.
- Cancellation: check the token at the start of a method, but never after a change has been saved. Cancelling
  then can't undo the change and would only skip logging it.
- EF Core migrations run at startup (`Database:MigrateOnStartup`), safely with several replicas. In Azure the app
  connects as `loginapp_app`, which owns the tables it creates; it has no elevated database rights.

## Auth design (don't weaken without asking)

- JWT signed with `Jwt:SigningKey`, valid `Jwt:ExpiryMinutes` (60), stored in the `__Host-LoginApp.Auth` cookie
  (`HttpOnly`, `Secure`, `SameSite=Strict`). There is no refresh yet; the SPA's auth store signs the user out
  when `sessionExpiresAt` passes.
- `/api/auth/me` re-reads the user from the database rather than trusting token claims.
- Lockout after five failed sign-ins; per-client-IP rate limiting on login and register.
- Passwords: minimum 12 characters, length-based policy (NIST SP 800-63B).

## Configuration in Azure

Set as Container App environment variables by Terraform (`infra/main.tf`), so config keys and Terraform must
agree:

- Key Vault secret names map to config keys: `ConnectionStrings--DefaultConnection`, `Jwt--SigningKey`.
- `Network__InternalNetworks__100+` holds Cloudflare's IP ranges, so `X-Forwarded-For` is trusted and rate
  limiting sees the real client IP. Indices 0-8 come from `appsettings.json`; keep them below 100.

## Container

`Dockerfile` builds both projects and runs as the non-root `app` user on port 80, using `setcap` on the dotnet
binary to allow binding a low port. If that fails on Container Apps, the fallback is `app_target_port = 8080` in
Terraform (the app reads `ASPNETCORE_HTTP_PORTS`).
