# ---------------------------------------------------------------------------------------------------------------------
# Subscription, naming and tags
# ---------------------------------------------------------------------------------------------------------------------

variable "subscription_id" {
  description = "Azure subscription to deploy into."
  type        = string

  validation {
    condition     = can(regex("^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$", var.subscription_id))
    error_message = "subscription_id must be a subscription GUID."
  }
}

variable "location" {
  description = "Azure region for every resource."
  type        = string
  default     = "eastus"
}

variable "project" {
  description = "Project name. Used in resource names and as the value of the required \"project\" tag."
  type        = string
  default     = "loginapp"

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{2,9}$", var.project))
    error_message = "project must be 3-10 lowercase letters or digits, starting with a letter (it becomes part of globally unique names)."
  }
}

variable "environment" {
  description = "Environment name, e.g. demo. Used in resource names and the \"environment\" tag."
  type        = string
  default     = "demo"

  validation {
    condition     = can(regex("^[a-z][a-z0-9]{1,7}$", var.environment))
    error_message = "environment must be 2-8 lowercase letters or digits, starting with a letter."
  }
}

variable "name_suffix" {
  description = "Suffix that makes the registry, Key Vault and PostgreSQL names globally unique. Null derives a stable 6-character value from the subscription, project and environment."
  type        = string
  default     = null

  validation {
    condition     = var.name_suffix == null || can(regex("^[a-z0-9]{3,6}$", var.name_suffix))
    error_message = "name_suffix must be 3-6 lowercase letters or digits."
  }
}

variable "tags" {
  description = "Extra tags for every resource. The project, environment and managed_by tags are always set and win over these."
  type        = map(string)
  default     = {}
}

# ---------------------------------------------------------------------------------------------------------------------
# Network
# ---------------------------------------------------------------------------------------------------------------------

variable "vnet_address_space" {
  description = "Address space of the virtual network. Must not overlap ranges reserved by Container Apps (100.100.0.0/16 parts, 172.30.0.0/16, 172.31.0.0/16, 192.0.2.0/24, 169.254.0.0/16) and should be in 10.0.0.0/8, which the app trusts as internal."
  type        = list(string)
  default     = ["10.40.0.0/16"]

  validation {
    condition     = length(var.vnet_address_space) > 0 && alltrue([for cidr in var.vnet_address_space : can(cidrhost(cidr, 0))])
    error_message = "vnet_address_space must list at least one valid CIDR range."
  }
}

