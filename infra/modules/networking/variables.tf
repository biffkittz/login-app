variable "name" {
  description = "Base name used in resource names, for example \"loginapp-demo\"."
  type        = string
}

variable "location" {
  description = "Azure region for the virtual network and its network security groups."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the network resources."
  type        = string
}

variable "address_space" {
  description = "Address space of the virtual network, in CIDR notation."
  type        = list(string)
}

variable "subnet_address_prefixes" {
  description = "Address prefix of each dedicated subnet. Each must lie inside address_space and must not overlap the others."
  type = object({
    app_gateway    = string
    container_apps = string
    postgresql     = string
  })
}

variable "gateway_ingress_allowed_cidrs" {
  description = "Source ranges allowed to reach Application Gateway on TCP 443. An empty list allows the whole internet."
  type        = list(string)
  default     = []
}

variable "tags" {
  description = "Tags applied to every taggable resource in this module."
  type        = map(string)
}
