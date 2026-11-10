# Nibbles (the Login App code base)

A realtime family grocery list for phones, at https://app.biffkittz.com: every signed-in user shares one list, and
changes reach every open list over SignalR (WebSockets) hosted in the app. It is built on a production-grade
sign-up / sign-in app whose names it keeps (`LoginApp.Api`, `loginapp` in Terraform; renaming Azure resources would
replace them). Hosted on Azure Container Apps behind Cloudflare; the container terminates TLS itself. A second,
independent instance of the same image runs at https://grocery.biffkittz.com, with its own database, accounts and list.

- `app/`: ASP.NET Core 10 API + Vue 3 SPA in one container. See `app/CLAUDE.md`.
- `infra/`: Terraform for Azure and Cloudflare. See `infra/CLAUDE.md`.
- `docs/`: app docs (DEVELOPMENT, BUILD_AND_DEPLOY, ARCHITECTURE). `infra/docs/` has the infrastructure docs.

## Ground rules

- The original requirements are in `app/prompts` and `infra/prompts`. They're the user's files: read them when a
  decision depends on intent, and don't edit them. `NOTES.md` and `.scratch/` are the user's too.
- Commit or push only when asked. `main` is the default branch, so branch first.
- Environment: WSL Ubuntu, VS Code via `login-app.code-workspace`. Use the Linux `az` (`/usr/bin/az`), not the
  Windows one.
- A PostgreSQL the user runs locally is on port 5432. Don't stop, reconfigure or reuse it for experiments; start a
  scratch cluster on another port instead.
- Never run `terraform apply` or create Azure resources without asking: they cost money and touch the live site.
  Read-only `az` and `terraform output` commands are fine.

## Security requirements (from the prompts)

- No hardcoded secrets. In Azure, the app reads them from Key Vault; locally, from `appsettings.Development.json`.
- Tokens live in `HttpOnly` cookies, never `localStorage`.
- HTTPS for every request; validate all input; log errors without sensitive data; follow OWASP guidance.
- In Azure, Kestrel serves HTTPS on 443 with the Cloudflare origin certificate and accepts only requests carrying
  Cloudflare's Authenticated Origin Pulls client certificate (health endpoints excepted). Locally it serves HTTP.
- SignalR runs in-process, never Azure SignalR Service (a requirement), so each instance runs exactly one replica. Writes go
  through the REST API; the hub only pushes.
- AI pictures of items come from the Gemini API (`ImageGeneration:ApiKey`; a secret like the others). Only item names
  are sent, and pictures are stored as small WebPs (a few KB). The Estimate Cost button uses the same key with Gemini
  3.1 Flash-Lite and sends only names, quantities and brands; its answer is checked against a JSON Schema, and 500
  estimates a day (all users) cap the spend. Tests never call Gemini; don't add ones that do.
- Every registration emails `registration_notification_recipients` (Terraform; the site owner) from
  notifications@biffkittz.com through Cloudflare Email Service, with a Cloudflare token limited to Email Sending,
  from Key Vault (`Email:ApiToken`). The email names the site, time and display name, never the registrant's email
  address.
  Registering never waits for or fails because of it. Tests never send email; don't add ones that do.

## CI/CD (`.github/`, `docs/CI_CD.md`)

- GitHub Actions signs in to Azure with OIDC only, one identity per GitHub environment (`infra-pr`, `demo-plan`,
  `demo`, `registry`); never add stored credentials or repository-level secrets. Pin actions to full commit SHAs and
  downloaded tools to checksums; pass untrusted values to scripts through `env`, never `${{ }}` inside `run`.
- The repository is public, so are its logs: never print Terraform plan values, outputs or secrets.
- Releases are a pull request that sets `app_image_tag` (opened by the app workflow); applies only run from `main`
  after approval in `demo`. Don't add a path that deploys around Terraform.

## Documentation

- When behavior, commands or configuration change, update the matching doc in the same change: the root
  `README.md`, `docs/*.md`, or `infra/docs/*.md` and the module READMEs.
- Style: plain, direct prose; comments and docs explain *why* (the requirement, platform behavior or trade-off),
  not what the code already says.
