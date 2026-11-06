variable "name" {
  description = "Globally unique vault name: 3-24 characters, letters, digits and hyphens, starting with a letter."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$", var.name)) && !strcontains(var.name, "--")
    error_message = "Key Vault names are 3-24 letters, digits and single hyphens, start with a letter and don't end with a hyphen."
  }
}

variable "location" {
  description = "Azure region of the vault."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the vault."
  type        = string
}

variable "tenant_id" {
  description = "Microsoft Entra tenant that authenticates requests to the vault."
  type        = string
}

variable "administrator_principal_id" {
  description = "Object ID of the principal running Terraform. It gets Key Vault Secrets Officer and Certificates Officer so it can manage the vault's contents."
  type        = string
}

variable "purge_protection_enabled" {
  description = "Whether deleted vaults and objects can't be purged before the retention period ends. Can't be turned off once on."
  type        = bool
  default     = true
}

variable "soft_delete_retention_days" {
  description = "Days that deleted vaults and objects stay recoverable (7-90). Can only be set when the vault is created."
  type        = number
  default     = 7

  validation {
    condition     = var.soft_delete_retention_days >= 7 && var.soft_delete_retention_days <= 90
    error_message = "soft_delete_retention_days must be between 7 and 90."
  }
}

variable "firewall_enabled" {
  description = "When true, the vault denies network access except from allowed_ip_ranges, allowed_subnet_ids and trusted Azure services."
  type        = bool
  default     = false
}

variable "allowed_ip_ranges" {
  description = "Public ranges (CIDR, e.g. \"203.0.113.10/32\") allowed through the firewall: the machines that run Terraform."
  type        = list(string)
  default     = []
}

variable "allowed_subnet_ids" {
  description = "Subnets (with the Microsoft.KeyVault service endpoint) allowed through the firewall."
  type        = list(string)
  default     = []
}

variable "secret_names" {
  description = "Secrets to create: map of a static key (used in secret_readers) to the secret's name in the vault."
  type        = map(string)
  default     = {}
}

variable "secret_values" {
  description = "Secret values, keyed like secret_names."
  type        = map(string)
  default     = {}
  sensitive   = true
}

variable "secret_readers" {
  description = "Grants Key Vault Secrets User on a single secret. Map of a static key to the secret's key in secret_names and the principal's object ID."
  type = map(object({
    secret       = string
    principal_id = string
  }))
  default = {}
}

variable "certificate_name" {
  description = "Name of the imported TLS certificate in the vault."
  type        = string
}

variable "certificate_pfx_base64" {
  description = "Base64-encoded PFX (PKCS#12) file with the TLS certificate and its private key."
  type        = string
  sensitive   = true
}

variable "certificate_password" {
  description = "Password of the PFX file, or null when it has none."
  type        = string
  default     = null
  sensitive   = true
}

variable "certificate_reader_principal_ids" {
  description = "Principals granted Key Vault Secrets User on the certificate's secret (Application Gateway), keyed by a static name."
  type        = map(string)
  default     = {}
}

variable "log_analytics_workspace_id" {
  description = "Log Analytics workspace that receives the vault's audit logs and metrics."
  type        = string
}

variable "rbac_propagation_wait" {
  description = "How long to wait after creating role assignments before anything relies on them, e.g. \"60s\"."
  type        = string
  default     = "60s"
}

variable "tags" {
  description = "Tags applied to every taggable resource in this module."
  type        = map(string)
}
