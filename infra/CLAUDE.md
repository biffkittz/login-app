# infra/: Terraform for Azure and Cloudflare

Provisions the Login App: Cloudflare (proxied DNS) → Application Gateway Standard_v2 (TLS with the Cloudflare
origin certificate from Key Vault) → HTTP :80 → internal Container Apps environment → PostgreSQL flexible server
(private access). Details: `README.md`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`, `docs/AUTHENTICATION.md`,
`docs/DEVELOPMENT.md`.

## Check loop (no credentials needed)

```bash
terraform init -backend=false     # also installs modules used by tests
terraform fmt -recursive && terraform validate
terraform test                    # mocked providers, a few seconds
tflint --init && tflint --recursive
terraform-docs .                  # regenerates README reference sections
shellcheck modules/container-app/scripts/db-bootstrap.sh
```

Pre-commit (`../.pre-commit-config.yaml`) and CI (`../.github/workflows/infra.yml`) run the same checks; CI never
touches Azure. Run all of them before calling a change done.

## Rules

- **Never `terraform apply` without the user's go-ahead.** A real plan needs `CLOUDFLARE_API_TOKEN`,
  `TF_VAR_tls_certificate_pfx_path` and `TF_VAR_tls_certificate_pfx_password`. Don't touch `backend.tf` (the
  user's remote state config) or `.terraform/terraform.tfstate`.
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
generated secrets and the Cloudflare record. Modules: `networking`, `registry`, `key-vault`, `database`,
`container-app` (environment, app, DB bootstrap job), `application-gateway`. Environment values:
`environments/demo.tfvars`.

## Things that will bite

- **`name_suffix` must stay set** in `demo.tfvars` (`85705a`). The fallback in `main.tf` currently uses
  `timestamp()`, which changes every plan and would replace the registry, Key Vault and PostgreSQL server (losing
  data).
- **Two-phase app deploy:** the Container App exists only when `app_image_tag` is set. The image must already be in
  ACR as `<app_image_repository>:<app_image_tag>`; demo uses repository `loginapp`, not the default `login-app`.
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
  deliberate. NSGs are the authoritative control for "only App Gateway reaches the app".
