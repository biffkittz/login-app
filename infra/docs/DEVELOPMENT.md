# Development environment for the infrastructure

Setting up WSL Ubuntu and VS Code to work on `infra/`, the everyday check loop, and the conventions the code follows.
The application's own setup (the .NET SDK, Node.js, local PostgreSQL) is in the app's
[docs/DEVELOPMENT.md](../../docs/DEVELOPMENT.md).

- [1. Install the tools](#1-install-the-tools)
- [2. VS Code](#2-vs-code)
- [3. The everyday loop](#3-the-everyday-loop)
- [4. Tests](#4-tests)
- [5. Pre-commit hooks](#5-pre-commit-hooks)
- [6. Continuous integration](#6-continuous-integration)
- [7. Conventions](#7-conventions)
- [8. Upgrading Terraform and providers](#8-upgrading-terraform-and-providers)
- [9. Troubleshooting](#9-troubleshooting)

## 1. Install the tools

All commands run in the WSL Ubuntu terminal (Ubuntu 26.04 "resolute" here; the repositories below also support
24.04).

| Tool | Version | Used for |
| --- | --- | --- |
| Terraform | 1.17.1 or later 1.x | Everything |
| Azure CLI (Linux) | 2.91 or later | Signing in; `az acr build`, `az containerapp job start` |
| TFLint | 0.65 | Linting (`.tflint.hcl` pins the azurerm ruleset 0.33.0) |
| terraform-docs | 0.24 | Regenerating the reference sections in the READMEs |
| pre-commit | latest | Running the checks on every commit |
| ShellCheck | from apt | Linting `db-bootstrap.sh` |

**Terraform** from HashiCorp's apt repository (signed packages, updated by `apt upgrade`):

```bash
sudo apt-get update && sudo apt-get install -y gnupg software-properties-common wget
wget -qO- https://apt.releases.hashicorp.com/gpg | sudo gpg --dearmor -o /usr/share/keyrings/hashicorp-archive-keyring.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/hashicorp-archive-keyring.gpg] https://apt.releases.hashicorp.com $(lsb_release -cs) main" \
  | sudo tee /etc/apt/sources.list.d/hashicorp.list
sudo apt-get update && sudo apt-get install -y terraform
terraform version        # Terraform v1.17.1 or later
```

**Azure CLI** for Linux, from Microsoft's apt repository:

```bash
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
hash -r && which az      # /usr/bin/az, not /mnt/c/... (that's the Windows CLI through WSL interop)
```

If `which az` still shows `/mnt/c/...`, the Windows CLI comes first on `PATH`. Either remove Windows paths from WSL
(`appendWindowsPath = false` under `[interop]` in `/etc/wsl.conf`, then `wsl --shutdown` from Windows) or use
`/usr/bin/az` explicitly. The Windows CLI works, but it's slower and keeps its tokens in Windows.

**TFLint, terraform-docs, pre-commit and ShellCheck:**

```bash
# TFLint: the official installer verifies the release checksum
curl -sSfL https://raw.githubusercontent.com/terraform-linters/tflint/master/install_linux.sh | bash
tflint --version

# terraform-docs into ~/.local/bin
mkdir -p ~/.local/bin
curl -sSfLo /tmp/terraform-docs.tgz https://github.com/terraform-docs/terraform-docs/releases/download/v0.24.0/terraform-docs-v0.24.0-linux-amd64.tar.gz
tar -xzf /tmp/terraform-docs.tgz -C ~/.local/bin terraform-docs && terraform-docs --version

# pre-commit (isolated with pipx) and ShellCheck
sudo apt-get install -y pipx shellcheck && pipx ensurepath && pipx install pre-commit
```

Open a new terminal afterwards so `~/.local/bin` is on `PATH`.

## 2. VS Code

Open the repository's workspace from WSL: `code login-app.code-workspace`. It has an **infra** folder and recommends
these extensions (accept the prompt, or install them from the Extensions view):

| Extension | ID | Why |
| --- | --- | --- |
| HashiCorp Terraform | `hashicorp.terraform` | Syntax, validation, completion and docs on hover (terraform-ls); formats on save |
| ShellCheck | `timonwong.shellcheck` | Lints `modules/container-app/scripts/db-bootstrap.sh` |
| EditorConfig | `EditorConfig.EditorConfig` | Two-space indentation, LF line endings and trimmed whitespace from `.editorconfig` |
| Azure Resources | `ms-azuretools.vscode-azureresourcegroups` | Browse what Terraform created |
| Azure Container Apps | `ms-azuretools.vscode-azurecontainerapps` | Logs, revisions and jobs of the deployed app |

The workspace formats Terraform files with `terraform fmt` on save. The extension needs `terraform init` (or
`terraform init -backend=false`) in `infra/` before completion and validation work.

## 3. The everyday loop

```bash
cd ~/login-app/infra
terraform init -backend=false            # providers and modules only; works offline from Azure
terraform fmt -recursive                 # format
terraform validate                       # syntax and types
terraform test                           # 38 runs against mocked providers (a few seconds)
tflint --init && tflint --recursive      # lint (the root module and every module)
terraform-docs .                         # refresh the READMEs after changing variables, outputs or resources
```

Use `-backend=false` for anything that doesn't need real infrastructure. A real **plan** needs a configured backend
and credentials ([AUTHENTICATION.md](AUTHENTICATION.md)):

```bash
terraform init                           # connects to the state backend (once per clone)
terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan
```

Read the whole plan before applying: resources to replace (`-/+`) or destroy (`-`) deserve a second look. Then apply
exactly that file with `terraform apply demo.tfplan`.

## 4. Tests

The `tests/` folder uses Terraform's native test framework with **mocked providers**, so tests need no Azure or
Cloudflare credentials and create nothing.

| File | Checks |
| --- | --- |
| `tests/root.tftest.hcl` | Region, required tags, naming, the two-phase app deployment, the DNS record, generated secrets, and the validation of every input that has rules |
| `tests/modules.tftest.hcl` | Each module on its own: NSG rules and their order, delegations and service endpoints, private PostgreSQL on B1ms, Basic registry without admin user, Key Vault RBAC and per-secret scopes, public single-replica Container App with TCP ingress (8443 outside, HTTPS on 443 in the container) |
| `tests/mocks/` | Realistic values for computed attributes (resource IDs, FQDNs), so mocked applies pass the provider's own validation |

Assertions can reference only the root module's resources and the modules' outputs. To test a module's internals,
add a `run` block with a `module { source = "./modules/<name>" }` block, as `modules.tftest.hcl` does. A run with
`command = apply` uses the mocks to produce known values, which helps when an assertion needs a computed attribute.

## 5. Pre-commit hooks

`.pre-commit-config.yaml` at the repository root runs, on every commit that touches `infra/`:

- `terraform fmt`;
- `terraform validate`;
- TFLint;
- terraform-docs;
- ShellCheck.

The hooks use your locally installed tools.

```bash
cd ~/login-app
pre-commit install                 # once per clone
pre-commit run --all-files         # run everything now
```

If terraform-docs updates a README, the commit stops so you can review and stage the change. `terraform test` isn't a
hook; run it yourself (it takes seconds), and CI runs it on every pull request.

## 6. Continuous integration

`.github/workflows/infra.yml` runs on pull requests and on pushes to `main` that change `infra/`. It uses pinned
action versions, read-only permissions and no cloud credentials. In order, it runs:

- `terraform fmt -check`;
- `init -backend=false`;
- `validate`;
- `terraform test`;
- TFLint;
- a terraform-docs freshness check;
- ShellCheck.

It never plans against Azure. To add plan and apply jobs, follow
[AUTHENTICATION.md](AUTHENTICATION.md#cicd-with-openid-connect).

## 7. Conventions

**Layout.**
- Each module has `main.tf` (resources), `variables.tf`, `outputs.tf`, `versions.tf` (requirements) and a `README.md`.
- The root module keeps provider requirements in `providers.tf` and the backend in `backend.tf`.
- Environments are `environments/<name>.tfvars`.

**Names.**
- Resources follow the
  [Cloud Adoption Framework abbreviations](https://learn.microsoft.com/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations)
  (`rg-`, `vnet-`, `snet-`, `nsg-`, `cae-`, `ca-`, `caj-`, `kv`, `cr`, `psql-`, `id-`, `log-`), built in
  `local.names`.
- In Terraform, a module's single resource of a type is named `this`.

**Tags.** Every taggable Azure resource gets `local.tags` (`project`, `environment`, `managed_by`). New resources must
pass `tags = var.tags` (in modules) or `tags = local.tags` (in the root module). `tests/root.tftest.hcl` checks the
`project` tag.

**Variables.**
- Every variable has a `type` and a `description`.
- Use `validation` blocks for anything with known rules, `sensitive = true` for secrets, and defaults only where a
  sensible one exists.
- Never put a secret in a tfvars file (see [AUTHENTICATION.md](AUTHENTICATION.md#handling-secrets)).

**Outputs.** Every output has a description. If consumers must wait for something, for example role propagation, gate
the output with `depends_on` rather than adding `depends_on` at every call site.

**Comments.** Explain *why*: the requirement, the platform behavior or the trade-off. Don't narrate what the code
already says.

**Providers and modules.**
- azurerm first. Use azapi only for a feature or resource azurerm lacks, and say so in a comment.
- New providers or external modules need the project owner's approval.
- The current set is azurerm, cloudflare, random and time.

**Idempotency.**
- No provisioners or scripts that run during apply.
- `lifecycle.ignore_changes` only with a comment explaining the platform behavior it absorbs.
- After every apply, a second plan must show no changes.

**State.**
- Remote only (`backend.tf`); never commit `*.tfstate` or plan files.
- Do commit `.terraform.lock.hcl`.

**Adding or changing a resource:**
1. Change the module, including variables with validation and outputs.
2. Add or extend a test.
3. Run the loop in section 3.
4. Update the docs: the module README text, [ARCHITECTURE.md](ARCHITECTURE.md) (the diagram and tables), and
   [DEPLOYMENT.md](DEPLOYMENT.md) if operations change.

## 8. Upgrading Terraform and providers

1. Check the new versions' changelogs for breaking changes. azurerm publishes an upgrade guide for each major
   version.
2. Raise the constraints in `providers.tf` (and the minimums in `modules/*/versions.tf`, if a new feature is needed).
3. Update the lock file and verify:

   ```bash
   terraform init -backend=false -upgrade
   git diff .terraform.lock.hcl          # only the providers you meant to upgrade should change
   terraform validate && terraform test && tflint --recursive
   terraform plan -var-file=environments/demo.tfvars   # with credentials: expect no changes unless intended
   ```

4. For Terraform itself, install the new version, raise `required_version` in `providers.tf` and in the modules, and
   update `terraform_version` in `.github/workflows/infra.yml`.

## 9. Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Module not installed` when running `terraform test` | Run `terraform init -backend=false`. Test files reference modules that must be installed too |
| `Backend initialization required` | Run `terraform init`. For checks that don't need state, use `terraform init -backend=false` |
| `Error: Failed to query available provider packages` | No access to registry.terraform.io (proxy or VPN); check `curl -I https://registry.terraform.io` |
| TFLint: `Plugin "azurerm" not found` | Run `tflint --init` in `infra/` |
| `az` is slow or reports Windows paths | You are using the Windows CLI through interop; install the Linux CLI (section 1) |
| The Terraform extension shows no completion | Run `terraform init -backend=false` in `infra/`, then **Terraform: Restart language server** |
| A hook rewrote files and the commit stopped | Review the changes, `git add` them, and commit again |
