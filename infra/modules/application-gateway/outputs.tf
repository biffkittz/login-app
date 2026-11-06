output "id" {
  description = "Resource ID of the application gateway."
  value       = azurerm_application_gateway.this.id
}

output "name" {
  description = "Name of the application gateway."
  value       = azurerm_application_gateway.this.name
}

output "public_ip_address" {
  description = "The gateway's static public IPv4 address (the target of the Cloudflare DNS record)."
  value       = azurerm_public_ip.this.ip_address
}
