variable "location" {
  description = "Azure region of the storage account."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the storage account and both Container Apps."
  type        = string
}

variable "tags" {
  description = "Tags applied to every taggable resource in this module."
  type        = map(string)
}

# ---------------------------------------------------------------------------------------------------------------------
# Where it runs
# ---------------------------------------------------------------------------------------------------------------------

variable "subscription_id" {
  description = "Subscription of the monitored resources, which the Azure Monitor exporter queries."
  type        = string
}

variable "container_app_environment_id" {
  description = "Container Apps environment that runs Prometheus and Grafana (the app's own environment)."
  type        = string
}

variable "container_app_environment_default_domain" {
  description = "The environment's default domain. Prometheus' internal address is <prometheus_app_name>.internal.<default domain>."
  type        = string
}

variable "container_apps_subnet_id" {
  description = "The environment's subnet, which must have the Microsoft.Storage service endpoint: the storage account admits NFS mounts from this subnet only."
  type        = string
}

# ---------------------------------------------------------------------------------------------------------------------
# Storage (Prometheus' database and Grafana's)
# ---------------------------------------------------------------------------------------------------------------------

variable "storage_account_name" {
  description = "Globally unique name of the Premium FileStorage account that holds the NFS share: 3-24 lowercase letters and digits."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.storage_account_name))
    error_message = "storage_account_name must be 3-24 lowercase letters and digits."
  }
}

