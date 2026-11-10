# container-app

The external Container Apps environment, the Login App (one Container App per entry of `apps`) and the database bootstrap job.

**Environment.**
- Workload profiles type with the Consumption profile, in the VNet (required for external TCP ingress).
- External: a public IP (`static_ip_address`), which Cloudflare's DNS record points to.
- Logs go to Log Analytics.

**Apps** (created only when `deploy_app` is true).
- Single revision mode, with a user-assigned identity for the image pull and the Key Vault references.
- Secrets come from Key Vault; the versionless URIs follow new secret versions automatically.
- **TCP ingress** on `app_exposed_port`, passed through to `app_target_port` without decryption, so Kestrel
  terminates TLS with the certificate from Key Vault (`OriginTls__CertificatePfxBase64`).
- IP restrictions refuse every source not listed in `ingress_allowed_cidrs` (none listed: everyone may connect).
- Startup, liveness and readiness probes over HTTPS.
- `ASPNETCORE_URLS=https://+:<app_target_port>`, so Kestrel listens for HTTPS only.

**Database bootstrap job** (`db_bootstrap`). A manually triggered job that runs
[`scripts/db-bootstrap.sh`](scripts/db-bootstrap.sh) with Microsoft's Azure Linux PostgreSQL image. The script
creates or updates the app's login role, gives it only connect rights and the right to create objects in schema
`public`, and revokes `PUBLIC`'s access to the database. It runs under its own identity, the only one that can read the
administrator password, and connects with `sslmode=verify-full`. Start it with
`az containerapp job start --name <job> --resource-group <group>`.

<!-- BEGIN_TF_DOCS -->
### Requirements

