variable "location" {
  description = "Azure region of the environment, app and job."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the environment, app, job and the environment's private DNS zone."
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
  description = "Name of the platform-managed resource group that Container Apps creates for the environment's load balancer. Must not exist yet."
  type        = string
}

variable "infrastructure_subnet_id" {
  description = "Subnet delegated to Microsoft.App/environments (at least /27)."
  type        = string
}

variable "virtual_network_id" {
  description = "Virtual network to link the environment's private DNS zone to."
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
  description = "Whether to create the Container App. False until an image exists in the registry."
  type        = bool
}

variable "app_name" {
  description = "Name of the Container App (2-32 lowercase letters, digits and hyphens). It is also the first label of the app's FQDN."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{0,30}[a-z0-9]$", var.app_name)) && !strcontains(var.app_name, "--")
    error_message = "Container App names are 2-32 lowercase letters, digits and single hyphens, starting with a letter."
  }
}

variable "app_image" {
  description = "Full image reference, e.g. crexample.azurecr.io/login-app:1a2b3c4. Ignored while deploy_app is false."
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
  description = "Port the container listens on. The ingress, health probes and ASPNETCORE_HTTP_PORTS all follow it."
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

variable "app_secrets" {
  description = "Container App secrets backed by Key Vault: map of secret name (lowercase, digits, hyphens) to a versionless Key Vault secret URI."
  type        = map(string)
  default     = {}
}

variable "app_secret_environment_variables" {
  description = "Environment variables that take their value from app_secrets: map of variable name to secret name."
  type        = map(string)
  default     = {}
}

variable "app_environment_variables" {
  description = "Plain (non-secret) environment variables for the app container."
  type        = map(string)
  default     = {}
}

variable "ingress_allowed_cidrs" {
  description = "IPv4 ranges allowed by the app's ingress IP restrictions, keyed by rule name. Every other source is refused."
  type        = map(string)
}

variable "liveness_probe_path" {
  description = "HTTP path of the liveness and startup probes. It must not depend on the database."
  type        = string
  default     = "/health/live"
}

variable "readiness_probe_path" {
  description = "HTTP path of the readiness probe. It includes the database check."
  type        = string
  default     = "/health/ready"
}

# ---------------------------------------------------------------------------------------------------------------------
# Database bootstrap job
# ---------------------------------------------------------------------------------------------------------------------

variable "db_bootstrap" {
  description = "Manually triggered job that creates the app's PostgreSQL role. Secrets are versionless Key Vault secret URIs."
  type = object({
    job_name                  = string
    image                     = string
    identity_id               = string
    postgresql_host           = string
    database_name             = string
    administrator_login       = string
    administrator_password_id = string
    app_role                  = string
    app_password_id           = string
  })
}
