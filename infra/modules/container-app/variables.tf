variable "location" {
  description = "Azure region of the environment, app and job."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the environment, app and job."
  type        = string
}

variable "tags" {
  description = "Tags applied to every taggable resource in this module. Container Apps copies the environment's tags to its managed infrastructure resource group."
  type        = map(string)
}

# ---------------------------------------------------------------------------------------------------------------------
# Environment
# ---------------------------------------------------------------------------------------------------------------------

variable "environment_name" {
  description = "Name of the Container Apps environment."
  type        = string
}

variable "infrastructure_resource_group_name" {
  description = "Name of the platform-managed resource group that Container Apps creates for the environment's load balancer and public IP. Must not exist yet."
  type        = string
}

variable "infrastructure_subnet_id" {
  description = "Subnet delegated to Microsoft.App/environments (at least /27)."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Log Analytics workspace that receives console and system logs."
  type        = string
}

# ---------------------------------------------------------------------------------------------------------------------
# Application
# ---------------------------------------------------------------------------------------------------------------------

variable "deploy_app" {
  description = "Whether to create the Container Apps. False until an image exists in the registry."
  type        = bool
}

variable "apps" {
  description = "The app's instances, keyed by a short, stable name: each becomes a Container App running app_image with the shared settings below. name: the Container App's name (2-32 lowercase letters, digits and hyphens; also the first label of its FQDN). exposed_port: its public TCP port on the environment's IP, unique across the environment. secrets: its Container App secrets, as a map of secret name (lowercase, digits, hyphens) to a versionless Key Vault secret URI; app_secret_environment_variables refers to them by name. environment_variables: plain variables of its own, added to (and overriding) app_environment_variables."
  type = map(object({
    name                  = string
    exposed_port          = number
    secrets               = map(string)
    environment_variables = map(string)
  }))

  validation {
    condition = alltrue([
      for app in values(var.apps) : can(regex("^[a-z][a-z0-9-]{0,30}[a-z0-9]$", app.name)) && !strcontains(app.name, "--")
    ])
    error_message = "Container App names are 2-32 lowercase letters, digits and single hyphens, starting with a letter."
  }

  validation {
    condition     = length(distinct([for app in values(var.apps) : app.exposed_port])) == length(var.apps) && length(distinct([for app in values(var.apps) : app.name])) == length(var.apps)
    error_message = "Every app needs its own name and exposed port: TCP ingress can't share a port on the environment's IP."
  }
}

variable "app_image" {
  description = "Full image reference, e.g. crexample.azurecr.io/login-app:1a2b3c4, run by every app. Ignored while deploy_app is false."
  type        = string
  default     = null
}

variable "app_identity_id" {
  description = "User-assigned managed identity of the app: pulls the image (AcrPull) and resolves the Key Vault secrets."
  type        = string
}

variable "registry_server" {
  description = "Login server of the registry that holds app_image."
  type        = string
}

variable "app_target_port" {
  description = "Port the container listens on for HTTPS. The ingress, health probes and ASPNETCORE_URLS all follow it."
  type        = number
}

variable "app_cpu" {
  description = "vCPUs per replica."
  type        = number
}

variable "app_memory" {
  description = "Memory per replica, e.g. \"1Gi\". Consumption profiles require twice the vCPU count in GiB."
  type        = string
}

variable "app_min_replicas" {
  description = "Minimum number of replicas."
  type        = number
}

variable "app_max_replicas" {
  description = "Maximum number of replicas."
  type        = number
}

variable "app_secret_environment_variables" {
  description = "Environment variables that take their value from each app's secrets, the same for every app: map of variable name to secret name. Every app's secrets must define these names."
  type        = map(string)
  default     = {}
}

variable "app_environment_variables" {
  description = "Plain (non-secret) environment variables for every app's container."
  type        = map(string)
  default     = {}
}

variable "ingress_allowed_cidrs" {
  description = "IPv4 ranges allowed by the app's ingress IP restrictions, keyed by rule name. Every other source is refused; an empty map allows everyone."
  type        = map(string)
}

variable "liveness_probe_path" {
  description = "Path of the liveness and startup probes. It must not depend on the database."
  type        = string
  default     = "/health/live"
}

variable "readiness_probe_path" {
  description = "Path of the readiness probe. It includes the database check."
  type        = string
  default     = "/health/ready"
}

# ---------------------------------------------------------------------------------------------------------------------
# Database bootstrap job
# ---------------------------------------------------------------------------------------------------------------------

variable "db_bootstrap" {
  description = "Manually triggered job that creates the app's PostgreSQL role and lets it into app_databases (every instance's database). database_name is the one the job connects to first. Secrets are versionless Key Vault secret URIs."
  type = object({
    job_name                  = string
    image                     = string
    identity_id               = string
    postgresql_host           = string
    database_name             = string
    app_databases             = list(string)
    administrator_login       = string
    administrator_password_id = string
    app_role                  = string
    app_password_id           = string
  })
}
