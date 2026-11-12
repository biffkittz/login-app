# Deployment

How to deploy the infrastructure of Nibbles (the Login App's code base) from nothing, verify it, operate it, and tear
it down. Commands run in WSL from `infra/` unless they say otherwise.

- [How a first deployment works](#how-a-first-deployment-works)
- [Prerequisites](#prerequisites)
- [Phase 1: the platform](#phase-1-the-platform)
- [Phase 2: build and push the image](#phase-2-build-and-push-the-image)
- [Phase 3: create the app's database role](#phase-3-create-the-apps-database-role)
- [Phase 4: deploy the app](#phase-4-deploy-the-app)
- [Verify](#verify)
- [Registration emails](#registration-emails)
- [Observability](#observability)
- [Day-2 operations](#day-2-operations)
- [Teardown](#teardown)
- [Troubleshooting](#troubleshooting)

## How a first deployment works

The Container App can't be created before its image is in the registry: its first revision would fail to pull. It
also needs its database role before it starts. The first deployment therefore has four phases, and afterwards every
release is just phase 2 plus phase 4.

| Phase | You run | Creates | Time |
| --- | --- | --- | --- |
| 1. Platform | `terraform apply` (no `app_image_tag`) | Everything except the Container Apps: network, NSGs, Log Analytics, identities, registry, Key Vault + secrets + certificate, PostgreSQL + a database per instance, Container Apps environment (public IP), bootstrap job, Cloudflare records, Origin Rules and Authenticated Origin Pulls; with `registration_notification_recipients`, Cloudflare Email Service for registration emails | 15–25 min |
| 2. Image | `az acr build` | `login-app:<tag>` in the registry | 5–10 min |
| 3. Database role | `az containerapp job start` | Role `loginapp_app` with its password from Key Vault, allowed into every instance's database | 1–2 min |
| 4. App | `terraform apply` with `app_image_tag` | A Container App per instance (`app` and `grocery`); each migrates its own schema on first start | 3–5 min |

## Prerequisites

- [ ] Tools installed in WSL: Terraform 1.17+, Azure CLI, Git ([DEVELOPMENT.md](DEVELOPMENT.md#1-install-the-tools)).
- [ ] Azure: signed in, subscription selected, and **Owner** (or Contributor + Role Based Access Control
      Administrator) on it ([AUTHENTICATION.md](AUTHENTICATION.md#azure)).
- [ ] The state storage account (`demonstration` in `rg-terraform-state`) exists and you can read and write it
      ([AUTHENTICATION.md](AUTHENTICATION.md#remote-state)).
- [ ] A Cloudflare API token that can edit DNS, **zone settings and Origin Rules** in `biffkittz.com`, and, for
      registration emails, **Email Sending, Email Routing Addresses and Account API Tokens** (Edit) in its account
      ([AUTHENTICATION.md](AUTHENTICATION.md#cloudflare)).
- [ ] The origin certificate PFX for `*.biffkittz.com` and its password
      ([AUTHENTICATION.md](AUTHENTICATION.md#the-origin-certificate-pfx)).
- [ ] Cloudflare zone settings: SSL/TLS mode **Full (strict)**, **Always Use HTTPS**
      ([AUTHENTICATION.md](AUTHENTICATION.md#zone-settings-to-check-once)).

Set up the shell. Repeat this in every new terminal; nothing here is written to disk.

```bash
cd ~/login-app/infra
az login && az account set --subscription 3c3f379f-8161-4631-a30c-a3af82622a6a
read -rsp 'Cloudflare API token: ' CLOUDFLARE_API_TOKEN && export CLOUDFLARE_API_TOKEN && echo
export TF_VAR_tls_certificate_pfx_path=/home/biffkittz/login-app/infra/.secrets/biffkittz-origin.pfx
read -rsp 'PFX password: ' TF_VAR_tls_certificate_pfx_password && export TF_VAR_tls_certificate_pfx_password && echo
# Optional: AI pictures of items and cost estimates. Without it the app runs, and both say they aren't set up.
read -rsp 'Gemini API key: ' TF_VAR_gemini_api_key && export TF_VAR_gemini_api_key && echo
# With observability_enabled (see "Observability"): the GitHub OAuth App's client secret for Grafana.
read -rsp 'Grafana GitHub client secret: ' TF_VAR_grafana_github_client_secret && export TF_VAR_grafana_github_client_secret && echo
# With temporal_namespace (see "Grocery runs"): the Temporal Cloud API key.
read -rsp 'Temporal Cloud API key: ' TF_VAR_temporal_api_key && export TF_VAR_temporal_api_key && echo
```

Set `TF_VAR_gemini_api_key` in every terminal you apply from once you use it: an apply without it removes the key from
Key Vault and the app, and pictures and cost estimates stop (pictures already drawn stay).

## Phase 1: the platform

```bash
terraform init                                                     # once per clone; connects to the state backend
terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan
```

Review the plan before applying. On the first run it should show a few dozen resources to add, and **0 to change, 0 to destroy**.
Look for:

- `module.container_apps.azurerm_container_app.this` is **absent**, because `app_image_tag` isn't set yet;
- `module.container_apps.azurerm_container_app_environment.this` has `internal_load_balancer_enabled = false`;
- `cloudflare_zone_setting.authenticated_origin_pulls` sets `tls_client_auth` to `on`;
- `cloudflare_ruleset.origin_port` routes `app.biffkittz.com` to port 8443 and `grocery.biffkittz.com` to 8444;
- `cloudflare_dns_record.app["main"]` and `["grocery"]` are proxied and point at the environment's IP (known after apply).

```bash
terraform apply demo.tfplan
terraform output                                                   # names, IPs and the next_steps commands
```

The apply pauses for 90 seconds three times (partly in parallel) while new role assignments propagate. The slowest
resources are PostgreSQL and the Container Apps environment, about 5 to 15 minutes each, and they are created in
parallel. Until phase 4, `https://app.biffkittz.com` answers a Cloudflare **52x** error, because nothing listens on
the environment's IP yet.

## Phase 2: build and push the image

`az acr build` builds `app/Dockerfile` inside the registry, so no local Docker is needed. Run it from the
**repository root**:

```bash
cd ~/login-app
TAG="$(git rev-parse --short HEAD)"                                # immutable tag: one image per commit
REGISTRY="$(terraform -chdir=infra output -raw container_registry_name)"
az acr build --registry "$REGISTRY" --image "login-app:$TAG" --file app/Dockerfile app
```

To build locally with Docker instead:

```bash
az acr login --name "$REGISTRY"
docker build --tag "$REGISTRY.azurecr.io/login-app:$TAG" app
docker push "$REGISTRY.azurecr.io/login-app:$TAG"
```

## Phase 3: create the app's database role

The bootstrap job connects to the private PostgreSQL server from inside the VNet as the administrator. It creates
`loginapp_app` with the password stored in Key Vault, and grants it only what the app needs.

```bash
RG="$(terraform -chdir=infra output -raw resource_group_name)"
JOB="$(terraform -chdir=infra output -raw db_bootstrap_job_name)"
az containerapp job start --name "$JOB" --resource-group "$RG"
az containerapp job execution list --name "$JOB" --resource-group "$RG" \
  --query "sort_by(@, &properties.startTime)[-1].{name:name, status:properties.status, start:properties.startTime}" --output table
```

Repeat the last command until the status is **Succeeded**, usually within a minute. To see what it printed, open the
job in the portal (**Execution history → Console logs**), or query Log Analytics. If the CLI offers to install the
`log-analytics` extension, accept.

```bash
WORKSPACE="$(az monitor log-analytics workspace show --resource-group "$RG" --workspace-name log-loginapp-demo --query customerId -o tsv)"
az monitor log-analytics query --workspace "$WORKSPACE" --output table --analytics-query \
  "ContainerAppConsoleLogs_CL | where ContainerName_s == 'psql' | order by TimeGenerated desc | take 20 | project TimeGenerated, Log_s"
```

Expected output, one line per instance's database, then `Done.` (Console logs reach Log Analytics a few minutes late.)

```text
'loginapp_app' can connect to 'loginapp' and create its schema objects.
'loginapp_app' can connect to 'grocery' and create its schema objects.
```

## Phase 4: deploy the app

Set the image tag in `environments/demo.tfvars` and commit it, so the deployed version is recorded:

```hcl
app_image_tag = "1a2b3c4"   # the TAG from phase 2
```

```bash
cd ~/login-app/infra
terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan   # expect: 2 to add (a Container App per instance)
terraform apply demo.tfplan
```

Keep `app_image_tag` set from now on. Removing it again plans to **destroy** the Container Apps. Every instance runs
this tag, so a release updates them all.

## Verify

```bash
curl -sS -o /dev/null -w '%{http_code}\n' https://app.biffkittz.com/            # 200 (the SPA)
curl -sS https://app.biffkittz.com/health/ready; echo                         # Healthy
curl -sS https://grocery.biffkittz.com/health/ready; echo                     # Healthy (the second instance)
terraform output app_instances                                               # every instance: URL, app, port, database

# The app's own logs (JSON, one event per line). On start look for "Serving HTTPS with certificate ...
# Cloudflare client certificates required: True" (event 4002) and, the first time, the migration messages.
az containerapp logs show --name ca-loginapp-demo --resource-group "$RG" --tail 50

# The direct route is closed. Connecting to the origin IP without Cloudflare's client certificate either times out
# (the ingress IP restrictions) or, if your IP is allowed, gets 403 from the app.
ORIGIN="$(terraform output -raw origin_public_ip)"
curl -sS -m 10 -k -o /dev/null -w '%{http_code}\n' --resolve app.biffkittz.com:8443:"$ORIGIN" https://app.biffkittz.com:8443/
# The health endpoint is the deliberate exception (it answers only if your IP is allowed):
curl -sS -m 10 -k --resolve app.biffkittz.com:8443:"$ORIGIN" https://app.biffkittz.com:8443/health/live; echo
```

Then, in a browser, open <https://app.biffkittz.com>, register, sign out and sign in again. Do the same on
<https://grocery.biffkittz.com>: it has its own accounts and list, so the account from app.biffkittz.com doesn't
exist there. Each registration sends the recipients an email "[grocery app] new registration detected" naming the site
([Registration emails](#registration-emails)).

**Check idempotency.** A plan right after an apply must report **No changes**:

```bash
terraform plan -var-file=environments/demo.tfvars -detailed-exitcode   # exit code 0 = no changes, 2 = drift
```

## Registration emails

Whenever someone registers on either instance, the app emails everyone in `registration_notification_recipients`
(`environments/demo.tfvars`) from notifications@biffkittz.com, through Cloudflare Email Service. The email says which
site, when, and the name the person typed; not their email address. Why Cloudflare, and what it adds to the zone:
[ARCHITECTURE.md](ARCHITECTURE.md#registration-emails-through-cloudflare-email-service).

The apply that first sets recipients (any phase) onboards biffkittz.com for Email Sending, adds each recipient to the
Cloudflare account, creates the app's email token, and gives the apps the token (Key Vault) and settings. Then,
once:

1. **Confirm each recipient.** Cloudflare emails every new recipient a link to verify the address. Follow it;
   until then, emails to that address may be refused. Check who has:

   ```bash
   terraform apply -var-file=environments/demo.tfvars -refresh-only   # reads the current state from Cloudflare
   terraform output registration_notifications                          # recipients: "verified" or "awaiting confirmation"
   ```

2. **Try it.** Register a throwaway account on <https://grocery.biffkittz.com>. The email arrives within a minute;
   the first one may land in spam until you mark it as wanted. The app logs each email (counts only, never addresses):

   ```bash
   az containerapp logs show --name ca-loginapp-demo-grocery --resource-group "$RG" --tail 100 | grep -E '"EventId":11(0[0-9]|10)'
   # 1100 at start: emails are on. 1103: sent. 1104-1110: why one wasn't (see Troubleshooting).
   ```

To change who gets the emails, edit the list and apply; new addresses get Cloudflare's confirmation email. An empty
list turns the emails off and removes the Cloudflare setup, including the DNS records Cloudflare added.

### Rotating the email token

```bash
terraform apply -var-file=environments/demo.tfvars -replace='module.email_sending[0].cloudflare_account_token.this'
# Re-read the secret now instead of waiting for the 30-minute sync, then restart each instance's revision.
IDENTITY="$(az identity show -n id-loginapp-demo-app -g "$RG" --query id -o tsv)"
VAULT="$(terraform output -raw key_vault_uri)"
for APP in ca-loginapp-demo ca-loginapp-demo-grocery; do
  az containerapp secret set --name "$APP" --resource-group "$RG" --secrets \
    "email-api-token=keyvaultref:${VAULT}secrets/Email--ApiToken,identityref:${IDENTITY}"
  az containerapp revision restart --name "$APP" --resource-group "$RG" \
    --revision "$(az containerapp show --name "$APP" --resource-group "$RG" --query properties.latestRevisionName -o tsv)"
done
```

The old token is deleted in the same apply, so an email sent between the apply and the restart fails and is logged
(event 1106); registering itself is unaffected.

## Observability

Prometheus collects the app's metrics (which the app pushes every 15 seconds) and Azure's platform metrics, and
Grafana shows them at https://grafana.biffkittz.com to the GitHub users you list. Design and trade-offs:
[ARCHITECTURE.md](ARCHITECTURE.md#observability-prometheus-and-grafana). It is off until you turn it on, and adds about
$63-77 a month ([cost](ARCHITECTURE.md#cost)).

1. **Create a GitHub OAuth App** (GitHub, **Settings → Developer settings → OAuth Apps → New OAuth App**, under your
   account or an organization):
   - Application name: `Nibbles Grafana`; Homepage URL: `https://grafana.biffkittz.com`;
   - Authorization callback URL: `https://grafana.biffkittz.com/login/github`.

   Note the **Client ID** (an OAuth App's starts with `Ov23li`; `Iv...` is a GitHub App, which isn't what this
   needs), then **Generate a new client secret** and copy it (GitHub shows it once).
2. **Set the values.** In `environments/demo.tfvars`, set `grafana_github_client_id` to the client ID, list the GitHub
   usernames who may sign in in `grafana_github_admins` (and `grafana_github_viewers`, for read-only users), and set
   `observability_enabled = true`. The client secret never goes in the file:
   `TF_VAR_grafana_github_client_secret` in your shell ([Prerequisites](#prerequisites)), and the
   `GRAFANA_GITHUB_CLIENT_SECRET` secret in the `infra-pr` and `demo-plan` environments for CI
   ([docs/CI_CD.md](../../docs/CI_CD.md)).
3. **If CI applies:** re-run `infra/scripts/setup-github-oidc.sh` once first, so CI's deploy identity may assign
   Monitoring Reader (the exporter's role).
4. **Plan and apply.** It adds the storage account and NFS share, two identities, six Key Vault secrets, the Prometheus
   and Grafana Container Apps, the `grafana` DNS record and its Origin Rule, the subnet's `Microsoft.Storage`
   service endpoint, and the app's telemetry settings (a new revision of each instance).
5. **Sign in** at https://grafana.biffkittz.com: Grafana sends you to GitHub, and back. The dashboards are in the
   **Nibbles** folder: *Realtime (SignalR)*, *Service health* and *Azure infrastructure*. The app's metrics appear
   within a minute; Azure's arrive a few minutes late, and the share's used capacity hourly.

Check the parts from the command line:

```bash
curl -s https://grafana.biffkittz.com/api/health                 # {"database": "ok", ...}
az containerapp logs show -n ca-loginapp-demo-prometheus -g "$RG" --container prometheus --tail 50
az containerapp logs show -n ca-loginapp-demo-grafana -g "$RG" --container grafana --tail 50
az containerapp logs show -n ca-loginapp-demo -g "$RG" --tail 200 | grep '"EventId":6000'
# 6000: "Metrics are sent to ca-loginapp-demo-prometheus.internal... every 15 s as site app.biffkittz.com."
```

Prometheus has no public address. To look at it directly, use Grafana's **Explore** (the Prometheus data source).

**Changing a dashboard.** Dashboards are code (`modules/observability/dashboards/*.json`) and can't be saved in
Grafana's UI. Edit a copy there, **Export → Export as JSON**, replace the file, and apply; Grafana restarts with it.

**Changing Prometheus' configuration** (`modules/observability/main.tf`) restarts Prometheus, with about a minute
without new samples while the new replica waits for the old one's database lock. A mistake in the configuration
stops Prometheus from starting: check its logs (above) after such an apply.

**Updating the images.** Dependabot can't see the digests in `observability_images` (variables.tf). Now and then,
look up the current releases and their digests and update the variable:

```bash
docker buildx imagetools inspect quay.io/prometheus/prometheus:v3.16.0 | head -3    # "Digest: sha256:..."
docker buildx imagetools inspect docker.io/grafana/grafana:13.3.1 | head -3
docker buildx imagetools inspect quay.io/webdevops/azure-metrics-exporter:26.10.0 | head -3
docker buildx imagetools inspect mcr.microsoft.com/azurelinux/base/core:3.0.20261027 | head -3
```

Read the release notes first: Grafana's major versions change dashboards' schema, and Prometheus' change flags.
Security releases (Grafana publishes them across its supported versions at once) are worth taking at once.

**Starting Prometheus' history afresh** (say, after a corrupted database): stop it, then delete `prometheus/` on the
share from a temporary container in the environment, as the share accepts NFS mounts from the Container Apps subnet
only. Simpler: set `observability_enabled = false` and apply, which deletes the share (Grafana's users and settings
too), then set it back to `true` and apply again.

## Grocery runs (Temporal Cloud)

Grocery runs ("speak now or forever hold your peace", [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md#grocery-runs))
are Temporal workflows. They run on Temporal Cloud, and each app runs its own Temporal worker in its own process, so
there is nothing to add to Azure: Terraform only gives the apps the namespace and an API key. Until then the app
offers no grocery runs, and says so in the start sheet.

1. **Create the namespace and its key** in Temporal Cloud: a namespace with API key authentication, in AWS
   `us-east-2` (the nearest to Central US), and a namespace-scoped service account with Write permission and an API
   key ([AUTHENTICATION.md](AUTHENTICATION.md#temporal-cloud)). Both sites share it: each polls a task queue of its own
   (`nibbles-main`, `nibbles-grocery`), and its runs have their own workflow ID.
2. **Set the values.** In `environments/demo.tfvars`, set `temporal_namespace` to the namespace's full ID
   (`<namespace>.<account>`). The key goes in `TF_VAR_temporal_api_key` ([Prerequisites](#prerequisites)) and, for CI,
   in the `TEMPORAL_API_KEY` secret of the `infra-pr` and `demo-plan` environments.
3. **Plan and apply.** It adds the Key Vault secret `Temporal--ApiKey` and the settings `Temporal__Address`,
   `Temporal__Namespace`, `Temporal__TaskQueue` and `Temporal__ApiKey` to each app: a new revision of each. The image
   must include grocery runs (this change or later), and its first start creates the runs' tables.
4. **Check.** Each app logs that its worker is polling (event 7000), and the Temporal Cloud UI lists two pollers on each
   task queue during the overlap of a deployment, one afterwards. Then try a run: start one on your phone, with the
   list open on another device signed in as someone else.

```bash
for APP in ca-loginapp-demo ca-loginapp-demo-grocery; do
  az containerapp logs show -n "$APP" -g "$RG" --tail 300 | grep -E '"EventId":(700[0-3]|504[0-7])'
done
# 7000: "Temporal worker polling task queue nibbles-main in namespace ... at ...". 7001: it failed and retries.
```

In Grafana, the *Service health* dashboard's **Grocery runs (Temporal)** row shows the worker's state per site, runs and
how they ended, and the worker's calls, latencies and failures.

**Temporal Cloud's cost** is your Temporal Cloud plan. A run takes roughly ten to twenty Actions: its start, and each
of its signals, timers and activities.

**Deploying a change to the workflow** (`GroceryRunWorkflow`): a new revision replays the runs in flight on the new code.
A change that would make the workflow decide differently for them must be guarded with `Workflow.Patched` (the
class's remarks say how); `GroceryRunReplayTests` fails in CI otherwise. Without such a change, or with no run in
flight, there's nothing to do.

### Rotating the API key

A service account can hold several keys at once, so rotate before the old key expires (Temporal emails 30, 20 and 10
days ahead), without stopping anything:

1. Temporal Cloud: create a second API key for the same service account.
2. Update `TF_VAR_temporal_api_key` (and `TEMPORAL_API_KEY` in CI), and apply: Key Vault gets the new key.
3. Give the apps the new key now rather than at the next 30-minute sync, and restart their revisions; their workers
   reconnect within seconds:

   ```bash
   IDENTITY="$(az identity show -n id-loginapp-demo-app -g "$RG" --query id -o tsv)"
   VAULT="$(terraform output -raw key_vault_uri)"
   for APP in ca-loginapp-demo ca-loginapp-demo-grocery; do
     az containerapp secret set --name "$APP" --resource-group "$RG" --secrets \
       "temporal-api-key=keyvaultref:${VAULT}secrets/Temporal--ApiKey,identityref:${IDENTITY}"
     az containerapp revision restart --name "$APP" --resource-group "$RG" \
       --revision "$(az containerapp show --name "$APP" --resource-group "$RG" --query properties.latestRevisionName -o tsv)"
   done
   ```

4. Once both apps log event 7000 again, delete the old key in Temporal Cloud.

To turn grocery runs off, remove `temporal_namespace` (and the key) and apply. Runs still in flight are abandoned: their
workflows wait for a worker that never comes (cancel them in the Temporal UI), and the app ends them for the phones
once their deadlines pass.

## Day-2 operations

| Task | How |
| --- | --- |
| **Release a new version** | Through CI: merge the app change, approve `registry`, merge the deployment pull request, approve `demo` ([docs/CI_CD.md](../../docs/CI_CD.md)). By hand: phase 2 with the new commit's tag, then set `app_image_tag` to it and plan/apply. The single revision is replaced; readiness probes keep traffic on the old replica until the new one is ready |
| **Roll back** | Set `app_image_tag` back to the previous tag and apply. Schema changes only move forward (see the app's [BUILD_AND_DEPLOY.md](../../docs/BUILD_AND_DEPLOY.md#4-database-migrations-in-azure)) |
| **Rotate the app's database password** | See [Rotating the app's database password](#rotating-the-apps-database-password) |
| **Rotate the PostgreSQL administrator password** | `terraform apply -var-file=environments/demo.tfvars -replace=random_password.postgresql_admin`. Terraform updates the server and Key Vault together |
| **Rotate the JWT signing key** | `terraform apply -var-file=environments/demo.tfvars -replace=random_bytes.jwt_signing_key`. Every session ends and users sign in again |
| **Renew the TLS certificate** | Put the new PFX at `TF_VAR_tls_certificate_pfx_path` and apply. A new certificate version is imported; Container Apps picks it up within 30 minutes and restarts the revision, and the app loads it on start. To switch at once, use the `secret set` and `revision restart` commands from [Rotating the app's database password](#rotating-the-apps-database-password) with the secret `origin-certificate` |
| **Cloudflare publishes new IP ranges** | The next plan shows the ingress IP restrictions and NSG rule changing; apply it. Plan regularly, e.g. weekly, so new edge locations aren't blocked |
| **Cloudflare replaces its origin-pull CA** | The current one expires on 2029-11-01. Replace `app/backend/Infrastructure/Tls/cloudflare-origin-pull-ca.pem` and release a new image before Cloudflare switches |
| **Add another instance of the app** | See [Adding an instance](#adding-an-instance) |
| **Change who gets registration emails** | Edit `registration_notification_recipients` and apply; new addresses confirm Cloudflare's link ([Registration emails](#registration-emails)) |
| **Rotate the email token** | See [Rotating the email token](#rotating-the-email-token) |
| **Scale** | Up only: `app_cpu` / `app_memory`. `app_max_replicas` must stay 1 (validated): the realtime hub broadcasts in-process, without a backplane, so a second replica's users would miss each other's changes |
| **Detect drift** | `terraform plan -detailed-exitcode` (exit code 2 = changes). Fix by re-applying, or by moving the change into code |
| **Upgrade Terraform or providers** | See [DEVELOPMENT.md](DEVELOPMENT.md#8-upgrading-terraform-and-providers) |

### Adding an instance

Every instance is an entry in `additional_app_instances` (`environments/demo.tfvars`). `grocery` is one; another
would look like this:

```hcl
additional_app_instances = {
  grocery = { hostname = "grocery.biffkittz.com", exposed_port = 8444, database_name = "grocery" }
  pantry  = { hostname = "pantry.biffkittz.com", exposed_port = 8445, database_name = "pantry" }
}
```

The host name must be in the zone, and the port and database must be new (validated). Then:

```bash
cd ~/login-app/infra
terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan
terraform apply demo.tfplan
az containerapp job start --name "$JOB" --resource-group "$RG"   # phase 3: lets the role into the new database
```

**The plan.** For one new instance, expect it to add the database, its connection-string secret and the identity's
read access to it, the Container App and the DNS record, and to replace `time_sleep.reader_rbac` (it waits again for
the new role assignment). It changes in place the Origin Rules ruleset (one more rule), the NSG rule (one more port)
and the bootstrap job (its list of databases). Nothing of the existing instance is changed.

The **first** plan with this version of the configuration also reports two moves, `azurerm_container_app.this[0]`
to `this["main"]` and `cloudflare_dns_record.app` to `app["main"]`. Those only rename the existing objects in the state.

**Until the job has run,** the new app can't reach its database (`permission denied for database "pantry"` in its
logs), fails its startup probe and is restarted. It comes up by itself at its next restart after the job succeeds,
within a few minutes; to make it immediate, restart its revision:

```bash
az containerapp revision restart --name ca-loginapp-demo-pantry --resource-group "$RG" \
  --revision "$(az containerapp show --name ca-loginapp-demo-pantry --resource-group "$RG" --query properties.latestRevisionName -o tsv)"
```

**Removing an instance** deletes its Container App, DNS record, Origin Rule, connection string and **database, with
its data**. Back up the database first if the data matters.

### Rotating the app's database password

A PostgreSQL role has one password at a time. New database connections can therefore fail from the moment the job
sets the new password until the app runs with the new connection string. Keep that window short:

```bash
cd ~/login-app/infra
terraform apply -var-file=environments/demo.tfvars -replace=random_password.postgresql_app   # updates both secrets
az containerapp job start --name "$JOB" --resource-group "$RG"                               # phase 3; wait for Succeeded
# Re-read the secret from Key Vault now instead of waiting for the 30-minute sync, then restart the revision. Every
# instance's connection string holds the password, so do this for each: the main instance's secret, then each other
# instance's <instance>-db-connection-string.
IDENTITY="$(az identity show -n id-loginapp-demo-app -g "$RG" --query id -o tsv)"
VAULT="$(terraform output -raw key_vault_uri)"
for pair in ca-loginapp-demo:ConnectionStrings--DefaultConnection ca-loginapp-demo-grocery:grocery-db-connection-string; do
  APP="${pair%%:*}"; SECRET="${pair#*:}"
  az containerapp secret set --name "$APP" --resource-group "$RG" --secrets \
    "db-connection-string=keyvaultref:${VAULT}secrets/${SECRET},identityref:${IDENTITY}"
  az containerapp revision restart --name "$APP" --resource-group "$RG" \
    --revision "$(az containerapp show --name "$APP" --resource-group "$RG" --query properties.latestRevisionName -o tsv)"
done
```

`secret set` writes exactly the reference Terraform declares, so it causes no drift. Without these two commands,
Container Apps picks up the new version within 30 minutes and restarts the revision itself. The JWT signing key and the
administrator password don't have this window: the first only signs users out, and the second is used only by the
job.

### If the app can't bind port 443

The image lets its non-root user bind ports below 1024 through a Linux file capability. If Container Apps doesn't
honor it, the container exits with `SocketException (13): Permission denied` (see `az containerapp logs show`). Fix
it without rebuilding the image:

```hcl
app_target_port = 8443   # environments/demo.tfvars
```

Terraform then sets `ASPNETCORE_URLS=https://+:8443`, the ingress target port and the probes. Cloudflare still
connects to the environment on `app_exposed_port` (8443).

### If Azure refuses IP restrictions on TCP ingress

If the apply fails on `ip_security_restriction`, set `restrict_ingress_to_cloudflare = false`. The client
certificate check still keeps everyone but Cloudflare away from the app; only the health endpoints stay reachable
directly.

## Teardown

```bash
terraform plan -destroy -var-file=environments/demo.tfvars -out=destroy.tfplan
terraform apply destroy.tfplan
```

| Resource | What happens |
| --- | --- |
| Cloudflare DNS records, Origin Rules, and (with recipients) the Email Sending setup | Deleted; both sites stop resolving, and Cloudflare removes the email DNS records it added. The domain's own MXroute records aren't ours and stay |
| PostgreSQL | Deleted. Azure keeps a dropped server's backups for 5 days, restorable only through its "restore a dropped server" procedure. Export data first if you need it (`pg_dump` from a job inside the VNet) |
| Key Vault | Soft-deleted for 7 days, not purged; purge protection forbids purging. Applying again within 7 days recovers it and its secrets, and the name stays reserved meanwhile |
| Log Analytics | Soft-deleted for 14 days; re-creating it with the same name recovers it |
| Container Apps managed resource group | Deleted with the environment |
| Remote state | Stays in the storage account, now empty of resources |

Destroying takes about 15 to 20 minutes.

## Troubleshooting

| Symptom | Likely cause and fix |
| --- | --- |
| Plan fails: `tls_certificate_pfx_path doesn't point to a file` | Set `TF_VAR_tls_certificate_pfx_path` in this shell. `~` is expanded, but quotes around `~` prevent the shell's expansion |
| Plan fails: `temporal_namespace and TF_VAR_temporal_api_key go together` | Set both (grocery runs on) or neither. In CI, the `TEMPORAL_API_KEY` secret is missing from the environment |
| App log event 7001 (`The Temporal worker stopped`) with `Unauthenticated` or `PermissionDenied` | The API key is wrong, expired or deleted, or its service account lacks Write on the namespace, or the namespace doesn't accept API keys. Fix it in Temporal Cloud, then rotate the key in ([Rotating the API key](#rotating-the-api-key)). The list keeps working meanwhile |
| Starting a grocery run says "Grocery runs aren't available right now" | The app can't reach Temporal Cloud: app log event 5046 has the reason, 7001 the worker's. Check the namespace's status page in Temporal Cloud |
| A run was started but never appeared on the phones | No worker is polling the site's task queue (event 7001, or the Temporal UI shows no pollers): the run waits in Temporal and goes on when the worker is back |
| Plan fails with a Cloudflare `401` / `Authentication error` | `CLOUDFLARE_API_TOKEN` is missing, expired or lacks DNS rights on `biffkittz.com` |
| Apply fails on `cloudflare_zone_setting.authenticated_origin_pulls` with `403` | The token lacks **Zone Settings: Edit** ([AUTHENTICATION.md](AUTHENTICATION.md#cloudflare)) |
| Apply fails on `cloudflare_ruleset.origin_port` with `403` | The token lacks **Origin Rules: Edit** ([AUTHENTICATION.md](AUTHENTICATION.md#cloudflare)) |
| Apply fails on `cloudflare_ruleset.origin_port`: a ruleset for the phase already exists | Origin Rules were created by hand in the dashboard. Import the ruleset (comment above the resource in `main.tf`) and add those rules to it |
| Container App fails: `exposedPort ... Invalid value: 443: exposedPort already in use` | `app_exposed_port` is 443 or 80, which the environment's HTTP ingress holds. Keep the default 8443 |
| `MissingSubscriptionRegistration` | Your identity can't register resource providers. Run `az provider register --namespace Microsoft.App` (and the others in `providers.tf`), or get Contributor |
| `AuthorizationFailed` creating `azurerm_role_assignment` | You lack Owner or Role Based Access Control Administrator on the subscription |
| `403 Forbidden` creating a Key Vault secret or certificate | Role propagation took longer than 90 s. Apply again, or raise `rbac_propagation_wait` |
| Certificate import fails: password or format error | Wrong `TF_VAR_tls_certificate_pfx_password`, or an OpenSSL 3 PFX that Key Vault doesn't accept. Re-create it with `openssl pkcs12 -export -legacy ...` |
| Cloudflare **526 Invalid SSL certificate** | The app's certificate doesn't cover `app.biffkittz.com`, or isn't a Cloudflare Origin CA / publicly trusted certificate |
| Cloudflare **525 SSL handshake failed** | Kestrel rejected Cloudflare's client certificate (app log event 4001), e.g. after Cloudflare changed its origin-pull CA. Update the embedded CA (see Day-2 operations) |
| Cloudflare **521 / 522** | Cloudflare can't connect: the app isn't deployed or isn't running (phase 4, app logs; `az containerapp logs show --name ca-loginapp-demo-<key>` for an additional instance), Cloudflare's IP ranges changed (plan and apply), or the Origin Rule is missing or names another port than the instance's exposed port |
| **403 Forbidden** on every page, app log event 4000 | Requests arrive without Cloudflare's client certificate: Authenticated Origin Pulls is off in Cloudflare (SSL/TLS → Origin Server), or the record isn't proxied |
| App exits at start: `OriginTls:CertificatePfxBase64 ...` | The certificate from Key Vault can't be used: it is expired, has no private key or isn't readable. Re-import the PFX and apply |
| App logs: `password authentication failed for user "loginapp_app"` | The bootstrap job hasn't run since the password was created or rotated. Run phase 3 |
| A new instance's logs: `permission denied for database "<name>"`, or it keeps restarting | The bootstrap job hasn't run since the instance was added. Run phase 3 ([Adding an instance](#adding-an-instance)) |
| Signed in on one instance, but the other asks to sign in | Expected: each instance has its own accounts, and a session only counts on the instance that started it |
| Readiness probe failing; liveness fine | The app can't reach PostgreSQL: check the job ran, then the NSG on `snet-psql` and the private DNS link |
| Bootstrap job fails: `unable to fetch secret ... using Managed identity` | Role propagation (start it again in a minute), or the platform issue in [microsoft/azure-container-apps#1804](https://github.com/microsoft/azure-container-apps/issues/1804) |
| Bootstrap job fails: `password authentication failed for user "loginapp_admin"` | The administrator password in Key Vault and on the server differ, e.g. after a manual change. Run `terraform apply -replace=random_password.postgresql_admin` |
| Bootstrap job fails: `certificate verify failed` | The CA bundle path changed in a new image tag; check `PGSSLROOTCERT` against the image |
| Cloudflare can't connect and the ingress logs show refused connections | The ingress IP restrictions blocked Cloudflare, e.g. after `container_apps_transitional_ingress_cidrs` was emptied too early (issue #1666) |
| Container exits with `SocketException (13): Permission denied` | See [If the app can't bind port 443](#if-the-app-cant-bind-port-443) |
| Plan wants to destroy the Container Apps | `app_image_tag` was removed from `environments/demo.tfvars` |
| Plan wants to destroy and re-create `ca-loginapp-demo` or the `app` DNS record | The `moved` blocks were removed from `main.tf` before this state was applied with them. Put them back |
| Plan fails: "The Cloudflare account offers none of the permission groups in preferred_permission_groups" | The account has no Email Sending yet, or Cloudflare names the permission differently. The message lists the email sending groups it found; put the right one in `cloudflare_email_sending_permission_groups` |
| Plan or apply fails on `data.cloudflare_account_api_token_permission_groups_list` or `module.email_sending[0].cloudflare_account_token.this` with `403` / `Authentication error` | The deploying token lacks **Account API Tokens: Edit** ([AUTHENTICATION.md](AUTHENTICATION.md#cloudflare)) |
| Apply fails on `module.email_sending[0].cloudflare_email_sending_subdomain.this` with `401 Unauthorized` and code 2036 | The deploying token lacks the **Account** permission **Email Sending: Edit** (Read isn't enough, and there's no Zone one; [AUTHENTICATION.md](AUTHENTICATION.md#cloudflare)). Add it and apply again. Resources that don't wait for the domain, the apps included, may already be applied: until it is onboarded, registering works but each email fails (log 1106) |
| Apply fails on `module.email_sending[0].cloudflare_email_sending_subdomain.this` with `403`, or still fails with the permission in place | The account may not be entitled to Email Sending (a public beta). Open **Compute → Email Service → Email Sending** in the dashboard to see whether it's available; a Workers Paid plan may be needed |
| Apply fails on `module.email_sending[0].cloudflare_email_routing_address.recipients[...]`: the address already exists | It is already a destination address of the account (added by hand). Import it: `terraform import -var-file=environments/demo.tfvars 'module.email_sending[0].cloudflare_email_routing_address.recipients["<address>"]' '<account id>/<destination address id>'` |
| No registration email arrives | Look in spam first. Then the app's log: 1101/1102 at start mean the emails are off (no recipients, or the email settings didn't arrive); 1106 gives Cloudflare's reason. `terraform output registration_notifications` should show the recipient as verified |
| App log 1106: `Cloudflare answered 403 (10102 ...)` or `(10105 ...)` | The token can't send (10102: wrong permission; rotate it) or the account isn't entitled to send to that address (10105: confirm the recipient, or Workers Paid for other addresses) |
| App log 1106: `Cloudflare answered 401` | The token was deleted or replaced: apply, then restart the revisions ([Rotating the email token](#rotating-the-email-token)) |
| Grafana: GitHub says `The redirect_uri is not associated with this application` | The OAuth App's callback URL must be exactly `https://grafana.biffkittz.com/login/github` |
| Grafana: back from GitHub, the sign-in is refused | Your GitHub username isn't in `grafana_github_admins` or `grafana_github_viewers`, spelled as GitHub spells it. Add it and apply |
| Grafana: `Login failed: Failed to get token from provider`; its log says `incorrect_client_credentials` | The client secret in Key Vault isn't the OAuth App's (for example, still a previous app's). Set the OAuth App's secret in `TF_VAR_grafana_github_client_secret` and in CI's `GRAFANA_GITHUB_CLIENT_SECRET` (`infra-pr`, `demo-plan`), apply, then let Grafana re-read it: `az containerapp secret set -n ca-loginapp-demo-grafana -g "$RG" --secrets "github-client-secret=keyvaultref:<vault URI>secrets/grafana-github-client-secret,identityref:<id-loginapp-demo-grafana resource ID>"` and `az containerapp revision restart` (or wait up to 30 minutes) |
| Grafana's log: `error getting user teams` | GitHub refused to list your teams: the client ID is a GitHub App's (`Iv...`) rather than an OAuth App's (`Ov23li...`). Create an OAuth App ([AUTHENTICATION.md](AUTHENTICATION.md#grafanas-github-sign-in)) and use its ID and secret |
| Apply fails on `cloudflare_dns_record.*`: `DNS record comment exceeds the maximum length of 100 characters` (9313) | Cloudflare limits comments to 100 characters below Enterprise; shorten the record's `comment` (the tests check the ones in `main.tf`) |
| Grafana's dashboards show **No data** for the app | The app isn't pushing: its log should show event 6000 at start. Prometheus' log shows refused pushes (`401`); after rotating Prometheus' passwords, restart the app's revisions so they re-read `Telemetry--OtlpPassword` |
| Prometheus' log repeats `Waiting for the previous Prometheus replica to release /prometheus/lock` | An old revision still runs. It goes once the new one is ready (a minute); if not, deactivate it: `az containerapp revision list -n ca-loginapp-demo-prometheus -g "$RG" -o table`, then `az containerapp revision deactivate`. After ten minutes Prometheus starts anyway and refuses to run if the lock is really held. With `flock: Bad file descriptor` before each line, the start script is testing the lock with an exclusive lock, which can't work on NFS from busybox's read-only descriptor: it must use `flock -s` |
| Prometheus or Grafana won't start: `mount.nfs: access denied by server` | The share admits only the Container Apps subnet: check its `Microsoft.Storage` service endpoint and the storage account's network rules, and that secure transfer is off (Terraform sets all three) |
| The *Azure infrastructure* dashboard is empty | The exporter can't read Azure Monitor yet: its Monitoring Reader role takes a few minutes to take effect after the first apply. Its log (`--container azure-metrics-exporter`) shows `AuthorizationFailed` until then |
| `Error acquiring the state lock` | Someone else is running Terraform, or a crashed run left the lock. Wait, or `terraform force-unlock <LOCK_ID>` once you're sure |
| A plan right after an apply isn't empty | Something drifts. Note the attribute and fix it in code (for example a value Azure sets that the configuration should state explicitly). Please don't silence it with `ignore_changes` without a comment explaining why |
