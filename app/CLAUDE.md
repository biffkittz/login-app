# app/: Nibbles (the Login App code base)

A realtime family grocery list. ASP.NET Core 10 minimal API (`backend/`, project `LoginApp.Api`) with a SignalR hub,
which also serves the compiled Vue 3 SPA (`frontend/`) from `wwwroot`. Tests: `backend.tests/` (xUnit v3) and Vitest
in `frontend/`. Full detail in `docs/DEVELOPMENT.md`, `docs/BUILD_AND_DEPLOY.md` and `docs/ARCHITECTURE.md` at the
repo root.

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

- Feature folders: `Features/Auth/` (accounts, sessions) and `Features/GroceryList/` (the list and its hub) each hold
  their endpoints, service, options and log messages. Cross-cutting code is under `Infrastructure/` (Configuration,
  Data, Hosting, Networking, Security, Tls).
- Central package versions in `Directory.Packages.props`; shared settings in `Directory.Build.props`. Analyzers
  and `.editorconfig` style are enforced: warnings are errors in Release, so build Release before calling it done.
- The Docker build context is `app/`, so the image build doesn't see the root `.editorconfig`. An analyzer setting the
  image needs (migrations as generated code, for example) belongs in an `.editorconfig` under `app/`. To check what
  the image build will do, publish a copy of `app/backend` plus the `Directory.*.props` files from outside the repo.
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

## Grocery list and realtime (`Features/GroceryList/`, `frontend/src/stores/grocery-list.ts`)

Read the "The grocery list" section of `docs/ARCHITECTURE.md` before changing this. The rules that keep phones in
agreement:

- Writes are REST (`/api/grocery-list/...`); each answers with a `GroceryListChange` and the hub broadcasts the same
  change to everyone (author included) after the commit, without the request's cancellation token. The hub
  (`/api/grocery-list/hub`) has no client-callable methods; keep it push-only.
- Every change to an item, moves included, increments `Version` (also the EF concurrency token). The SPA keeps the
  highest version, remembers deleted IDs, and reloads the whole list on every (re)connect. Don't add events that rely
  on delivery order.
- Store choices come from `GroceryList:Stores` (appsettings.json) and are sent with `GET /api/grocery-list`; the
  server rejects other stores (400 on `Store`) and saves its own spelling. The SPA groups by store
  (`utils/store-groups.ts`: no store first, then the configured order, then legacy stores). Items move within their
  group (`listIndexForGroupMove` turns that into a place in the whole list) or are dropped into another, which
  changes their store (`listIndexForGroupTransfer`; `store` on the move request, one change with one version).
  Legacy-store groups take no drops. `components/grocery/grocery-store-groups.vue` owns the drag across groups.
- Order is a fractional `Position` (`ItemPositions`); a move names its neighbor (`afterItemId`), never an index.
  Moving doesn't change `LastModifiedAt`/`LastModifiedById`; a move that changes the store does.
- `PATCH` carries only the changed fields (null = unchanged, `""` = clear); the editor sends only what the user edited.
- Writes take turns on `GroceryListWriteLock` (in-process): fine because the app is one instance (SignalR has no
  backplane; Terraform enforces one replica).
- Client IDs: the SPA generates item UUIDs so optimistic adds match the server's copy.
- The SPA connects WebSockets-only with `skipNegotiation`, waits about a second before any reconnect (an immediate
  one lands on a server that is shutting down), reloads on resume after 15 s in the background, and reloads over
  HTTP every 30 s while offline (that is also how it notices a 401).
- AI pictures (`Features/GroceryList/Images/`): creating or renaming an item (a new name, not just new casing) sets
  `ImagePending`, or reuses the picture already drawn for that name (`GroceryItemImage`, one per `NameKey`).
  `GroceryImageWorker` (hosted service) drains pending items from the database at startup and when
  `GroceryImageSignal` wakes it, calls Gemini (`GeminiImageGenerator`, REST `generateContent`, key in the
  `x-goog-api-key` header), shrinks to a 128 px WebP (`GroceryImageThumbnails`, SkiaSharp) and finishes through
  `IGroceryListService.FinishImageAsync`, which bumps `Version` but not `LastModifiedAt`. Never send Gemini anything
  but the name, and never log names. `ImageGeneration:MaxImagesPerDay` caps spend (in memory).
- Cost estimates (`Features/GroceryList/CostEstimates/`): `POST /api/grocery-list/cost-estimate` (the Estimate Cost
  button). `CostEstimateService` reads the list, takes one of `CostEstimates:MaxEstimatesPerDay` (500, all users, in
  memory) and asks `GeminiCostEstimator` (model `gemini-3.1-flash-lite`, `v1beta` `generateContent`, the
  `ImageGeneration:ApiKey` key). The prompt carries the JSON Schema (`ResponseSchema`), which also goes in
  `responseJsonSchema`. Send only number, name, quantity and brand (as JSON); match answers by item number, never
  name; treat the answer as untrusted (`ReadEstimate`, `Clean`, the $10,000 cap) and add up the total in the app.
  Tests use `CostEstimateApiFactory` with `FakeCostEstimator`; never call Gemini.
