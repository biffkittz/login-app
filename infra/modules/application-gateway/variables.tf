variable "name" {
  description = "Name of the application gateway."
  type        = string
}

variable "public_ip_name" {
  description = "Name of the gateway's public IP address."
  type        = string
}

variable "location" {
  description = "Azure region of the gateway."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the gateway and its public IP."
  type        = string
}

variable "subnet_id" {
  description = "Dedicated subnet for the gateway."
  type        = string
}

variable "zones" {
  description = "Availability zones for the gateway and its public IP. Leave empty only in regions without zones."
  type        = list(string)
  default     = ["1", "2", "3"]
}

variable "autoscale" {
  description = "Autoscaling bounds in capacity units: min_capacity 0 means no capacity is reserved (billed by use), max_capacity is 2-125."
  type = object({
    min_capacity = number
    max_capacity = number
  })
}

variable "identity_id" {
  description = "User-assigned managed identity used to read the TLS certificate from Key Vault."
  type        = string
}

variable "ssl_certificate_key_vault_secret_id" {
  description = "Versionless Key Vault secret URI of the TLS certificate."
  type        = string
}

variable "ssl_policy_name" {
  description = "Predefined TLS policy for the listener. AppGwSslPolicy20220101S allows TLS 1.2 and 1.3 with strong ciphers only."
  type        = string
  default     = "AppGwSslPolicy20220101S"
}

variable "host_name" {
  description = "Public host name served by the HTTPS listener, e.g. app.biffkittz.com. Requests for other host names get no response from the app."
  type        = string
}

variable "backend_fqdn" {
  description = "Private FQDN of the Container App. It is also sent as the Host header, because the Container Apps ingress routes by host name."
  type        = string
}

variable "backend_port" {
  description = "HTTP port on the Container Apps environment's internal load balancer."
  type        = number
  default     = 80
}

variable "health_probe_path" {
  description = "Path probed on the backend. A 2xx or 3xx answer marks the backend healthy."
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Log Analytics workspace that receives access logs and metrics."
  type        = string
}

variable "tags" {
  description = "Tags applied to the gateway and its public IP."
  type        = map(string)
}
