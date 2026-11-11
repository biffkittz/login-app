# infra/: Terraform for Azure and Cloudflare

Provisions Nibbles, the realtime grocery list built on the Login App: Cloudflare (proxied DNS, Authenticated Origin
Pulls) → public IP of an external Container Apps environment → TCP ingress on 8443 (TLS passed through) → Kestrel terminates TLS with the origin certificate from
Key Vault → PostgreSQL flexible server (private access). There is no Application Gateway (removed for cost). Two
instances run side by side: `main` (app.biffkittz.com, 8443, database `loginapp`) and `grocery`
(grocery.biffkittz.com, 8444, database `grocery`), from `additional_app_instances` in `environments/demo.tfvars`.
Details: `README.md`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`, `docs/AUTHENTICATION.md`, `docs/DEVELOPMENT.md`.

## Check loop (no credentials needed)

```bash
terraform init -backend=false     # also installs modules used by tests
terraform fmt -recursive && terraform validate
terraform test                    # mocked providers, a few seconds
tflint --init && tflint --recursive
terraform-docs .                  # regenerates README reference sections
shellcheck modules/container-app/scripts/*.sh modules/observability/scripts/*.sh
```

Pre-commit (`../.pre-commit-config.yaml`) and CI (`../.github/workflows/infra.yml`) run the same checks. Run all of
them before calling a change done. CI also plans pull requests and applies `main` after approval (`../docs/CI_CD.md`):
its scripts (`../.github/scripts/terraform-*`) must never print plan values or outputs (the repository is public),
and the saved plan only travels encrypted. You and CI's deploy identity both administer Key Vault
(`key_vault_administrator_object_id`, `key_vault_additional_administrators`); keep both set.

## Rules

- **Never `terraform apply` without the user's go-ahead.** A real plan needs `CLOUDFLARE_API_TOKEN`,
  `TF_VAR_tls_certificate_pfx_path` and `TF_VAR_tls_certificate_pfx_password`, plus `TF_VAR_gemini_api_key` once the
  app uses one (an apply without it removes the key and stops AI pictures and cost estimates). Don't touch
  `backend.tf` (the user's remote state config) or `.terraform/terraform.tfstate`.
- **Providers:** azurerm, cloudflare, random, time only. Anything else (including azapi or external modules) needs
  the user's approval and a comment explaining why. Prefer azurerm over azapi.
- **Idempotency:** a second plan must show no changes. No provisioners or apply-time scripts. Every
  `lifecycle.ignore_changes` gets a comment naming the platform behavior it absorbs.
- **Tags:** every taggable resource gets `local.tags` (root) or `var.tags` (modules); `project = "loginapp"` is
  required.
- **Variables:** type, description, validation where rules exist, `sensitive = true` for secrets. Never put a
  secret in a tfvars file.
- **Outputs:** gate on `depends_on` when consumers must wait (e.g. RBAC propagation via `time_sleep`).
- Don't hand-edit between the `BEGIN_TF_DOCS` / `END_TF_DOCS` markers; run `terraform-docs .`.

## Layout

Root (`main.tf`, `variables.tf`, `outputs.tf`, `providers.tf`, `backend.tf`) holds naming, tags, identities,
generated secrets and the Cloudflare records. Modules: `networking`, `registry`, `key-vault`, `database`,
`container-app` (environment, app, DB bootstrap job), `email-sending` (Cloudflare Email Service, only while
`registration_notification_recipients` lists anyone), `observability` (Prometheus, Grafana, their NFS share; only with
`observability_enabled`). Environment values: `environments/demo.tfvars`.

## Things that will bite

- **Names must not change.** The suffix comes from `name_suffix` in `demo.tfvars`, or else a deterministic hash of
  the subscription and name. Changing either renames, and so replaces, the registry, Key Vault and PostgreSQL server
  (losing data). Never derive it from something that varies, such as `timestamp()`.
- **Two-phase app deploy:** the Container App exists only when `app_image_tag` is set. The image must already be in
  ACR as `<app_image_repository>:<app_image_tag>` (default repository `login-app`; `terraform output next_steps`
  prints the matching `az acr build` command). Check with `az acr repository show-tags` when an apply says MANIFEST_UNKNOWN.
  Once set, keep `app_image_tag` set, or the app is destroyed.
- **DB bootstrap job** (`az containerapp job start`) creates the `loginapp_app` role; run it after the first apply
  and before deploying the app. The SQL in `db-bootstrap.sh` uses psql variables and `\gexec` to stay
  injection-safe and idempotent; keep it that way.
- **The database is private.** To query it, run psql from a temporary Container App in the same environment;
  credentials are Key Vault secrets `postgresql-app-password` / `postgresql-admin-password`.
- **Tests:** assertions can't reference child-module resources; use a `run` with `module { source = ... }`.
  Mocked applies still run provider ID validation, so mocks in `tests/mocks/` need realistic IDs.
- **Lock file:** commit `.terraform.lock.hcl`. Don't use `TF_PLUGIN_CACHE_DIR` here; it leaves symlinks that break
  later installs.
- PostgreSQL flexible server doesn't support service endpoints; private access (delegated subnet + private DNS) is
  deliberate.
- **The public ingress can't be fenced by an NSG** (external workload-profiles environment). "Only Cloudflare" is
  enforced by the app's client-certificate check, plus ingress IP restrictions. Keep
  `require_cloudflare_client_certificate` and the app's `OriginTls` settings in step: Cloudflare must present the
  certificate whenever the app requires it.
- **Exactly one replica.** The grocery list's realtime hub (SignalR) runs in-process with no backplane, so
  `app_max_replicas` is validated to be 1. Don't relax that without adding a backplane (not Azure SignalR Service:
  the requirements rule it out).
- **Instances** (`local.app_instances`: `"main"` plus `additional_app_instances`) share the environment, image,
  identity, Key Vault secrets and database role; each has its own Container App, DNS record, Origin Rule, exposed port,
  database and connection-string secret (`<key>-db-connection-string`), and additional ones set `Jwt__Audience` to
  their host name. Exposed ports must be unique (two TCP apps can't share one). After adding an instance, the
  bootstrap job must run again (it grants per database). The `moved` blocks in `main.tf` keep the pre-instance
  addresses of the main app and DNS record; keep them.
- **Registration emails** go through Cloudflare Email Service (`module.email_sending`): biffkittz.com onboarded for
  Email Sending (Cloudflare adds and locks `cf-bounce` MX/SPF, DKIM and `_dmarc` records itself; the domain's MXroute
  MX and SPF records are not ours, don't touch them), the recipients as verified destination addresses (each confirms
  Cloudflare's link once), and an account-owned token that reaches the app as `Email--ApiToken`. That token holds
  Email Sending Write, the narrowest group that can send (it also manages sending domains). The deploying Cloudflare
  token also needs the Account permissions Email Sending, Email Routing Addresses and Account API Tokens, all at
  Edit. Email Sending has no Zone form, although onboarding is a zone call; without it, onboarding answers 401 with
  code 2036. ACS Email was rejected (retiring 2028; it needs the root SPF record). The module takes Cloudflare's
  permission catalogue as a variable because mocks can't return lists: test choices in
  `tests/email-sending.tftest.hcl`, and use `override_module` for it in root runs.
- **The exposed port can't be 80 or 443:** the environment's HTTP ingress holds them ("exposedPort already in use").
  `cloudflare_ruleset.origin_port` (an Origin Rule) sends Cloudflare to 8443; the container still listens on 443.
  The Cloudflare token needs DNS, Zone Settings and Origin Rules edit.
- **Observability** (`module.observability`, `observability_enabled`): Prometheus (internal ingress, Basic auth users
  `app`, `grafana`, `prometheus` with generated passwords; `web.yml` holds bcrypt hashes) and Grafana (TCP ingress on
  `grafana_exposed_port` 8445, its own Origin Rule after the instances', the app's IP restrictions, TLS with the origin
  certificate converted to PEM by an init container, GitHub sign-in only). Grafana can't check Cloudflare's client
  certificate; don't claim it does. Their data lives on a Premium NFS share: Container Apps can't mount disks, and
  NFS forces `https_traffic_only_enabled = false` (the account takes no keys and admits only the Container Apps
  subnet). A new Prometheus revision waits for the old one's database lock, which is why its probes watch the exporter's
  port (8080): don't point them at 9090. On NFS, flock() is an fcntl lock: busybox's flock opens files read-only, so
  only `flock -s` works there (an exclusive attempt fails with EBADF). Config reaches the containers as environment variables that init containers
  write to disk; dashboards are `modules/observability/dashboards/*.json` (gzip+Base64, one variable each). Images
  are pinned by digest (`observability_images`); keep the digest form. The CI deploy identity may assign Monitoring
  Reader only because `scripts/setup-github-oidc.sh` lists it: a new role Terraform assigns needs adding there too.