variable "subnet_address_prefixes" {
  description = "Prefix of each dedicated subnet, inside vnet_address_space. Container Apps (workload profiles) needs at least /27, PostgreSQL at least /28."
  type = object({
    container_apps = string
    postgresql     = string
  })
  default = {
    container_apps = "10.40.1.0/24"
    postgresql     = "10.40.2.0/24"
  }

  validation {
    condition = alltrue([
      for cidr in values(var.subnet_address_prefixes) : can(cidrhost(cidr, 0))
    ]) && tonumber(split("/", var.subnet_address_prefixes.container_apps)[1]) <= 27 && tonumber(split("/", var.subnet_address_prefixes.postgresql)[1]) <= 28
    error_message = "Each subnet prefix must be a valid CIDR range; container_apps must be /27 or larger and postgresql /28 or larger."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Public entry point: Cloudflare DNS and the app's TLS
# ---------------------------------------------------------------------------------------------------------------------

variable "dns_zone_name" {
  description = "Cloudflare zone that holds the app's DNS record."
  type        = string
  default     = "biffkittz.com"
}

variable "app_hostname" {
  description = "Public host name of the app. Terraform creates a proxied A record for it in the Cloudflare zone."
  type        = string
  default     = "app.biffkittz.com"

  validation {
    condition     = endswith(lower(var.app_hostname), ".${lower(var.dns_zone_name)}")
    error_message = "app_hostname must be a name inside dns_zone_name, e.g. app.biffkittz.com."
  }
}

variable "restrict_ingress_to_cloudflare" {
  description = "Allow connections to the app's public ingress only from Cloudflare's published IPv4 ranges (plus ingress_additional_allowed_cidrs), using the ingress IP restrictions. The client-certificate check is the stronger control; this one keeps everyone else from even reaching Kestrel."
  type        = bool
  default     = true
}

variable "ingress_additional_allowed_cidrs" {
  description = "Extra source ranges allowed by the app's ingress IP restrictions when restrict_ingress_to_cloudflare is true, e.g. your own IP for troubleshooting. Without Cloudflare's client certificate they still get only the health endpoints."
  type        = list(string)
  default     = []

  validation {
    condition     = alltrue([for cidr in var.ingress_additional_allowed_cidrs : can(cidrhost(cidr, 0))])
    error_message = "ingress_additional_allowed_cidrs must contain CIDR ranges, e.g. 203.0.113.10/32."
  }
}

variable "require_cloudflare_client_certificate" {
  description = "Turn on Cloudflare Authenticated Origin Pulls for the zone (Cloudflare then presents a client certificate to every origin) and make the app refuse requests without it. This is what keeps visitors from bypassing Cloudflare: a public Container Apps environment can't be locked down with an NSG."
  type        = bool
  default     = true
}

variable "tls_certificate_pfx_path" {
  description = "Path to the PFX file with the TLS certificate and private key that the app serves: the Cloudflare origin certificate. Keep it outside the repository (\"~\" is expanded)."
  type        = string

  validation {
    condition     = fileexists(pathexpand(var.tls_certificate_pfx_path))
    error_message = "tls_certificate_pfx_path doesn't point to a file."
  }
}

variable "tls_certificate_pfx_password" {
  description = "Password of the PFX file, or null when it has none. Pass it as TF_VAR_tls_certificate_pfx_password; never put it in a tfvars file."
  type        = string
  default     = null
  sensitive   = true
}

# ---------------------------------------------------------------------------------------------------------------------
# Container registry and the Container App
# ---------------------------------------------------------------------------------------------------------------------

variable "container_registry_sku" {
  description = "Azure Container Registry SKU."
  type        = string
  default     = "Basic"
}

variable "app_image_repository" {
  description = "Repository of the app image in the registry."
  type        = string
  default     = "login-app"
}

variable "app_image_tag" {
  description = "Tag of the app image to run, e.g. the short commit SHA. Null (the default) skips creating the Container App until a first image has been pushed; see docs/DEPLOYMENT.md."
  type        = string
  default     = null

  validation {
    condition     = var.app_image_tag == null || can(regex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$", var.app_image_tag))
    error_message = "app_image_tag must be a valid image tag (letters, digits, _, . and -, at most 128 characters)."
  }
}

variable "app_target_port" {
  description = "Port Kestrel listens on for HTTPS inside the container. 443 needs the image's port-binding capability; set 8443 if the platform refuses it (see docs/DEPLOYMENT.md)."
  type        = number
  default     = 443

  validation {
    condition     = var.app_target_port >= 1 && var.app_target_port <= 65535 && var.app_target_port != 36985
    error_message = "app_target_port must be a TCP port; 36985 is reserved by Container Apps for health checks."
  }
}

variable "app_exposed_port" {
  description = "Public TCP port of the environment that Cloudflare connects to. Not 80 or 443: the environment's own HTTP ingress holds those (Azure answers \"exposedPort already in use\"). Terraform adds a Cloudflare Origin Rule so visitors still use 443."
  type        = number
  default     = 8443

  validation {
    condition     = var.app_exposed_port >= 1 && var.app_exposed_port <= 65535 && !contains([80, 443, 36985], var.app_exposed_port)
    error_message = "app_exposed_port must be a TCP port other than 80 and 443 (the environment's HTTP ingress) and 36985 (Container Apps health checks)."
  }
}

variable "app_cpu" {
  description = "vCPUs per app replica."
  type        = number
  default     = 0.5
}

variable "app_memory" {
  description = "Memory per app replica. The Consumption profile requires exactly twice the vCPUs in GiB (0.5 vCPU -> 1Gi)."
  type        = string
  default     = "1Gi"

  validation {
    condition     = can(regex("^[0-9]+(\\.[0-9]+)?Gi$", var.app_memory)) && tonumber(trimsuffix(var.app_memory, "Gi")) == var.app_cpu * 2
    error_message = "app_memory must be twice app_cpu in Gi, e.g. app_cpu = 0.5 with app_memory = \"1Gi\"."
  }
}

variable "app_min_replicas" {
  description = "Minimum number of app replicas. 1 keeps one warm replica (no cold starts)."
  type        = number
  default     = 1
}

variable "app_max_replicas" {
  description = "Maximum number of app replicas. The demo runs exactly one."
  type        = number
  default     = 1

  validation {
    condition     = var.app_max_replicas >= 1 && var.app_min_replicas >= 0 && var.app_min_replicas <= var.app_max_replicas
    error_message = "Replica counts need 0 <= app_min_replicas <= app_max_replicas, with app_max_replicas at least 1."
  }
}

variable "container_apps_transitional_ingress_cidrs" {
  description = "Container Apps infrastructure ranges also allowed by the app's ingress IP restrictions. While Microsoft rolls out real client IPs to the ingress (until about March 2027, GitHub issue microsoft/azure-container-apps#1666), requests may appear to come from these ranges. Remove them once Microsoft announces the rollout is complete; Cloudflare's client certificate keeps other callers out in the meantime."
  type        = list(string)
  default     = ["100.100.0.0/17", "100.100.128.0/19", "100.100.160.0/19", "100.100.192.0/19"]

  validation {
    condition     = alltrue([for cidr in var.container_apps_transitional_ingress_cidrs : can(cidrhost(cidr, 0))])
    error_message = "container_apps_transitional_ingress_cidrs must contain CIDR ranges."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# PostgreSQL
# ---------------------------------------------------------------------------------------------------------------------

variable "postgresql_version" {
  description = "PostgreSQL major version."
  type        = string
  default     = "18"

  validation {
    condition     = contains(["16", "17", "18"], var.postgresql_version)
    error_message = "postgresql_version must be 16, 17 or 18."
  }
}

variable "postgresql_sku_name" {
  description = "Server compute SKU. B_Standard_B1ms (Burstable, 1 vCore, 2 GiB) is the smallest available."
  type        = string
  default     = "B_Standard_B1ms"
}

variable "postgresql_storage_mb" {
  description = "Server storage in MB. 32768 (32 GiB) is the smallest size."
  type        = number
  default     = 32768

  validation {
    condition     = contains([32768, 65536, 131072, 262144, 524288, 1048576, 2097152, 4193280, 4194304, 8388608, 16777216, 33553408], var.postgresql_storage_mb)
    error_message = "postgresql_storage_mb must be one of the sizes Premium SSD storage supports, e.g. 32768."
  }
}

variable "postgresql_backup_retention_days" {
  description = "Days of point-in-time-restore backups (7-35)."
  type        = number
  default     = 7

  validation {
    condition     = var.postgresql_backup_retention_days >= 7 && var.postgresql_backup_retention_days <= 35
    error_message = "postgresql_backup_retention_days must be between 7 and 35."
  }
}

variable "postgresql_admin_login" {
  description = "Login name of the server administrator. Used only by the bootstrap job, never by the app."
  type        = string
  default     = "loginapp_admin"

  validation {
    condition = (
      can(regex("^[a-z][a-z0-9_]{0,62}$", var.postgresql_admin_login)) &&
      !startswith(var.postgresql_admin_login, "pg_") &&
      !contains(["admin", "administrator", "azure_pg_admin", "azure_superuser", "azuresu", "guest", "public", "root", "sa", "superuser"], var.postgresql_admin_login)
    )
    error_message = "postgresql_admin_login must be a lowercase identifier that isn't reserved (admin, root, azure_superuser, pg_*, ...)."
  }
}

variable "postgresql_app_role" {
  description = "PostgreSQL login role the app connects as. The bootstrap job creates it; it owns the app's tables but has no server-level rights."
  type        = string
  default     = "loginapp_app"

  validation {
    condition     = can(regex("^[a-z][a-z0-9_]{0,62}$", var.postgresql_app_role)) && !startswith(var.postgresql_app_role, "pg_")
    error_message = "postgresql_app_role must be a lowercase identifier that doesn't start with pg_."
  }
}

variable "database_name" {
  description = "Name of the application database."
  type        = string
  default     = "loginapp"
}

variable "db_bootstrap_image" {
  description = "Image with psql for the database bootstrap job, pinned to a dated tag. Microsoft's Azure Linux PostgreSQL image is pulled anonymously from MCR, so there is no Docker Hub rate limit."
  type        = string
  default     = "mcr.microsoft.com/azurelinux/base/postgres:16.15-1-azl3.0.20261027"
}

# ---------------------------------------------------------------------------------------------------------------------
# Key Vault
# ---------------------------------------------------------------------------------------------------------------------

variable "key_vault_purge_protection_enabled" {
  description = "Prevents purging deleted vault objects before soft-delete retention (7 days) ends. Recommended; it can't be turned off later."
  type        = bool
  default     = true
}

variable "key_vault_firewall_enabled" {
  description = "Deny Key Vault network access except from the Container Apps subnet, trusted Azure services and key_vault_allowed_ip_ranges. Read docs/ARCHITECTURE.md (Key Vault network access) before enabling it."
  type        = bool
  default     = false
}

variable "key_vault_allowed_ip_ranges" {
  description = "Public ranges allowed through the Key Vault firewall: wherever Terraform runs, e.g. [\"203.0.113.10/32\"]."
  type        = list(string)
  default     = []

  validation {
    condition     = alltrue([for cidr in var.key_vault_allowed_ip_ranges : can(cidrhost(cidr, 0))])
    error_message = "key_vault_allowed_ip_ranges must contain CIDR ranges, e.g. 203.0.113.10/32."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Monitoring and timing
# ---------------------------------------------------------------------------------------------------------------------

variable "log_analytics_retention_days" {
  description = "Days the Log Analytics workspace keeps logs (30-730)."
  type        = number
  default     = 30

  validation {
    condition     = var.log_analytics_retention_days >= 30 && var.log_analytics_retention_days <= 730
    error_message = "log_analytics_retention_days must be between 30 and 730."
  }
}

variable "log_analytics_daily_quota_gb" {
  description = "Daily ingestion cap in GB, protecting the demo budget. Ingestion stops for the rest of the day when reached; -1 removes the cap."
  type        = number
  default     = 1

  validation {
    condition     = var.log_analytics_daily_quota_gb == -1 || var.log_analytics_daily_quota_gb > 0
    error_message = "log_analytics_daily_quota_gb must be -1 (no cap) or a positive number."
  }
}

variable "rbac_propagation_wait" {
  description = "Pause after creating role assignments before relying on them. Azure usually propagates them in 1-2 minutes."
  type        = string
  default     = "90s"

  validation {
    condition     = can(regex("^[0-9]+(s|m)$", var.rbac_propagation_wait))
    error_message = "rbac_propagation_wait must be a duration such as \"90s\" or \"2m\"."
  }
}
