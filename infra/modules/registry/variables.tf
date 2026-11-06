variable "name" {
  description = "Globally unique registry name: 5-50 alphanumeric characters."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z0-9]{5,50}$", var.name))
    error_message = "Registry names are 5-50 alphanumeric characters."
  }
}

variable "location" {
  description = "Azure region of the registry."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the registry."
  type        = string
}

variable "sku" {
  description = "Registry SKU. Basic has no network rules or private endpoints; Premium adds both."
  type        = string
  default     = "Basic"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.sku)
    error_message = "sku must be Basic, Standard or Premium."
  }
}

variable "pull_principal_ids" {
  description = "Principals (managed identities) granted AcrPull, keyed by a static name such as \"app\"."
  type        = map(string)
  default     = {}
}

variable "rbac_propagation_wait" {
  description = "How long to wait after creating role assignments before anything relies on them, e.g. \"60s\"."
  type        = string
  default     = "60s"
}

variable "tags" {
  description = "Tags applied to the registry."
  type        = map(string)
}