| Name | Version |
| ---- | ------- |
| <a name="requirement_terraform"></a> [terraform](#requirement\_terraform) | >= 1.17.1 |
| <a name="requirement_azurerm"></a> [azurerm](#requirement\_azurerm) | >= 5.12 |

### Providers

| Name | Version |
| ---- | ------- |
| <a name="provider_azurerm"></a> [azurerm](#provider\_azurerm) | >= 5.12 |

### Resources

| Name | Type |
| ---- | ---- |
| [azurerm_container_app.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app) | resource |
| [azurerm_container_app_environment.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app_environment) | resource |
| [azurerm_container_app_job.db_bootstrap](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app_job) | resource |

### Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_app_cpu"></a> [app\_cpu](#input\_app\_cpu) | vCPUs per replica. | `number` | n/a | yes |
| <a name="input_app_identity_id"></a> [app\_identity\_id](#input\_app\_identity\_id) | User-assigned managed identity of the app: pulls the image (AcrPull) and resolves the Key Vault secrets. | `string` | n/a | yes |
| <a name="input_app_max_replicas"></a> [app\_max\_replicas](#input\_app\_max\_replicas) | Maximum number of replicas. | `number` | n/a | yes |
| <a name="input_app_memory"></a> [app\_memory](#input\_app\_memory) | Memory per replica, e.g. "1Gi". Consumption profiles require twice the vCPU count in GiB. | `string` | n/a | yes |
| <a name="input_app_min_replicas"></a> [app\_min\_replicas](#input\_app\_min\_replicas) | Minimum number of replicas. | `number` | n/a | yes |
| <a name="input_app_target_port"></a> [app\_target\_port](#input\_app\_target\_port) | Port the container listens on for HTTPS. The ingress, health probes and ASPNETCORE\_URLS all follow it. | `number` | n/a | yes |
| <a name="input_apps"></a> [apps](#input\_apps) | The app's instances, keyed by a short, stable name: each becomes a Container App running app\_image with the shared settings below. name: the Container App's name (2-32 lowercase letters, digits and hyphens; also the first label of its FQDN). exposed\_port: its public TCP port on the environment's IP, unique across the environment. secrets: its Container App secrets, as a map of secret name (lowercase, digits, hyphens) to a versionless Key Vault secret URI; app\_secret\_environment\_variables refers to them by name. environment\_variables: plain variables of its own, added to (and overriding) app\_environment\_variables. | <pre>map(object({<br/>    name                  = string<br/>    exposed_port          = number<br/>    secrets               = map(string)<br/>    environment_variables = map(string)<br/>  }))</pre> | n/a | yes |
| <a name="input_db_bootstrap"></a> [db\_bootstrap](#input\_db\_bootstrap) | Manually triggered job that creates the app's PostgreSQL role and lets it into app\_databases (every instance's database). database\_name is the one the job connects to first. Secrets are versionless Key Vault secret URIs. | <pre>object({<br/>    job_name                  = string<br/>    image                     = string<br/>    identity_id               = string<br/>    postgresql_host           = string<br/>    database_name             = string<br/>    app_databases             = list(string)<br/>    administrator_login       = string<br/>    administrator_password_id = string<br/>    app_role                  = string<br/>    app_password_id           = string<br/>  })</pre> | n/a | yes |
| <a name="input_deploy_app"></a> [deploy\_app](#input\_deploy\_app) | Whether to create the Container Apps. False until an image exists in the registry. | `bool` | n/a | yes |
| <a name="input_environment_name"></a> [environment\_name](#input\_environment\_name) | Name of the Container Apps environment. | `string` | n/a | yes |
| <a name="input_infrastructure_resource_group_name"></a> [infrastructure\_resource\_group\_name](#input\_infrastructure\_resource\_group\_name) | Name of the platform-managed resource group that Container Apps creates for the environment's load balancer and public IP. Must not exist yet. | `string` | n/a | yes |
| <a name="input_infrastructure_subnet_id"></a> [infrastructure\_subnet\_id](#input\_infrastructure\_subnet\_id) | Subnet delegated to Microsoft.App/environments (at least /27). | `string` | n/a | yes |
| <a name="input_ingress_allowed_cidrs"></a> [ingress\_allowed\_cidrs](#input\_ingress\_allowed\_cidrs) | IPv4 ranges allowed by the app's ingress IP restrictions, keyed by rule name. Every other source is refused; an empty map allows everyone. | `map(string)` | n/a | yes |
| <a name="input_location"></a> [location](#input\_location) | Azure region of the environment, app and job. | `string` | n/a | yes |
| <a name="input_log_analytics_workspace_id"></a> [log\_analytics\_workspace\_id](#input\_log\_analytics\_workspace\_id) | Log Analytics workspace that receives console and system logs. | `string` | n/a | yes |
| <a name="input_registry_server"></a> [registry\_server](#input\_registry\_server) | Login server of the registry that holds app\_image. | `string` | n/a | yes |
| <a name="input_resource_group_name"></a> [resource\_group\_name](#input\_resource\_group\_name) | Resource group that holds the environment, app and job. | `string` | n/a | yes |
| <a name="input_tags"></a> [tags](#input\_tags) | Tags applied to every taggable resource in this module. Container Apps copies the environment's tags to its managed infrastructure resource group. | `map(string)` | n/a | yes |
| <a name="input_app_environment_variables"></a> [app\_environment\_variables](#input\_app\_environment\_variables) | Plain (non-secret) environment variables for every app's container. | `map(string)` | `{}` | no |
| <a name="input_app_image"></a> [app\_image](#input\_app\_image) | Full image reference, e.g. crexample.azurecr.io/login-app:1a2b3c4, run by every app. Ignored while deploy\_app is false. | `string` | `null` | no |
| <a name="input_app_secret_environment_variables"></a> [app\_secret\_environment\_variables](#input\_app\_secret\_environment\_variables) | Environment variables that take their value from each app's secrets, the same for every app: map of variable name to secret name. Every app's secrets must define these names. | `map(string)` | `{}` | no |
| <a name="input_liveness_probe_path"></a> [liveness\_probe\_path](#input\_liveness\_probe\_path) | Path of the liveness and startup probes. It must not depend on the database. | `string` | `"/health/live"` | no |
| <a name="input_readiness_probe_path"></a> [readiness\_probe\_path](#input\_readiness\_probe\_path) | Path of the readiness probe. It includes the database check. | `string` | `"/health/ready"` | no |

### Outputs

| Name | Description |
| ---- | ----------- |
| <a name="output_app_fqdns"></a> [app\_fqdns](#output\_app\_fqdns) | Each app's platform FQDN (<app name>.<default domain>), keyed like apps and known before the apps exist. With TCP ingress it answers on the app's exposed port; users reach the apps through Cloudflare instead. |
| <a name="output_app_ids"></a> [app\_ids](#output\_app\_ids) | Resource IDs of the Container Apps, keyed like apps; empty while deploy\_app is false. |
| <a name="output_app_latest_revision_names"></a> [app\_latest\_revision\_names](#output\_app\_latest\_revision\_names) | Names of the apps' latest revisions, keyed like apps; empty while deploy\_app is false. |
| <a name="output_app_names"></a> [app\_names](#output\_app\_names) | Each app's Container App name, keyed like apps, whether or not it has been deployed yet. |
| <a name="output_db_bootstrap_job_name"></a> [db\_bootstrap\_job\_name](#output\_db\_bootstrap\_job\_name) | Name of the database bootstrap job. |
| <a name="output_default_domain"></a> [default\_domain](#output\_default\_domain) | The environment's default domain. Apps are served at <app name>.<default domain>. |
| <a name="output_environment_id"></a> [environment\_id](#output\_environment\_id) | Resource ID of the Container Apps environment. |
| <a name="output_environment_name"></a> [environment\_name](#output\_environment\_name) | Name of the Container Apps environment. |
| <a name="output_static_ip_address"></a> [static\_ip\_address](#output\_static\_ip\_address) | Public IP of the environment, which Cloudflare's DNS record points to. |
<!-- END_TF_DOCS -->
