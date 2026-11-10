# CI/CD with GitHub Actions

How changes to Nibbles get from a pull request to <https://app.biffkittz.com> and <https://grocery.biffkittz.com>,
what keeps that path safe, and how to set it up. Every deployment is a reviewed Terraform plan, applied only after a
person approves it, and no Azure password, client secret or storage key exists anywhere in GitHub.

- [How it works](#how-it-works)
- [Security model](#security-model)
- [One-time setup](#one-time-setup)
- [Day to day](#day-to-day)
- [Troubleshooting](#troubleshooting)

## How it works

| Workflow | Runs on | Does |
| --- | --- | --- |
| [`app.yml`](../.github/workflows/app.yml) | every pull request; pushes to `main` that touch the app; manual | Backend build, format, tests and vulnerable-package check; frontend lint, types, tests and `npm audit`; image build and Trivy scan. On `main`: publish the image, then propose deploying it |
| [`infra.yml`](../.github/workflows/infra.yml) | every pull request; pushes to `main` that touch `infra/`; manual; weekly | fmt, validate, mocked tests, TFLint, terraform-docs, ShellCheck; a speculative plan on pull requests; on `main`, a plan and an approval-gated apply; a weekly drift check |
| [`workflows.yml`](../.github/workflows/workflows.yml) | changes to `.github/` or `infra/scripts/` | actionlint, zizmor (Actions security) and ShellCheck on the CI definitions themselves |
| [`dependabot.yml`](../.github/dependabot.yml) | weekly | Update pull requests for actions, NuGet, npm and Terraform providers, each release at least a week old |

The deployed image is whatever `app_image_tag` in `infra/environments/demo.tfvars` on `main` says. A release therefore
doesn't bypass Terraform: publishing an image opens a pull request that changes that one line, and merging it goes
through the same plan-and-approve path as any infrastructure change. Rolling back is reverting that pull request.

### Releasing a change to the app

```mermaid
sequenceDiagram
  actor Dev as You
  participant App as app workflow
  participant ACR as Container Registry
  participant Infra as infra workflow
  participant Azure as Azure + Cloudflare
  Dev->>App: merge a pull request that changes app/
  App->>App: backend and frontend checks
  App-->>Dev: approve "registry"? (build and push)
  Dev->>App: approve
  App->>App: build, scan with Trivy (HIGH/CRITICAL with a fix fail it)
  App->>ACR: push login-app:<12-character commit>
  App->>Dev: pull request "Deploy login-app:<tag>" (app_image_tag), with a speculative plan comment
  Dev->>Infra: merge it
  Infra->>Infra: checks, then plan (saved, encrypted)
  Infra-->>Dev: approve "demo"? (terraform apply; the plan summary lists what changes)
  Dev->>Infra: approve
  Infra->>Azure: terraform apply of exactly that plan
  Infra->>Azure: smoke test both sites through Cloudflare
```

Three decisions, each with its own control:

1. **Build and push the image?** The `registry` environment's required reviewers. Remove them to publish every merge
   automatically; nothing reaches the sites without step 3 anyway.
2. **Deploy this image?** Merging the deployment pull request.
3. **Apply this plan?** The `demo` environment's required reviewers, after reading the plan job's summary.

### Changing the infrastructure

A pull request that touches `infra/` gets the checks and a **speculative plan**, posted as a comment. It runs without
a refresh, as an identity that can only read, so it shows what the change does to the state but not drift. Merging
runs a full plan on `main` (the `demo-plan` environment), saves it encrypted, and waits for approval in `demo`. The
apply job applies that saved plan, never a new one, and Terraform refuses it if the state changed in between.

You can also run the infra workflow by hand on `main` (**Actions → infra → Run workflow**): a plan, or a plan and an
approval-gated apply with **apply** ticked. That covers changes that aren't in git, such as new Cloudflare IP ranges,
an origin certificate you renewed, or a rotated Gemini key.

### Jobs, environments and identities

| Job | GitHub environment | Signs in to Azure as | Can |
| --- | --- | --- | --- |
| app: checks, image scan | none | nobody | read the repository |
| app: publish | `registry` | `id-loginapp-github-push` | push images to the registry |
| app: propose deploy | none | nobody | create the deploy branch and pull request (`GITHUB_TOKEN`) |
| infra: checks | none | nobody | read the repository |
| infra: speculative plan | `infra-pr` | `id-loginapp-github-plan` | read the subscription's configuration and the state |
| infra: plan on main, drift check | `demo-plan` | `id-loginapp-github-deploy` | everything Terraform needs (it only plans) |
| infra: apply | `demo` | `id-loginapp-github-deploy` | everything Terraform needs |

## Security model

**No long-lived credentials.** The workflows sign in to Azure with GitHub's OpenID Connect tokens: Microsoft Entra ID
exchanges a short-lived token that GitHub issues for one job for a token of a user-assigned managed identity. Each
identity's federated credential names one GitHub environment of this repository
(`repo:biffkittz/login-app:environment:<name>`), so only jobs that run in that environment can use it, and the
environment decides which branches may run there and who must approve. Terraform's state backend uses Entra ID too
(`use_azuread_auth`), so no storage account key exists in CI.

**Least privilege per stage.**

| Identity | Roles | Why not less |
| --- | --- | --- |
| `id-loginapp-github-plan` | Reader on the subscription; Storage Blob Data Reader on the state container | Pull request plans don't refresh (`-refresh=false`) or lock the state (`-lock=false`), so reading is all they need |
| `id-loginapp-github-deploy` | Contributor on the subscription; Role Based Access Control Administrator on the subscription, **constrained** to AcrPull, Key Vault Secrets User, Key Vault Secrets Officer and Key Vault Certificates Officer; Storage Blob Data Contributor on the state container; Key Vault Secrets/Certificates Officer through Terraform | Terraform creates the resource group, registers resource providers and assigns those four roles. The condition stops it from granting anything else, Owner included |
| `id-loginapp-github-push` | AcrPush and Reader on the registry | Pushing is all publishing does; Reader lets `az acr login` find the registry |

**The repository is public, and so are its logs, job summaries and artifacts.**

- Terraform's plan output, which shows attribute values, never reaches the log. The summaries list each changing
  resource, its action and the names of the attributes that change
  ([`terraform-plan-summary.py`](../.github/scripts/terraform-plan-summary.py)); the app image is the only value shown.
- The apply log stops before Terraform's outputs (the origin IP address, the email recipients).
- A saved plan holds every secret in clear text, so it is encrypted (OpenPGP, AES-256 with an integrity check) with
  `TF_PLAN_ENCRYPTION_KEY` before it is uploaded for the apply job, and kept seven days.
- Pull requests from forks never run a job with secrets or an Azure identity: those jobs check that the branch is in
  this repository, and GitHub gives fork runs neither secrets nor OIDC tokens. Dependabot's branches are skipped too.

**Secrets live in environments, not the repository.** Every secret is stored in the environment that needs it, so a
job outside that environment, or on a branch the environment doesn't allow, can't read it. The deploy-strength
Cloudflare token exists only in `demo-plan` and `demo`, which accept `main` only; pull request plans get a read-only
token.

**Supply chain.**

- Every third-party action is pinned to a full commit SHA, with the release in a comment.
- Downloaded tools (Trivy, actionlint, zizmor, terraform-docs) are checked against pinned SHA-256 checksums.
- `terraform init -lockfile=readonly` installs only the provider builds `.terraform.lock.hcl` records.
- `npm ci` and `dotnet restore` install what the lock and props files say.
- No dependency caches are shared between pull requests and `main`.
- Dependabot proposes updates only after a release has been out for a week. Security updates come at once.

**Hardened jobs.** Each job starts with `contents: read` and gets only what it uses: `id-token: write` where it signs
in, write access only for the job that opens the deployment pull request. Checkouts keep no credentials
(`persist-credentials: false`), and untrusted values reach scripts as environment variables, never interpolated into
them (zizmor checks this). Images are scanned before they are pushed, and the registry gets only what was scanned.
Tags are immutable commit hashes, never `latest`.

**What remains.**

- Anyone who can push a branch to this repository can open a pull request whose plan job runs their code with the
  `infra-pr` secrets: the PFX, the Gemini key and a read-only Cloudflare token. The plan identity can also read the
  Terraform state, which holds every generated secret. With collaborators you don't trust that far, add required
  reviewers to `infra-pr`.
- The deploy identity is powerful: Contributor on the subscription. Keep the subscription for this app alone.
- With a single maintainer, the person who approves is the person who wrote the change. With a team, turn on
  **Prevent self-review** for `demo`.

## One-time setup

Do these in order. Steps 1 to 3 run on your machine with your own credentials, as in
[infra/docs/DEPLOYMENT.md](../infra/docs/DEPLOYMENT.md#prerequisites).

### 1. Create the Azure identities

From `infra/`, signed in as an Owner of the subscription, after the platform exists:

```bash
cd ~/login-app/infra
az login && az account set --subscription 3c3f379f-8161-4631-a30c-a3af82622a6a
./scripts/setup-github-oidc.sh
```

[`setup-github-oidc.sh`](../infra/scripts/setup-github-oidc.sh) creates the three identities in `rg-terraform-state`
(outside the Terraform-managed resource group, so `terraform destroy` can't remove the pipeline), their federated
credentials and their roles. It is safe to run again. It ends by printing the values for the next steps. Keep that
output.

### 2. Let the deploy identity manage Key Vault's contents

Terraform reads every secret back on each refresh, so the deploy identity needs Key Vault's data-plane roles before
CI's first plan. By default the role belongs to whoever runs Terraform, which would make each run take it from the
other. Name both of you instead: uncomment and fill in the two lines at the end of `environments/demo.tfvars`, with the
IDs the script printed:

```hcl
key_vault_administrator_object_id   = "<your object ID>"
key_vault_additional_administrators = { github_deploy = "<id-loginapp-github-deploy principal ID>" }
```

Then plan and apply once from your machine. The plan should only **add** two role assignments for the deploy identity
(and recreate the role-propagation wait); nothing else changes.

```bash
terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan && terraform apply demo.tfplan
terraform-docs .     # the new variables appear in the README reference; CI checks it is current
```

### 3. Create the Cloudflare API tokens

Make tokens for CI rather than reusing your personal one, so you can roll or revoke each separately. In the
Cloudflare dashboard: **My Profile → API Tokens → Create Token → Custom token**.

| Token | Permissions | Resources | Goes to |
| --- | --- | --- | --- |
| `loginapp CI deploy` | the full set in [AUTHENTICATION.md](../infra/docs/AUTHENTICATION.md#create-the-api-token): Zone · DNS, Zone Settings, Origin Rules · Edit; Account · Email Sending, Email Routing Addresses, Account API Tokens · Edit | zone `biffkittz.com`; the account that owns it | `demo-plan`, `demo` |
| `loginapp CI plan (read-only)` | Zone · Zone · Read; Account · Account API Tokens · Read | zone `biffkittz.com`; the account that owns it | `infra-pr` |

The read-only token only needs to read what the configuration looks up (the zone, and the token permission catalogue
for registration emails), because pull request plans don't refresh. Give both an expiry date and put a reminder in
your calendar. GitHub's runners have no fixed IP addresses, so IP filtering isn't possible.

### 4. Configure the repository's Actions settings

**Settings → Actions → General:**

- **Actions permissions:** *Allow biffkittz, and select non-biffkittz, actions and reusable workflows*, with **Allow
  actions created by GitHub** ticked and these allowed: `azure/login@*, hashicorp/setup-terraform@*,
  terraform-linters/setup-tflint@*`. Tick **Require actions to be pinned to a full-length commit SHA**.
- **Approval for running fork pull request workflows from contributors:** *Require approval for all external
  contributors*.
- **Workflow permissions:** *Read repository contents and packages permissions* (each job asks for more where it needs
  it), and tick **Allow GitHub Actions to create and approve pull requests**: the app workflow opens the deployment
  pull requests. None of the workflows approves anything.

### 5. Create the environments

**Settings → Environments → New environment**, four times:

| Environment | Deployment branches and tags | Required reviewers | Variables | Secrets |
| --- | --- | --- | --- | --- |
| `infra-pr` | No restriction (pull request branches) | none (add some for collaborators you don't fully trust) | `AZURE_CLIENT_ID` = plan identity | `CLOUDFLARE_API_TOKEN` (read-only token), `TLS_CERTIFICATE_PFX_BASE64`, `TLS_CERTIFICATE_PFX_PASSWORD`, `GEMINI_API_KEY` |
| `demo-plan` | Selected branches: `main` | none | `AZURE_CLIENT_ID` = deploy identity | `CLOUDFLARE_API_TOKEN` (deploy token), `TLS_CERTIFICATE_PFX_BASE64`, `TLS_CERTIFICATE_PFX_PASSWORD`, `GEMINI_API_KEY`, `TF_PLAN_ENCRYPTION_KEY` |
| `demo` | Selected branches: `main` | you (and, for a team, **Prevent self-review**) | `AZURE_CLIENT_ID` = deploy identity | `CLOUDFLARE_API_TOKEN` (deploy token), `TLS_CERTIFICATE_PFX_BASE64`, `TF_PLAN_ENCRYPTION_KEY` |
| `registry` | Selected branches: `main` | you (remove to publish every merge automatically) | `AZURE_CLIENT_ID` = push identity | none |

On `demo` and `registry`, untick **Allow administrators to bypass configured protection rules**, so the approval
can't be skipped by accident.

| Secret | Value | How to produce it |
| --- | --- | --- |
| `CLOUDFLARE_API_TOKEN` | A token from step 3 | Copied from the Cloudflare dashboard when you create it |
| `TLS_CERTIFICATE_PFX_BASE64` | The origin certificate PFX, Base64 on one line | `base64 -w0 ~/.secrets/biffkittz-origin.pfx` |
| `TLS_CERTIFICATE_PFX_PASSWORD` | The PFX's password (leave it out if the PFX has none) | Your password manager |
| `GEMINI_API_KEY` | The Gemini API key. Without it every plan removes the key and the app loses AI pictures and cost estimates; the plan warns | Google AI Studio |
| `TF_PLAN_ENCRYPTION_KEY` | A random key, the **same** in `demo-plan` and `demo`; keep a copy in your password manager to read plans locally | `openssl rand -base64 32` |

With the [GitHub CLI](https://cli.github.com) (`gh auth login` first), secrets are set from standard input, so they
stay out of your shell history:

```bash
base64 -w0 ~/.secrets/biffkittz-origin.pfx | gh secret set TLS_CERTIFICATE_PFX_BASE64 --env infra-pr
base64 -w0 ~/.secrets/biffkittz-origin.pfx | gh secret set TLS_CERTIFICATE_PFX_BASE64 --env demo-plan
base64 -w0 ~/.secrets/biffkittz-origin.pfx | gh secret set TLS_CERTIFICATE_PFX_BASE64 --env demo
gh secret set TLS_CERTIFICATE_PFX_PASSWORD --env infra-pr     # prompts for the value; repeat for demo-plan
gh secret set GEMINI_API_KEY --env infra-pr                   # prompts; repeat for demo-plan
gh secret set CLOUDFLARE_API_TOKEN --env infra-pr             # the read-only token
gh secret set CLOUDFLARE_API_TOKEN --env demo-plan            # the deploy token; repeat for demo
key="$(openssl rand -base64 32)"
for env in demo-plan demo; do printf '%s' "$key" | gh secret set TF_PLAN_ENCRYPTION_KEY --env "$env"; done
printf '%s\n' "$key"                                         # save it in your password manager, then:
unset key

gh variable set AZURE_CLIENT_ID --env infra-pr --body "<plan identity client ID>"
gh variable set AZURE_CLIENT_ID --env demo-plan --body "<deploy identity client ID>"
gh variable set AZURE_CLIENT_ID --env demo --body "<deploy identity client ID>"
gh variable set AZURE_CLIENT_ID --env registry --body "<push identity client ID>"
```

Repository secrets aren't used at all: every job outside an environment would be able to read them.

### 6. Set the repository variables

**Settings → Secrets and variables → Actions → Variables** (or `gh variable set NAME --body ...`). None of these are
secret: an identity's client ID is useless without a GitHub token from the right environment.

| Variable | Value |
| --- | --- |
| `AZURE_TENANT_ID` | The tenant the script printed |
| `AZURE_SUBSCRIPTION_ID` | `3c3f379f-8161-4631-a30c-a3af82622a6a` |
| `ACR_NAME` | The registry's name (`terraform output -raw container_registry_name`) |

### 7. Protect `main`

**Settings → Rules → Rulesets → New branch ruleset**, targeting the default branch, enforcement **Active**, no bypass
list:

- Restrict deletions; block force pushes.
- Require a pull request before merging. One required approval for a team; zero if you work alone, since you can't
  approve your own pull requests (you can approve the deployment pull requests, which GitHub Actions opens). Dismiss
  stale approvals on new commits; require conversation resolution.
- Require status checks to pass: **app checks passed** and **infra checks passed**. Both workflows run on every pull
  request, so these always report; they pass when nothing relevant changed. Leave *Require branches to be up to date*
  off, or every deployment pull request would need updating after each merge.

Also tick **Settings → General → Automatically delete head branches**, so merged `deploy/login-app-*` branches go away.

### 8. Turn on the repository's security features

**Settings → Code security**: secret scanning with push protection, Dependabot alerts and security updates, private
vulnerability reporting, and code scanning with the default CodeQL setup (C#, JavaScript/TypeScript and Actions;
free for public repositories).

### 9. The first run

Push the CI/CD changes on a branch and open a pull request: the checks and a speculative plan should pass, and the plan
should report no changes (step 2 applied the only one). Merge it. The infra workflow plans on `main`, finds nothing to
apply and stops; the app workflow asks for approval in `registry`, publishes this commit's image and opens its
deployment pull request. Merging that one is the first deployment through CI.

## Day to day

### Ship a change to the app

1. Open a pull request. The app checks and the image scan run; merge when green.
2. Approve **registry** in the run (Actions → app → the run → *Review deployments*).
3. Review and merge the **Deploy login-app:&lt;tag&gt;** pull request. Its comment shows the speculative plan: the app
   image changing on each Container App.
4. Approve **demo** in the infra run on `main` after reading the plan summary. The smoke test then checks both sites.

### Change the infrastructure

Open a pull request; read the plan comment; merge; approve **demo**. If a plan destroys or replaces something, the
summary says so in a warning: read every line of it.

### Review a plan in detail

The summaries leave out attribute values. To see the full plan an approval would apply, download and decrypt its
artifact on your machine, at the same commit:

```bash
gh run download <run ID> --repo biffkittz/login-app --name tfplan-<run ID> --dir "$HOME/.cache/tfplan"
read -rsp 'TF_PLAN_ENCRYPTION_KEY: ' key && echo
gpg --batch --quiet --pinentry-mode loopback --passphrase-fd 3 --decrypt \
  --output "$HOME/.cache/tfplan/tfplan" "$HOME/.cache/tfplan/tfplan.gpg" 3<<< "$key"; unset key
git switch --detach <commit of the run> && cd infra && terraform init
terraform show "$HOME/.cache/tfplan/tfplan"
shred -u "$HOME/.cache/tfplan/tfplan"        # it holds every secret in clear text
```

### Roll back

Revert the deployment pull request (**Revert** on its page), merge the revert, approve **demo**. The previous image is
still in the registry under its tag. To deploy an older one, open a pull request that sets `app_image_tag` to it.

### Plan or apply by hand

**Actions → infra → Run workflow** on `main`. Without **apply**, it only plans; with it, the apply waits for approval in
`demo`. Running it on another branch plans that branch speculatively.

### Drift

Every Monday the infra workflow plans `main` against the real infrastructure. If anything differs, the run fails (and
GitHub emails you) with a summary of what drifted. Usual causes: a change in the portal, new Cloudflare IP ranges, or a
provider update. Fix it by applying from `main` or by changing the code. GitHub pauses scheduled workflows in a public
repository after 60 days without activity; re-enable it under **Actions** if that happens.

### Dependabot pull requests

They run the checks without secrets. Merge app and action updates when green. For a Terraform provider update, start a
plan on its branch by hand (**Run workflow**, choosing the branch) before merging.

### Rotate a credential

The Azure identities have no secrets to rotate. For the others, update the secret in every environment that holds it:

| Credential | Rotate by |
| --- | --- |
| Cloudflare tokens | **Roll** in the Cloudflare dashboard, then update `CLOUDFLARE_API_TOKEN` in the environments |
| `TF_PLAN_ENCRYPTION_KEY` | A new random key in `demo-plan` and `demo`. Plans saved with the old key can no longer be applied: re-run them |
| Origin certificate | Update `TLS_CERTIFICATE_PFX_BASE64` (and the password) everywhere, then run the infra workflow with **apply** |
| Gemini key | Update `GEMINI_API_KEY` in `infra-pr` and `demo-plan`, then run the infra workflow with **apply** |

### Terraform on your own machine

It still works, and the state lock keeps you and CI from applying at once. Prefer CI for applies, so each one has a
reviewed plan and an entry in the environment's deployment history; keep local applies for setup and emergencies.
After a local apply, the next CI plan should report no changes.

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `AADSTS70021: No matching federated identity record found` | The job's subject doesn't match a federated credential: the environment's name, the repository name, or the job runs without its environment. Re-run `setup-github-oidc.sh` after renaming either |
| `AADSTS700016` or `Identity not found` | `AZURE_CLIENT_ID` in that environment isn't the identity's **client** ID (the script prints it) |
| Plan fails with `403` reading Key Vault secrets (`ForbiddenByRbac`) | The deploy identity isn't a Key Vault administrator yet: step 2, applied from your machine |
| Plan fails with `AuthorizationFailed` on the state (`Storage Blob Data`) | The identity's state role is missing or hasn't propagated yet (a few minutes); re-run `setup-github-oidc.sh` |
| `Error acquiring the state lock` | Someone else, or another run, holds it. Wait; `terraform force-unlock` only once you are sure nothing is running |
| Apply: `Saved plan is stale` | The state changed after the plan (another apply). Re-run the whole workflow, so it plans again |
| Apply: `decryption failed` | `TF_PLAN_ENCRYPTION_KEY` differs between `demo-plan` and `demo` |
| Speculative plan shows the Key Vault certificate or Gemini secret changing | The environment's PFX or Gemini secret differs from what was last applied |
| `terraform-docs is up to date` fails | Run `terraform-docs .` in `infra/` and commit the result |
| The publish job fails at `az acr login` or `docker push` with `unauthorized` | The push identity's AcrPush role is missing (re-run the script), or the registry was re-created. If the registry uses ABAC repository permissions, AcrPush isn't honored: assign *Container Registry Repository Writer* instead |
| Trivy fails the image | A HIGH or CRITICAL vulnerability with a fix: update the package (NuGet, npm) or rebuild to pick up a patched base image. The log names the package and the fixed version |
| No deployment pull request appears | **Allow GitHub Actions to create and approve pull requests** is off (step 4), or `app_image_tag` already has this tag |
| The deployment pull request waits for status checks forever | The checks were started with `workflow_dispatch`; look for the runs under Actions. Re-run *Propose deploying it* to start them again |
| Smoke test fails with `403` | Cloudflare challenged the runner (Bot Fight Mode or a WAF rule). Allow the user agent `login-app-ci-smoke-test` in a WAF skip rule, or check the app logs |
| `Required approval` never offered | You're not a required reviewer of the environment, or the job's branch isn't allowed there |
