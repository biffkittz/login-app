# Login App

A sign-up / sign-in / sign-out web app built to production standards, hosted on Azure Container Apps behind
Cloudflare and Application Gateway at https://app.biffkittz.com.

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
- Kestrel serves the site on port 80 and accepts requests from internal (VNet) sources only.

## Documentation

- When behavior, commands or configuration change, update the matching doc in the same change: the root
  `README.md`, `docs/*.md`, or `infra/docs/*.md` and the module READMEs.
- Style: plain, direct prose; comments and docs explain *why* (the requirement, platform behavior or trade-off),
  not what the code already says.
