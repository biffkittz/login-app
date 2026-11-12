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
  description = "Public host name of the app's main instance. Terraform creates a proxied A record for it in the Cloudflare zone. Further instances are in additional_app_instances."
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

variable "gemini_api_key" {
  description = "Google Gemini API key with which the app draws pictures of grocery items and estimates what the list costs. Gemini's image models have no free tier, so the key's Google Cloud project needs billing. Null leaves pictures and cost estimates unavailable (the app says so). Pass it as TF_VAR_gemini_api_key; never put it in a tfvars file."
  type        = string
  default     = null
  sensitive   = true

  validation {
    condition     = var.gemini_api_key == null || try(trimspace(var.gemini_api_key) != "", false)
    error_message = "gemini_api_key must be null or a key; an empty string would look configured to Terraform but not to the app."
  }
}

variable "temporal_namespace" {
  description = "Temporal Cloud namespace that runs grocery runs, as <namespace>.<account> (e.g. nibbles-demo.a1b2c): the app's in-process worker polls it, and the API starts and signals workflows in it. Both sites share it, each with a task queue of its own (nibbles-<instance>). Null leaves grocery runs unavailable (the app says so). Not a secret."
  type        = string
  default     = null

  validation {
    condition     = var.temporal_namespace == null || can(regex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?\\.[a-z0-9]+$", var.temporal_namespace))
    error_message = "temporal_namespace must be the namespace's full ID, <namespace>.<account> in lower case, as the Temporal Cloud UI shows it (e.g. nibbles-demo.a1b2c)."
  }

  validation {
    condition     = (var.temporal_namespace == null) == (var.temporal_api_key == null)
    error_message = "temporal_namespace and TF_VAR_temporal_api_key go together: set both to turn grocery runs on, or neither."
  }
}

variable "temporal_address" {
  description = "Temporal Cloud gRPC endpoint (host:port) for temporal_namespace. Null uses the namespace endpoint, <namespace>.<account>.tmprl.cloud:7233, which Temporal recommends for API keys."
  type        = string
  default     = null

  validation {
    condition     = var.temporal_address == null || can(regex("^[A-Za-z0-9][A-Za-z0-9.-]*:[0-9]{1,5}$", var.temporal_address))
    error_message = "temporal_address must be host:port, such as nibbles-demo.a1b2c.tmprl.cloud:7233."
  }
}

variable "temporal_api_key" {
  description = "API key of a namespace-scoped Temporal Cloud service account with Write permission on temporal_namespace. Keys expire (2 years at most); rotate before then (infra/docs/DEPLOYMENT.md). Pass it as TF_VAR_temporal_api_key (the TEMPORAL_API_KEY secret in CI); never put it in a tfvars file."
  type        = string
  default     = null
  sensitive   = true

  validation {
    condition     = var.temporal_api_key == null || can(regex("^[^\\s]{20,}$", var.temporal_api_key))
    error_message = "temporal_api_key must be null or a key without spaces; check how it was copied."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Container registry and the Container App
# ---------------------------------------------------------------------------------------------------------------------

variable "registration_notification_recipients" {
  description = "Email addresses that get an email whenever someone registers an account on any instance of the app, sent through Cloudflare Email Service from notification_sender_username@dns_zone_name. Empty (the default) turns the emails off and leaves Cloudflare Email Service unconfigured. Cloudflare emails each address a confirmation link once (docs/DEPLOYMENT.md); sending to confirmed addresses is free on every Cloudflare plan."
  type        = list(string)
  default     = []

  validation {
    condition = alltrue([
      for address in var.registration_notification_recipients :
      can(regex("^[^@\\s\"<>,;]+@[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)+$", address)) && length(address) <= 254
    ])
    error_message = "Each of registration_notification_recipients must be a plain email address (name@example.com), at most 254 characters."
  }

  validation {
    condition = (
      length(var.registration_notification_recipients) <= 50 &&
      length(distinct([for address in var.registration_notification_recipients : lower(address)])) == length(var.registration_notification_recipients)
    )
    error_message = "registration_notification_recipients must not repeat an address, and holds at most 50: Cloudflare's limit for one email."
  }
}

variable "notification_sender_username" {
  description = "The part before the @ of the address the app sends its emails from, in dns_zone_name: \"notifications\" sends from notifications@biffkittz.com. Nobody reads replies: Cloudflare Email Service only sends from it."
  type        = string
  default     = "notifications"

  validation {
    condition     = can(regex("^[a-z0-9]([a-z0-9._-]{0,62}[a-z0-9])?$", var.notification_sender_username)) && !strcontains(var.notification_sender_username, "..")
    error_message = "notification_sender_username must be 1-64 lowercase letters, digits, dots, hyphens or underscores, start and end with a letter or digit, and have no two dots in a row."
  }
}

variable "cloudflare_email_sending_permission_groups" {
  description = "Names of Cloudflare API token permission groups that allow sending email, narrowest first. The app's own sending token gets the first one the Cloudflare account offers. Change this only if Cloudflare renames them; the plan then fails and lists the email sending groups it found."
  type        = list(string)
  default     = ["Email Sending Send", "Email Sending Write"]

  validation {
    condition     = length(var.cloudflare_email_sending_permission_groups) > 0 && alltrue([for name in var.cloudflare_email_sending_permission_groups : trimspace(name) != ""])
    error_message = "cloudflare_email_sending_permission_groups must list at least one permission group name."
  }
}

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
  description = "Tag of the app image every instance runs, e.g. the short commit SHA. Null (the default) skips creating the Container Apps until a first image has been pushed; see docs/DEPLOYMENT.md."
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
  description = "Public TCP port of the environment that Cloudflare connects to for the main instance (additional instances have their own). Not 80 or 443: the environment's own HTTP ingress holds those (Azure answers \"exposedPort already in use\"). Terraform adds a Cloudflare Origin Rule so visitors still use 443."
  type        = number
  default     = 8443

  validation {
    condition     = var.app_exposed_port >= 1 && var.app_exposed_port <= 65535 && !contains([80, 443, 36985], var.app_exposed_port)
    error_message = "app_exposed_port must be a TCP port other than 80 and 443 (the environment's HTTP ingress) and 36985 (Container Apps health checks)."
  }
}

variable "additional_app_instances" {
  description = "More instances of the app, keyed by a short, stable name (e.g. grocery). Each runs the main instance's image with its identity, Key Vault secrets and settings, but has its own Container App (<main app name>-<key>), public host name (a proxied DNS record and an Origin Rule), exposed port and database on the same PostgreSQL server, so its list and accounts are its own. After adding one, run the database bootstrap job (docs/DEPLOYMENT.md)."
  type = map(object({
    hostname      = string
    exposed_port  = number
    database_name = string
  }))
  default = {}

  validation {
    condition     = alltrue([for key in keys(var.additional_app_instances) : can(regex("^[a-z][a-z0-9]{1,11}$", key)) && !contains(["main", "grafana", "prometheus"], key)])
    error_message = "Instance keys are 2-12 lowercase letters or digits, starting with a letter, and not \"main\" (the instance at app_hostname), \"grafana\" or \"prometheus\" (their Container Apps' names)."
  }

  validation {
    condition = (
      alltrue([for instance in values(var.additional_app_instances) : endswith(lower(instance.hostname), ".${lower(var.dns_zone_name)}")]) &&
      length(distinct(concat([lower(var.app_hostname)], [for instance in values(var.additional_app_instances) : lower(instance.hostname)]))) == length(var.additional_app_instances) + 1
    )
    error_message = "Each instance's hostname must be a name inside dns_zone_name, different from app_hostname and from the other instances'."
  }

  validation {
    condition = (
      alltrue([for instance in values(var.additional_app_instances) : instance.exposed_port >= 1 && instance.exposed_port <= 65535 && !contains([80, 443, 36985], instance.exposed_port)]) &&
      length(distinct(concat([var.app_exposed_port], [for instance in values(var.additional_app_instances) : instance.exposed_port]))) == length(var.additional_app_instances) + 1
    )
    error_message = "Each instance's exposed_port must be a TCP port other than 80, 443 and 36985 (see app_exposed_port), and differ from app_exposed_port and the other instances': TCP ingress can't share a port."
  }

  validation {
    condition = (
      alltrue([
        for instance in values(var.additional_app_instances) :
        can(regex("^[a-z][a-z0-9_]{0,62}$", instance.database_name)) && !startswith(instance.database_name, "pg_") &&
        !contains(["postgres", "azure_maintenance", "azure_sys", "template0", "template1"], instance.database_name)
      ]) &&
      length(distinct(concat([var.database_name], [for instance in values(var.additional_app_instances) : instance.database_name]))) == length(var.additional_app_instances) + 1
    )
    error_message = "Each instance's database_name must be a lowercase identifier that isn't a system database, and differ from database_name and the other instances': every instance keeps its own data."
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
  description = "Maximum number of app replicas. Must be 1: the grocery list's realtime updates (SignalR) run inside the app with no backplane, so users connected to a second replica would never see each other's changes."
  type        = number
  default     = 1

  validation {
    condition     = var.app_max_replicas == 1 && var.app_min_replicas >= 0 && var.app_min_replicas <= var.app_max_replicas
    error_message = "app_max_replicas must be 1 (SignalR runs in-process, without a backplane), and app_min_replicas 0 or 1."
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
  description = "Name of the main instance's database. Additional instances name theirs in additional_app_instances."
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

variable "key_vault_administrator_object_id" {
  description = "Object ID of the principal that manages the vault's contents (Key Vault Secrets Officer and Certificates Officer). Null means whoever runs Terraform, which suits one person running it. Set it once several principals run Terraform, such as you and GitHub Actions (docs/CI_CD.md): otherwise every plan moves the role to whoever runs it."
  type        = string
  default     = null

  validation {
    condition     = var.key_vault_administrator_object_id == null || can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", var.key_vault_administrator_object_id))
    error_message = "key_vault_administrator_object_id must be an object ID (a GUID), or null."
  }
}

variable "key_vault_additional_administrators" {
  description = "More principals that manage the vault's contents, by a label of your choosing, e.g. { github_deploy = \"<object ID>\" }. Every principal that runs a refreshing plan or an apply needs this access: Terraform reads the secrets back."
  type        = map(string)
  default     = {}

  validation {
    condition = alltrue([
      for label, object_id in var.key_vault_additional_administrators :
      can(regex("^[a-z0-9_-]{1,40}$", label)) && can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", object_id))
    ])
    error_message = "key_vault_additional_administrators maps labels (lower case letters, digits, _ and -) to object IDs (GUIDs)."
  }

  validation {
    condition = var.key_vault_administrator_object_id == null || !contains(
      [for object_id in values(var.key_vault_additional_administrators) : lower(object_id)],
      lower(coalesce(var.key_vault_administrator_object_id, "-"))
    )
    error_message = "key_vault_additional_administrators must not list key_vault_administrator_object_id again: Azure refuses a second identical role assignment."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Observability: Prometheus and Grafana (modules/observability)
# ---------------------------------------------------------------------------------------------------------------------

variable "observability_enabled" {
  description = "Runs Prometheus and Grafana in the app's environment: the app pushes its metrics to Prometheus, and Grafana is public at grafana_hostname with GitHub sign-in. Needs grafana_github_client_id, grafana_github_admins and TF_VAR_grafana_github_client_secret (infra/docs/DEPLOYMENT.md)."
  type        = bool
  default     = false
}

variable "grafana_hostname" {
  description = "Public host name of Grafana, inside dns_zone_name. Cloudflare proxies it like the app's."
  type        = string
  default     = "grafana.biffkittz.com"

  validation {
    condition = (
      endswith(lower(var.grafana_hostname), ".${lower(var.dns_zone_name)}") &&
      !contains(concat([lower(var.app_hostname)], [for instance in values(var.additional_app_instances) : lower(instance.hostname)]), lower(var.grafana_hostname))
    )
    error_message = "grafana_hostname must be a name inside dns_zone_name that no instance of the app uses."
  }
}

variable "grafana_exposed_port" {
  description = "Public TCP port of the environment for Grafana, which Cloudflare's Origin Rule connects to (see app_exposed_port)."
  type        = number
  default     = 8445

  validation {
    condition = (
      var.grafana_exposed_port >= 1 && var.grafana_exposed_port <= 65535 &&
      !contains(concat([80, 443, 36985, var.app_exposed_port], [for instance in values(var.additional_app_instances) : instance.exposed_port]), var.grafana_exposed_port)
    )
    error_message = "grafana_exposed_port must be a TCP port other than 80, 443 and 36985 and other than every app instance's exposed_port: TCP ingress can't share a port."
  }
}

variable "grafana_github_client_id" {
  description = "Client ID of the GitHub OAuth App that Grafana signs users in with (not a secret). Register it with the callback URL https://<grafana_hostname>/login/github."
  type        = string
  default     = null

  validation {
    condition     = var.grafana_github_client_id == null || can(regex("^[A-Za-z0-9.]{16,40}$", var.grafana_github_client_id))
    error_message = "grafana_github_client_id must be a GitHub OAuth App's client ID, such as Ov23li followed by 14 letters and digits."
  }

  validation {
    condition     = !var.observability_enabled || var.grafana_github_client_id != null
    error_message = "observability_enabled needs grafana_github_client_id: GitHub is the only way to sign in to Grafana."
  }
}

variable "grafana_github_client_secret" {
  description = "Client secret of that GitHub OAuth App. A secret: pass it as TF_VAR_grafana_github_client_secret (the GRAFANA_GITHUB_CLIENT_SECRET secret in CI); it is kept in Key Vault."
  type        = string
  default     = null
  sensitive   = true

  validation {
    condition     = !var.observability_enabled || can(regex("^[A-Za-z0-9_]{20,}$", var.grafana_github_client_secret))
    error_message = "observability_enabled needs TF_VAR_grafana_github_client_secret: the OAuth App's client secret (GitHub shows it once, when it is generated)."
  }
}

variable "grafana_github_admins" {
  description = "GitHub usernames, as GitHub spells them, that sign in to Grafana as administrators. Anyone not listed here or in grafana_github_viewers is refused."
  type        = list(string)
  default     = []

  validation {
    condition     = alltrue([for login in var.grafana_github_admins : can(regex("^[A-Za-z0-9]([A-Za-z0-9-]{0,37}[A-Za-z0-9])?$", login))])
    error_message = "grafana_github_admins must hold GitHub usernames: letters, digits and inner hyphens, at most 39 characters."
  }

  validation {
    condition     = !var.observability_enabled || length(var.grafana_github_admins) > 0
    error_message = "observability_enabled needs at least one GitHub username in grafana_github_admins: Grafana has no other administrator."
  }
}

variable "grafana_github_viewers" {
  description = "GitHub usernames that may sign in to Grafana to view dashboards, without changing anything."
  type        = list(string)
  default     = []

  validation {
    condition     = alltrue([for login in var.grafana_github_viewers : can(regex("^[A-Za-z0-9]([A-Za-z0-9-]{0,37}[A-Za-z0-9])?$", login))])
    error_message = "grafana_github_viewers must hold GitHub usernames: letters, digits and inner hyphens, at most 39 characters."
  }
}

variable "observability_images" {
  description = "Images of the observability stack, pinned by digest (a tag can be moved to other content). Dependabot can't see these: check for new releases now and then (infra/docs/DEPLOYMENT.md)."
  type = object({
    prometheus             = string
    azure_metrics_exporter = string
    grafana                = string
    init                   = string
  })
  default = {
    # Prometheus v3.15.1, from Prometheus' own registry (no Docker Hub pull limits).
    prometheus = "quay.io/prometheus/prometheus@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2"
    # webdevops/azure-metrics-exporter 26.10.0 (MIT): Azure Monitor metrics for Prometheus.
    azure_metrics_exporter = "quay.io/webdevops/azure-metrics-exporter@sha256:0f26a275e593538983a13c9ebb9d229e296524a3f292e684666667f24eae2b76"
    # Grafana OSS 13.3.1 (a security release), from Docker Hub.
    grafana = "docker.io/grafana/grafana@sha256:0615ec4fbd4072004b7d86b46ded63013a74b7d6eb1119273dd89e8fa25c13cf"
    # Azure Linux 3.0.20261027 core, the base of the database bootstrap job's image: bash, openssl, gzip.
    init = "mcr.microsoft.com/azurelinux/base/core@sha256:88fc2b4a425801b3b4d8fe5342e24fb6134669fb718ec970340bdd36dee50c35"
  }
}

variable "prometheus_cpu" {
  description = "vCPUs for Prometheus. Its Azure Monitor exporter takes another 0.25 vCPU and 0.5 GiB."
  type        = number
  default     = 0.25
}

variable "prometheus_memory" {
  description = "Memory for Prometheus, twice prometheus_cpu in Gi."
  type        = string
  default     = "0.5Gi"
}

variable "prometheus_retention_time" {
  description = "How long Prometheus keeps metrics, e.g. \"30d\"."
  type        = string
  default     = "30d"
}

variable "prometheus_retention_size" {
  description = "Most space Prometheus' data may take on the 100 GiB share, e.g. \"20GB\"; the oldest data goes first."
  type        = string
  default     = "20GB"
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
