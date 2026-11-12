# observability

Prometheus and Grafana as Container Apps in the app's own environment, with their data on an NFS share.

- **Prometheus** (internal ingress only) receives the app's metrics, which the app pushes over OTLP
  (`/api/v1/otlp/v1/metrics`, HTTP Basic authentication), and scrapes the Azure Monitor exporter beside it for platform
  metrics: the Container Apps' replicas, restarts, CPU and memory, the PostgreSQL server, and the share's used
  capacity. The exporter signs in as Prometheus' user-assigned identity, which has Monitoring Reader on the app's
  resource group.
- **Grafana** is public at `grafana_hostname` through Cloudflare, like the app's instances: a TCP ingress port of its
  own, Cloudflare-only IP restrictions, and TLS in the container with the same origin certificate. GitHub is the only
  way in, for the users in `grafana_github_admins` and `grafana_github_viewers`. Its data source and the dashboards
  in `dashboards/` are provisioned and read-only in the UI.
- **Storage**: a Premium FileStorage account with one NFS share, which admits only the Container Apps subnet.
  Prometheus keeps its database in `prometheus/`, Grafana its SQLite database in `grafana/`.

Container Apps can't mount Azure Disks. Of what it can mount, NFS is the POSIX file system Prometheus' database
needs; SMB is not. Two things follow:

- **Secure transfer is off on the storage account.** Container Apps can't encrypt NFS in transit, and Azure refuses
  the mount while "secure transfer required" is on. The account accepts no keys and only the Container Apps subnet.
- **One writer at a time.** A new revision starts before the old one stops. Prometheus' start script waits for the old
  replica to release its database lock, and its probes watch the exporter's port so that the waiting replica still
  counts as ready (otherwise neither would ever give way).

Configuration reaches the containers as environment variables, which an init container (Azure Linux) writes to disk:
`prometheus.yml` and the web configuration for Prometheus, the provisioning files and the PEM certificate for Grafana.
A change to either redeploys that app. To change a dashboard, edit its JSON here (or export it from Grafana) and
apply.

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
| [azurerm_container_app.grafana](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app) | resource |
| [azurerm_container_app.prometheus](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app) | resource |
| [azurerm_container_app_environment_storage.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app_environment_storage) | resource |
| [azurerm_role_assignment.metrics_reader](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_storage_account.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_account) | resource |
| [azurerm_storage_share.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/storage_share) | resource |

### Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_container_app_environment_default_domain"></a> [container\_app\_environment\_default\_domain](#input\_container\_app\_environment\_default\_domain) | The environment's default domain. Prometheus' internal address is <prometheus\_app\_name>.internal.<default domain>. | `string` | n/a | yes |
| <a name="input_container_app_environment_id"></a> [container\_app\_environment\_id](#input\_container\_app\_environment\_id) | Container Apps environment that runs Prometheus and Grafana (the app's own environment). | `string` | n/a | yes |
| <a name="input_container_apps_subnet_id"></a> [container\_apps\_subnet\_id](#input\_container\_apps\_subnet\_id) | The environment's subnet, which must have the Microsoft.Storage service endpoint: the storage account admits NFS mounts from this subnet only. | `string` | n/a | yes |
| <a name="input_grafana_app_name"></a> [grafana\_app\_name](#input\_grafana\_app\_name) | Name of Grafana's Container App. | `string` | n/a | yes |
| <a name="input_grafana_exposed_port"></a> [grafana\_exposed\_port](#input\_grafana\_exposed\_port) | Public TCP port of the environment that Cloudflare's Origin Rule connects to for Grafana. Unique among the environment's TCP apps. | `number` | n/a | yes |
| <a name="input_grafana_github_admins"></a> [grafana\_github\_admins](#input\_grafana\_github\_admins) | GitHub usernames that become Grafana administrators. Anyone not in this list or grafana\_github\_viewers is refused. | `list(string)` | n/a | yes |
| <a name="input_grafana_github_client_id"></a> [grafana\_github\_client\_id](#input\_grafana\_github\_client\_id) | Client ID of the GitHub OAuth App that Grafana signs users in with. | `string` | n/a | yes |
| <a name="input_grafana_hostname"></a> [grafana\_hostname](#input\_grafana\_hostname) | Public host name of Grafana (Cloudflare proxies it), e.g. grafana.biffkittz.com. GitHub sends users back to https://<host name>/login/github. | `string` | n/a | yes |
| <a name="input_grafana_identity_id"></a> [grafana\_identity\_id](#input\_grafana\_identity\_id) | Resource ID of Grafana's user-assigned identity, which reads its Key Vault secrets and the origin certificate. | `string` | n/a | yes |
| <a name="input_grafana_secret_ids"></a> [grafana\_secret\_ids](#input\_grafana\_secret\_ids) | Versionless Key Vault secret IDs for Grafana: github\_client\_secret, secret\_key (encrypts what Grafana stores), prometheus\_password (Grafana's Prometheus user), and origin\_certificate (the certificate's secret, a Base64 PFX without password). | <pre>object({<br/>    github_client_secret = string<br/>    secret_key           = string<br/>    prometheus_password  = string<br/>    origin_certificate   = string<br/>  })</pre> | n/a | yes |
| <a name="input_images"></a> [images](#input\_images) | Images, each pinned by digest: prometheus, azure\_metrics\_exporter, grafana, and init (Azure Linux with bash and openssl, which prepares files before Prometheus and Grafana start). | <pre>object({<br/>    prometheus             = string<br/>    azure_metrics_exporter = string<br/>    grafana                = string<br/>    init                   = string<br/>  })</pre> | n/a | yes |
| <a name="input_location"></a> [location](#input\_location) | Azure region of the storage account. | `string` | n/a | yes |
| <a name="input_monitored_postgresql_server_id"></a> [monitored\_postgresql\_server\_id](#input\_monitored\_postgresql\_server\_id) | PostgreSQL flexible server whose platform metrics Prometheus collects. | `string` | n/a | yes |
| <a name="input_monitored_resource_group_id"></a> [monitored\_resource\_group\_id](#input\_monitored\_resource\_group\_id) | Resource group whose resources' platform metrics the Azure Monitor exporter may read (Monitoring Reader). | `string` | n/a | yes |
| <a name="input_prometheus_app_name"></a> [prometheus\_app\_name](#input\_prometheus\_app\_name) | Name of Prometheus' Container App. | `string` | n/a | yes |
| <a name="input_prometheus_identity"></a> [prometheus\_identity](#input\_prometheus\_identity) | User-assigned identity of Prometheus' Container App: it reads the app's Key Vault secrets and, for the Azure Monitor exporter, the monitored resources' metrics. | <pre>object({<br/>    id           = string<br/>    principal_id = string<br/>    client_id    = string<br/>  })</pre> | n/a | yes |
| <a name="input_prometheus_secret_ids"></a> [prometheus\_secret\_ids](#input\_prometheus\_secret\_ids) | Versionless Key Vault secret IDs: web\_config (Prometheus' web configuration with the bcrypt hashes of its users' passwords) and self\_scrape\_password (the password Prometheus uses to read its own metrics). | <pre>object({<br/>    web_config           = string<br/>    self_scrape_password = string<br/>  })</pre> | n/a | yes |
| <a name="input_resource_group_name"></a> [resource\_group\_name](#input\_resource\_group\_name) | Resource group that holds the storage account and both Container Apps. | `string` | n/a | yes |
| <a name="input_storage_account_name"></a> [storage\_account\_name](#input\_storage\_account\_name) | Globally unique name of the Premium FileStorage account that holds the NFS share: 3-24 lowercase letters and digits. | `string` | n/a | yes |
| <a name="input_subscription_id"></a> [subscription\_id](#input\_subscription\_id) | Subscription of the monitored resources, which the Azure Monitor exporter queries. | `string` | n/a | yes |
| <a name="input_tags"></a> [tags](#input\_tags) | Tags applied to every taggable resource in this module. | `map(string)` | n/a | yes |
| <a name="input_grafana_cpu"></a> [grafana\_cpu](#input\_grafana\_cpu) | vCPUs for Grafana. | `number` | `0.25` | no |
| <a name="input_grafana_github_viewers"></a> [grafana\_github\_viewers](#input\_grafana\_github\_viewers) | GitHub usernames that may view dashboards (but not change anything). | `list(string)` | `[]` | no |
| <a name="input_grafana_ingress_allowed_cidrs"></a> [grafana\_ingress\_allowed\_cidrs](#input\_grafana\_ingress\_allowed\_cidrs) | IP restrictions for Grafana's ingress, keyed by rule name (Cloudflare's ranges). Grafana can't check Cloudflare's client certificate, so these are what keep direct connections out. | `map(string)` | `{}` | no |
| <a name="input_grafana_memory"></a> [grafana\_memory](#input\_grafana\_memory) | Memory for Grafana, twice grafana\_cpu in Gi. | `string` | `"0.5Gi"` | no |
| <a name="input_monitored_container_app_ids"></a> [monitored\_container\_app\_ids](#input\_monitored\_container\_app\_ids) | Container Apps whose platform metrics (replicas, restarts, CPU, memory, network) Prometheus collects, besides its own and Grafana's. | `list(string)` | `[]` | no |
| <a name="input_prometheus_cpu"></a> [prometheus\_cpu](#input\_prometheus\_cpu) | vCPUs for the Prometheus container. Its Azure Monitor exporter takes another 0.25 vCPU and 0.5 GiB. | `number` | `0.25` | no |
| <a name="input_prometheus_memory"></a> [prometheus\_memory](#input\_prometheus\_memory) | Memory for the Prometheus container, twice prometheus\_cpu in Gi. | `string` | `"0.5Gi"` | no |
| <a name="input_prometheus_retention_size"></a> [prometheus\_retention\_size](#input\_prometheus\_retention\_size) | Most disk space Prometheus' blocks may use, e.g. "20GB"; the oldest go first. Keep it well under share\_quota\_gib. | `string` | `"20GB"` | no |
| <a name="input_prometheus_retention_time"></a> [prometheus\_retention\_time](#input\_prometheus\_retention\_time) | How long Prometheus keeps samples, e.g. "30d". | `string` | `"30d"` | no |
| <a name="input_share_quota_gib"></a> [share\_quota\_gib](#input\_share\_quota\_gib) | Provisioned size of the NFS share in GiB. Premium shares bill by provisioned size, and 100 GiB is the smallest. | `number` | `100` | no |

### Outputs

| Name | Description |
| ---- | ----------- |
| <a name="output_github_callback_url"></a> [github\_callback\_url](#output\_github\_callback\_url) | Authorization callback URL to register in the GitHub OAuth App. |
| <a name="output_grafana_app_id"></a> [grafana\_app\_id](#output\_grafana\_app\_id) | Resource ID of Grafana's Container App. |
| <a name="output_grafana_app_name"></a> [grafana\_app\_name](#output\_grafana\_app\_name) | Name of Grafana's Container App. |
| <a name="output_grafana_url"></a> [grafana\_url](#output\_grafana\_url) | Grafana's public URL. |
| <a name="output_otlp_metrics_endpoint"></a> [otlp\_metrics\_endpoint](#output\_otlp\_metrics\_endpoint) | Where the app pushes its metrics (Prometheus' OTLP receiver), its Telemetry:OtlpEndpoint. |
| <a name="output_prometheus_app_id"></a> [prometheus\_app\_id](#output\_prometheus\_app\_id) | Resource ID of Prometheus' Container App. |
| <a name="output_prometheus_app_name"></a> [prometheus\_app\_name](#output\_prometheus\_app\_name) | Name of Prometheus' Container App. |
| <a name="output_prometheus_url"></a> [prometheus\_url](#output\_prometheus\_url) | Prometheus' internal URL, reachable from the environment's own apps only. |
| <a name="output_share_name"></a> [share\_name](#output\_share\_name) | Name of the NFS share (directories prometheus/ and grafana/). |
| <a name="output_storage_account_name"></a> [storage\_account\_name](#output\_storage\_account\_name) | Name of the storage account that holds the NFS share. |
<!-- END_TF_DOCS -->
