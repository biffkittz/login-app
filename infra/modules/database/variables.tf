variable "server_name" {
  description = "Globally unique server name (3-63 lowercase letters, digits and hyphens). Clients connect to <server_name>.postgres.database.azure.com."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", var.server_name))
    error_message = "Server names are 3-63 lowercase letters, digits and hyphens, and can't start or end with a hyphen."
  }
}

variable "location" {
  description = "Azure region of the server. Must match the region of the virtual network."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the server and its private DNS zone."
  type        = string
}

variable "postgresql_version" {
  description = "PostgreSQL major version."
  type        = string
}

variable "sku_name" {
  description = "Compute SKU in tier_family_size form, e.g. B_Standard_B1ms (Burstable, 1 vCore, 2 GiB)."
  type        = string
}

variable "storage_mb" {
  description = "Allocated storage in MB."
  type        = number
}

variable "backup_retention_days" {
  description = "Days of point-in-time-restore backups to keep (7-35)."
  type        = number
}

variable "administrator_login" {
  description = "Login name of the server administrator."
  type        = string
}

variable "administrator_password" {
  description = "Password of the server administrator."
  type        = string
  sensitive   = true
}

variable "database_name" {
  description = "Name of the application database."
  type        = string
}

variable "additional_database_names" {
  description = "More databases on the same server, one per additional instance of the app: map of a static key (the instance) to the database name."
  type        = map(string)
  default     = {}
}

variable "delegated_subnet_id" {
  description = "Subnet delegated to Microsoft.DBforPostgreSQL/flexibleServers, where the server gets its private IP."
  type        = string
}

variable "virtual_network_id" {
  description = "Virtual network to link the private DNS zone to, so clients in it resolve the server's private IP."
  type        = string
}

variable "tags" {
  description = "Tags applied to every taggable resource in this module."
  type        = map(string)
}
