# Deployment

How to deploy the Login App's infrastructure from nothing, verify it, operate it, and tear it down. Commands run in
WSL from `infra/` unless they say otherwise.

- [How a first deployment works](#how-a-first-deployment-works)
- [Prerequisites](#prerequisites)
- [Phase 1: the platform](#phase-1-the-platform)
- [Phase 2: build and push the image](#phase-2-build-and-push-the-image)
- [Phase 3: create the app's database role](#phase-3-create-the-apps-database-role)
- [Phase 4: deploy the app](#phase-4-deploy-the-app)
- [Migrating from the Application Gateway version](#migrating-from-the-application-gateway-version)
- [Verify](#verify)
- [Day-2 operations](#day-2-operations)
- [Teardown](#teardown)
- [Troubleshooting](#troubleshooting)

## How a first deployment works

The Container App can't be created before its image is in the registry: its first revision would fail to pull. It
also needs its database role before it starts. The first deployment therefore has four phases, and afterwards every
release is just phase 2 plus phase 4.

| Phase | You run | Creates | Time |
| --- | --- | --- | --- |
| 1. Platform | `terraform apply` (no `app_image_tag`) | Everything except the Container App: network, NSGs, Log Analytics, identities, registry, Key Vault + secrets + certificate, PostgreSQL + database, Container Apps environment (public IP), bootstrap job, Cloudflare record and Authenticated Origin Pulls | 15–25 min |
| 2. Image | `az acr build` | `login-app:<tag>` in the registry | 5–10 min |
| 3. Database role | `az containerapp job start` | Role `loginapp_app` with its password from Key Vault | 1–2 min |
| 4. App | `terraform apply` with `app_image_tag` | The Container App; the app migrates its schema on first start | 3–5 min |

## Prerequisites

- [ ] Tools installed in WSL: Terraform 1.17+, Azure CLI, Git ([DEVELOPMENT.md](DEVELOPMENT.md#1-install-the-tools)).
- [ ] Azure: signed in, subscription selected, and **Owner** (or Contributor + Role Based Access Control
      Administrator) on it ([AUTHENTICATION.md](AUTHENTICATION.md#azure)).
- [ ] The state storage account (`demonstration` in `rg-terraform-state`) exists and you can read and write it
      ([AUTHENTICATION.md](AUTHENTICATION.md#remote-state)).
- [ ] A Cloudflare API token that can edit DNS, **zone settings and Origin Rules** in `biffkittz.com`
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
```

## Phase 1: the platform

```bash
terraform init                                                     # once per clone; connects to the state backend
terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan
```

Review the plan before applying. On the first run it should show about **50 to add, 0 to change, 0 to destroy**.
Look for:

- `module.container_apps.azurerm_container_app.this` is **absent**, because `app_image_tag` isn't set yet;
- `module.container_apps.azurerm_container_app_environment.this` has `internal_load_balancer_enabled = false`;
- `cloudflare_zone_setting.authenticated_origin_pulls` sets `tls_client_auth` to `on`;
- `cloudflare_ruleset.origin_port` routes `app.biffkittz.com` to port 8443;
- `cloudflare_dns_record.app` is proxied and points at the environment's IP (known after apply).

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

Expected output: `Done. 'loginapp_app' can connect to 'loginapp' and create its schema objects.` (Console logs reach
Log Analytics a few minutes late.)

## Phase 4: deploy the app

Set the image tag in `environments/demo.tfvars` and commit it, so the deployed version is recorded:

```hcl
app_image_tag = "1a2b3c4"   # the TAG from phase 2
```

```bash
cd ~/login-app/infra
terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan   # expect: 1 to add (the Container App)
terraform apply demo.tfplan
```

Keep `app_image_tag` set from now on. Removing it again plans to **destroy** the Container App.

## Migrating from the Application Gateway version

An earlier version of this configuration put Application Gateway in front of an internal environment. Moving an
existing deployment to the current one is a single apply, with a few minutes of downtime.

1. **Build an image with the TLS support** (the app code that reads `OriginTls`), as in phase 2, with a new tag.
2. **Set that tag** in `environments/demo.tfvars` (`app_image_tag`).
3. **Make sure the Cloudflare token can edit zone settings and Origin Rules**
   ([AUTHENTICATION.md](AUTHENTICATION.md#cloudflare)).
4. **Plan and read it carefully:**

   ```bash
   terraform plan -var-file=environments/demo.tfvars -out=demo.tfplan
   ```

   | Expect | Why |
   | --- | --- |
   | **Destroy:** Application Gateway, its public IP and diagnostic setting, `id-loginapp-demo-agw`, `snet-appgw` with its NSG and rules, the environment's private DNS zone with its link and records | No longer needed |
   | **Replace (`-/+`):** the Container Apps environment, the Container App and the bootstrap job | `internal_load_balancer_enabled` can't change in place |
   | **Replace:** the certificate's role assignment | It now goes to the app's identity instead of the gateway's |
   | **Update:** `cloudflare_dns_record.app`, the Container Apps NSG rules | New IP; new ingress rule |
   | **Create:** `cloudflare_zone_setting.authenticated_origin_pulls` | Cloudflare's client certificate |
   | **No change:** PostgreSQL, Key Vault and its secrets, the registry | Your data and secrets stay |

   If the plan wants to replace PostgreSQL, the Key Vault or the registry, **stop**: their names changed, usually
   because `name_suffix` was removed from `demo.tfvars`.

5. **Apply.** `terraform apply demo.tfplan`. The site is down from when the old environment is deleted until the new
   app is ready, typically 10 to 20 minutes. The database role from phase 3 still exists, so the job doesn't need to
   run again.

If the apply fails with an error that the managed resource group `rg-loginapp-demo-cae-managed` already exists, Azure
is still deleting the old one. Wait a few minutes and apply again.

## Verify

```bash
curl -sS -o /dev/null -w '%{http_code}\n' https://app.biffkittz.com/            # 200 (the SPA)
curl -sS https://app.biffkittz.com/health/ready; echo                         # Healthy

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

Then, in a browser, open <https://app.biffkittz.com>, register, sign out and sign in again.

**Check idempotency.** A plan right after an apply must report **No changes**:

```bash
terraform plan -var-file=environments/demo.tfvars -detailed-exitcode   # exit code 0 = no changes, 2 = drift
```

## Day-2 operations

| Task | How |
| --- | --- |
| **Release a new version** | Phase 2 with the new commit's tag, then set `app_image_tag` to it and plan/apply. The single revision is replaced; readiness probes keep traffic on the old replica until the new one is ready |
| **Roll back** | Set `app_image_tag` back to the previous tag and apply. Schema changes only move forward (see the app's [BUILD_AND_DEPLOY.md](../../docs/BUILD_AND_DEPLOY.md#4-database-migrations-in-azure)) |
| **Rotate the app's database password** | See [Rotating the app's database password](#rotating-the-apps-database-password) |
| **Rotate the PostgreSQL administrator password** | `terraform apply -var-file=environments/demo.tfvars -replace=random_password.postgresql_admin`. Terraform updates the server and Key Vault together |
| **Rotate the JWT signing key** | `terraform apply -var-file=environments/demo.tfvars -replace=random_bytes.jwt_signing_key`. Every session ends and users sign in again |
| **Renew the TLS certificate** | Put the new PFX at `TF_VAR_tls_certificate_pfx_path` and apply. A new certificate version is imported; Container Apps picks it up within 30 minutes and restarts the revision, and the app loads it on start. To switch at once, use the `secret set` and `revision restart` commands from [Rotating the app's database password](#rotating-the-apps-database-password) with the secret `origin-certificate` |
| **Cloudflare publishes new IP ranges** | The next plan shows the ingress IP restrictions and NSG rule changing; apply it. Plan regularly, e.g. weekly, so new edge locations aren't blocked |
| **Cloudflare replaces its origin-pull CA** | The current one expires on 2029-11-01. Replace `app/backend/Infrastructure/Tls/cloudflare-origin-pull-ca.pem` and release a new image before Cloudflare switches |
| **Scale** | Change `app_min_replicas` / `app_max_replicas`; the app is stateless |
| **Detect drift** | `terraform plan -detailed-exitcode` (exit code 2 = changes). Fix by re-applying, or by moving the change into code |
| **Upgrade Terraform or providers** | See [DEVELOPMENT.md](DEVELOPMENT.md#8-upgrading-terraform-and-providers) |

### Rotating the app's database password

A PostgreSQL role has one password at a time. New database connections can therefore fail from the moment the job
sets the new password until the app runs with the new connection string. Keep that window short:

```bash
cd ~/login-app/infra
terraform apply -var-file=environments/demo.tfvars -replace=random_password.postgresql_app   # updates both secrets
az containerapp job start --name "$JOB" --resource-group "$RG"                               # phase 3; wait for Succeeded
# Re-read the secret from Key Vault now instead of waiting for the 30-minute sync, then restart the revision.
az containerapp secret set --name ca-loginapp-demo --resource-group "$RG" --secrets \
  "db-connection-string=keyvaultref:$(terraform output -raw key_vault_uri)secrets/ConnectionStrings--DefaultConnection,identityref:$(az identity show -n id-loginapp-demo-app -g "$RG" --query id -o tsv)"
az containerapp revision restart --name ca-loginapp-demo --resource-group "$RG" \
  --revision "$(az containerapp show --name ca-loginapp-demo --resource-group "$RG" --query properties.latestRevisionName -o tsv)"
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
| Cloudflare `app` record | Deleted; `app.biffkittz.com` stops resolving |
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
| Cloudflare **521 / 522** | Cloudflare can't connect: the app isn't deployed or isn't running (phase 4, app logs), Cloudflare's IP ranges changed (plan and apply), or the Origin Rule is missing or names another port than `app_exposed_port` |
| **403 Forbidden** on every page, app log event 4000 | Requests arrive without Cloudflare's client certificate: Authenticated Origin Pulls is off in Cloudflare (SSL/TLS → Origin Server), or the record isn't proxied |
| App exits at start: `OriginTls:CertificatePfxBase64 ...` | The certificate from Key Vault can't be used: it is expired, has no private key or isn't readable. Re-import the PFX and apply |
| App logs: `password authentication failed for user "loginapp_app"` | The bootstrap job hasn't run since the password was created or rotated. Run phase 3 |
| Readiness probe failing; liveness fine | The app can't reach PostgreSQL: check the job ran, then the NSG on `snet-psql` and the private DNS link |
| Bootstrap job fails: `unable to fetch secret ... using Managed identity` | Role propagation (start it again in a minute), or the platform issue in [microsoft/azure-container-apps#1804](https://github.com/microsoft/azure-container-apps/issues/1804) |
| Bootstrap job fails: `password authentication failed for user "loginapp_admin"` | The administrator password in Key Vault and on the server differ, e.g. after a manual change. Run `terraform apply -replace=random_password.postgresql_admin` |
| Bootstrap job fails: `certificate verify failed` | The CA bundle path changed in a new image tag; check `PGSSLROOTCERT` against the image |
| Cloudflare can't connect and the ingress logs show refused connections | The ingress IP restrictions blocked Cloudflare, e.g. after `container_apps_transitional_ingress_cidrs` was emptied too early (issue #1666) |
| Container exits with `SocketException (13): Permission denied` | See [If the app can't bind port 443](#if-the-app-cant-bind-port-443) |
| Plan wants to destroy the Container App | `app_image_tag` was removed from `environments/demo.tfvars` |
| `Error acquiring the state lock` | Someone else is running Terraform, or a crashed run left the lock. Wait, or `terraform force-unlock <LOCK_ID>` once you're sure |
| A plan right after an apply isn't empty | Something drifts. Note the attribute and fix it in code (for example a value Azure sets that the configuration should state explicitly). Please don't silence it with `ignore_changes` without a comment explaining why |
