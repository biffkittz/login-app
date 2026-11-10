variable "account_id" {
  description = "Cloudflare account that owns the zone: the recipients become its destination addresses and the token is its own."
  type        = string

  validation {
    condition     = can(regex("^[0-9a-f]{32}$", var.account_id))
    error_message = "account_id must be a Cloudflare account ID: 32 lowercase hexadecimal characters."
  }
}

variable "zone_id" {
  description = "Cloudflare zone that holds domain. Cloudflare Email Service needs the domain's DNS to be on Cloudflare, where it adds the records sending needs."
  type        = string
}

variable "domain" {
  description = "The domain to send from, onboarded for Email Sending: the zone apex (e.g. biffkittz.com) or a name inside it."
  type        = string
}

variable "recipients" {
  description = "Addresses that receive the emails, in lower case. Each becomes a verified destination address of the account once its owner follows the confirmation link Cloudflare sends it."
  type        = list(string)

  validation {
    condition     = length(var.recipients) > 0 && alltrue([for address in var.recipients : address == lower(address)])
    error_message = "recipients must name at least one address, in lower case (as Cloudflare keeps destination addresses)."
  }
}

variable "token_name" {
  description = "Name of the app's API token, as the Cloudflare dashboard lists it."
  type        = string
}

variable "permission_groups" {
  description = "Cloudflare's catalogue of API token permission groups (the cloudflare_account_api_token_permission_groups_list data source's result), from which the token's permission is chosen."
  type = list(object({
    id            = string
    name          = string
    is_selectable = optional(bool)
    scopes        = optional(list(string), [])
  }))
}

variable "preferred_permission_groups" {
  description = "Names of the permission groups that allow sending email, narrowest first. The token gets the first one in permission_groups."
  type        = list(string)

  validation {
    condition     = length(var.preferred_permission_groups) > 0
    error_message = "preferred_permission_groups must name at least one permission group."
  }
}