- Picture URLs (`/api/grocery-list/images/{id}`) are immutable and the one API response that may be cached:
  `SecurityHeadersMiddleware` sets `no-store` only when an endpoint hasn't set `Cache-Control` itself.
- The list's settings (`GroceryListSettings`, one row, versioned) are shared by everyone: `PATCH
  /api/grocery-list/settings`, broadcast as `SettingsChanged`. Turning pictures off clears pending items.
- Integration tests use `Shopper` and `RealtimeListener` (`backend.tests/Features/GroceryList/`); `LoginApiFactory`
  uses a temporary SQLite file, because the hub and HTTP requests hit the database concurrently, and creates the
  schema before the host starts (the picture worker queries at once). It has no Gemini key; `PictureApiFactory` adds
  one plus `FakeImageGenerator`, so no test calls the paid API. Tests sharing that factory use unique item names.
- Publishing is per platform (`--use-current-runtime`, in the Dockerfile and `buildAndRunSite.sh`): portable output
  would carry SkiaSharp's native library for every OS (over 450 MB).

## Grocery runs (`Features/GroceryList/Runs/`, `Infrastructure/Temporal/`)

Read "Grocery runs" in `docs/ARCHITECTURE.md` first. The rules:

- `GroceryRunWorkflow` (type `GroceryRun`) decides; `GroceryRunActivities` (`OpenLastCall`, `RecordReady`,
  `StartShopping`, `EndRun`) save each numbered `GroceryRunState` through `GroceryRunStore.ApplyAsync` (only newer
  sequences apply; under `GroceryListWriteLock`) and broadcast `RunChanged`. The API (`GroceryRunService`) reads the
  database and only starts and signals workflows through `IGroceryRunCoordinator`; it never queries Temporal.
- Workflow code is replayed: use `Workflow.UtcNow`, lists (not hash sets), no I/O, no logging, no `Task.Run`. Names in
  attributes (workflow, signals `ImGood`/`StartShoppingNow`/`Cancel`/`FinishShopping`, query `State`, activities)
  and the enums' JSON names are in recorded histories: never rename them. A change to timers or activities needs
  `Workflow.Patched`; after one, record new histories into a new `Histories/v<N>` folder (docs/DEVELOPMENT.md) and keep
  the old ones.
- Payloads (`GroceryRunWorkflowInput`, `GroceryRunState`, `GroceryRunSignal`) hold IDs and times only.
- Participants are whoever has the list open when a run starts (`GroceryListPresence`, fed by the hub). Last call locks
  `DELETE` and `move` (`GroceryListWriteStatus.LockedForLastCall`, checked under the write lock); the lock lapses one
  minute past the deadline (`GroceryRunQueries.DeadlineGrace`), and a run is over past `ExpiresAt`, whatever its status.
- `TemporalWorkerService` replaces the SDK's hosted worker, which would stop the app when Temporal is unreachable. The
  `temporal` health check is Degraded at worst and not part of readiness. Keep both properties.
- Config: `Temporal:Address`/`:Namespace`/`:ApiKey`/`:TaskQueue` (off without an address; plain text to loopback only),
  `GroceryRuns:LastCallMinutes` (appsettings.json) and `:ShoppingTimeLimitMinutes`. Log events 5040-5047 (runs),
  7000-7003 (worker); metrics `nibbles.grocery_runs.*` and the SDK's meter `Temporal` (`temporal_*`).
- Tests: `GroceryRunApiFactory` swaps in `FakeGroceryRunCoordinator` (API rules, the lock); `GroceryRunWorkflowTests`
  and `GroceryRunEndToEndTests` need the Temporal CLI (`TemporalDevServer`, skipped without it unless
  `TEMPORAL_TESTS_REQUIRED=true`); `TemporalOutageTests` and `GroceryRunReplayTests` need nothing. The app's clock is
  real in these tests (tokens are checked against it): place deadlines relative to now (`RunStates`).

## Registration emails (`Features/Auth/Notifications/`, `Infrastructure/Email/`)

- `AuthService.RegisterAsync` calls `IRegistrationNotifier.NotifyRegistered` after the account is saved; it only
  queues (`RegistrationNotificationQueue`, a bounded in-memory channel) and never throws. `RegistrationNotificationWorker`
  sends one email at a time with `IEmailSender` (`CloudflareEmailSender`: Cloudflare Email Service REST API,
  `POST accounts/{Email:AccountId}/email/sending/send`, bearer `Email:ApiToken`). Registration must never wait for or
  fail because of the email; keep it that way.
- Off unless `RegistrationNotifications:Recipients` has entries and `Email:AccountId`/`:ApiToken`/`:SenderAddress` are
  all set (all-or-none is validated at startup). Terraform sets them in Azure; locally they are empty.
- The email is plain text: site (`RegistrationNotifications:SiteUrl`), UTC time and the quoted display name with line
  breaks and bidi controls replaced (`RegistrationEmail.Sanitize`). Never put the registrant's email address in it, and
  never log addresses, names or email text (`RegistrationNotificationLog`, events 1100-1110).
- Retries: transient failures twice (5 s, 10 s), refusals and client timeouts never (a timeout may have sent it).
  `MaxPerDay` (50) caps a sign-up flood; the queue holds 100; at shutdown waiting emails get 10 s.
- Tests never call Cloudflare: `NotificationApiFactory` swaps in `FakeEmailSender`; the worker's timing tests use
  `FakeTimeProvider`. Source files must stay ASCII: write `\uXXXX` escapes for special characters in C# literals
  (a literal U+2028 breaks a string).

## Metrics (`Infrastructure/Telemetry/`, `*Metrics.cs` in each feature)

- Instruments come from `IMeterFactory` on the `Nibbles` meter (`ApplicationMeter.Name`), named `nibbles.<area>.<what>`,
  one small `*Metrics` singleton per feature (e.g. `GroceryListMetrics`). Tag values are fixed strings (constants on
  the class); never a user, name, email or anything a user typed.
- Export is off unless `Telemetry:OtlpEndpoint` is set (Terraform, in Azure): `AddApplicationTelemetry` reads the
  section at registration to decide whether to add the OTLP pipeline. New built-in meters go in its list.
- The dashboards in `infra/modules/observability/dashboards` and the table in `docs/ARCHITECTURE.md` ("Metrics") name
  the Prometheus forms (`nibbles_signalr_messages_total`, ...): keep them in step with the instruments.
- Tests read instruments with `MetricCollector<T>` (`Microsoft.Extensions.Diagnostics.Testing`) on the host's
  `IMeterFactory`; `MetricsExportTests` checks the OTLP export against a local stand-in receiver.

## Origin TLS (`Infrastructure/Tls/`)

- `OriginTls:CertificatePfxBase64` (passwordless PFX from Key Vault) makes Kestrel serve HTTPS; validated at startup.
- `OriginTls:RequireCloudflareClientCertificate`: Kestrel uses `AllowCertificate` and validates any presented
  certificate against the embedded Cloudflare origin-pull CA only (`cloudflare-origin-pull-ca.pem`, expires
  2029-11-01; `.gitignore` has an exception for it). The middleware 403s requests without one, except
  `/health/live` and `/health/ready`, which the Container Apps probes call without a certificate.
- `OriginTlsKestrelTests` runs real TLS handshakes against Kestrel; keep it passing when touching this code.

## Configuration in Azure

Set as Container App environment variables by Terraform (`infra/main.tf`), so config keys and Terraform must
agree:

- Key Vault secret names map to config keys: `ConnectionStrings--DefaultConnection`, `Jwt--SigningKey`,
  `ImageGeneration--ApiKey` (only when Terraform gets `TF_VAR_gemini_api_key`; it also powers cost estimates); the
  certificate's secret becomes `OriginTls__CertificatePfxBase64`.
- The same image runs as two instances (app.biffkittz.com and grocery.biffkittz.com). The grocery instance gets its
  connection string from `grocery-db-connection-string` (its own database) and `Jwt__Audience=grocery.biffkittz.com`,
  so a session from one instance isn't accepted by the other. The app itself needs no instance awareness: keep
  per-instance differences in configuration.
- Registration emails: `Email__AccountId`, `Email__SenderAddress` (`notifications@biffkittz.com`),
  `RegistrationNotifications__Recipients__N` and each instance's `RegistrationNotifications__SiteUrl` are plain
  variables; `Email__ApiToken` comes from Key Vault (`Email--ApiToken`, a Cloudflare account token for Email Sending).
  All only when `registration_notification_recipients` is set.
- `ASPNETCORE_URLS=https://+:443` overrides the image's `ASPNETCORE_HTTP_PORTS=80`.
- `Network__InternalNetworks__100+` holds Cloudflare's IP ranges, so `X-Forwarded-For` is trusted and rate
  limiting sees the real client IP. Indices 0-8 come from `appsettings.json`; keep them below 100.
- Grocery runs (with `temporal_namespace`): `Temporal__Address`, `Temporal__Namespace` and each instance's
  `Temporal__TaskQueue` (`nibbles-<instance>`) are plain variables; `Temporal__ApiKey` comes from Key Vault
  (`Temporal--ApiKey`).
- Metrics (with `observability_enabled`): `Telemetry__OtlpEndpoint` (Prometheus' internal OTLP URL),
  `Telemetry__OtlpUsername` (`app`) and each instance's `Telemetry__Site` (its host name) are plain variables;
  `Telemetry__OtlpPassword` comes from Key Vault (`Telemetry--OtlpPassword`).

## Container

`Dockerfile` builds both projects and runs as the non-root `app` user (HTTP 80 by default, HTTPS 443 in Azure), using
`setcap` on the dotnet binary to allow binding a low port. If that fails on Container Apps, the fallback is
`app_target_port = 8443` in Terraform.