variable "share_quota_gib" {
  description = "Provisioned size of the NFS share in GiB. Premium shares bill by provisioned size, and 100 GiB is the smallest."
  type        = number
  default     = 100

  validation {
    condition     = var.share_quota_gib >= 100 && floor(var.share_quota_gib) == var.share_quota_gib
    error_message = "share_quota_gib must be a whole number of at least 100 (the smallest Premium file share)."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Images
# ---------------------------------------------------------------------------------------------------------------------

variable "images" {
  description = "Images, each pinned by digest: prometheus, azure_metrics_exporter, grafana, and init (Azure Linux with bash and openssl, which prepares files before Prometheus and Grafana start)."
  type = object({
    prometheus             = string
    azure_metrics_exporter = string
    grafana                = string
    init                   = string
  })

  validation {
    condition     = alltrue([for image in values(var.images) : can(regex("^[a-z0-9.-]+(:[0-9]+)?(/[a-z0-9._-]+)+@sha256:[0-9a-f]{64}$", image))])
    error_message = "Every image must be <registry>/<repository>@sha256:<digest>: a tag can be moved to other content, a digest can't."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Prometheus
# ---------------------------------------------------------------------------------------------------------------------

variable "prometheus_app_name" {
  description = "Name of Prometheus' Container App."
  type        = string
}

variable "prometheus_identity" {
  description = "User-assigned identity of Prometheus' Container App: it reads the app's Key Vault secrets and, for the Azure Monitor exporter, the monitored resources' metrics."
  type = object({
    id           = string
    principal_id = string
    client_id    = string
  })
}

variable "prometheus_cpu" {
  description = "vCPUs for the Prometheus container. Its Azure Monitor exporter takes another 0.25 vCPU and 0.5 GiB."
  type        = number
  default     = 0.25
}

variable "prometheus_memory" {
  description = "Memory for the Prometheus container, twice prometheus_cpu in Gi."
  type        = string
  default     = "0.5Gi"

  validation {
    condition     = can(regex("^[0-9]+(\\.[0-9]+)?Gi$", var.prometheus_memory)) && tonumber(trimsuffix(var.prometheus_memory, "Gi")) == var.prometheus_cpu * 2
    error_message = "prometheus_memory must be twice prometheus_cpu in Gi, e.g. 0.25 with \"0.5Gi\"."
  }
}

variable "prometheus_retention_time" {
  description = "How long Prometheus keeps samples, e.g. \"30d\"."
  type        = string
  default     = "30d"

  validation {
    condition     = can(regex("^[0-9]+(h|d|w|y)$", var.prometheus_retention_time))
    error_message = "prometheus_retention_time must be a number with h, d, w or y, e.g. \"30d\"."
  }
}

variable "prometheus_retention_size" {
  description = "Most disk space Prometheus' blocks may use, e.g. \"20GB\"; the oldest go first. Keep it well under share_quota_gib."
  type        = string
  default     = "20GB"

  validation {
    condition     = can(regex("^[0-9]+(MB|GB)$", var.prometheus_retention_size))
    error_message = "prometheus_retention_size must be a number with MB or GB, e.g. \"20GB\"."
  }
}

variable "prometheus_secret_ids" {
  description = "Versionless Key Vault secret IDs: web_config (Prometheus' web configuration with the bcrypt hashes of its users' passwords) and self_scrape_password (the password Prometheus uses to read its own metrics)."
  type = object({
    web_config           = string
    self_scrape_password = string
  })
}

variable "monitored_resource_group_id" {
  description = "Resource group whose resources' platform metrics the Azure Monitor exporter may read (Monitoring Reader)."
  type        = string
}

variable "monitored_container_app_ids" {
  description = "Container Apps whose platform metrics (replicas, restarts, CPU, memory, network) Prometheus collects, besides its own and Grafana's."
  type        = list(string)
  default     = []
}

variable "monitored_postgresql_server_id" {
  description = "PostgreSQL flexible server whose platform metrics Prometheus collects."
  type        = string
}

# ---------------------------------------------------------------------------------------------------------------------
# Grafana
# ---------------------------------------------------------------------------------------------------------------------

variable "grafana_app_name" {
  description = "Name of Grafana's Container App."
  type        = string
}

variable "grafana_identity_id" {
  description = "Resource ID of Grafana's user-assigned identity, which reads its Key Vault secrets and the origin certificate."
  type        = string
}

variable "grafana_cpu" {
  description = "vCPUs for Grafana."
  type        = number
  default     = 0.25
}

variable "grafana_memory" {
  description = "Memory for Grafana, twice grafana_cpu in Gi."
  type        = string
  default     = "0.5Gi"

  validation {
    condition     = can(regex("^[0-9]+(\\.[0-9]+)?Gi$", var.grafana_memory)) && tonumber(trimsuffix(var.grafana_memory, "Gi")) == var.grafana_cpu * 2
    error_message = "grafana_memory must be twice grafana_cpu in Gi, e.g. 0.25 with \"0.5Gi\"."
  }
}

variable "grafana_hostname" {
  description = "Public host name of Grafana (Cloudflare proxies it), e.g. grafana.biffkittz.com. GitHub sends users back to https://<host name>/login/github."
  type        = string
}

variable "grafana_exposed_port" {
  description = "Public TCP port of the environment that Cloudflare's Origin Rule connects to for Grafana. Unique among the environment's TCP apps."
  type        = number
}

variable "grafana_ingress_allowed_cidrs" {
  description = "IP restrictions for Grafana's ingress, keyed by rule name (Cloudflare's ranges). Grafana can't check Cloudflare's client certificate, so these are what keep direct connections out."
  type        = map(string)
  default     = {}
}

variable "grafana_github_client_id" {
  description = "Client ID of the GitHub OAuth App that Grafana signs users in with."
  type        = string
}

variable "grafana_github_admins" {
  description = "GitHub usernames that become Grafana administrators. Anyone not in this list or grafana_github_viewers is refused."
  type        = list(string)

  validation {
    condition     = length(var.grafana_github_admins) > 0
    error_message = "grafana_github_admins must name at least one GitHub user: Grafana has no other administrator."
  }
}

variable "grafana_github_viewers" {
  description = "GitHub usernames that may view dashboards (but not change anything)."
  type        = list(string)
  default     = []
}

variable "grafana_secret_ids" {
  description = "Versionless Key Vault secret IDs for Grafana: github_client_secret, secret_key (encrypts what Grafana stores), prometheus_password (Grafana's Prometheus user), and origin_certificate (the certificate's secret, a Base64 PFX without password)."
  type = object({
    github_client_secret = string
    secret_key           = string
    prometheus_password  = string
    origin_certificate   = string
  })
}
