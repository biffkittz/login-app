output "server_id" {
  description = "Resource ID of the server."
  value       = azurerm_postgresql_flexible_server.this.id
}

output "server_name" {
  description = "Name of the server."
  value       = azurerm_postgresql_flexible_server.this.name
}

output "fqdn" {
  description = "Host name for connection strings. It resolves to the private IP inside the linked virtual network."
  value       = azurerm_postgresql_flexible_server.this.fqdn
}

output "database_name" {
  description = "Name of the application database."
  value       = azurerm_postgresql_flexible_server_database.this.name
}

output "administrator_login" {
  description = "Login name of the server administrator."
  value       = azurerm_postgresql_flexible_server.this.administrator_login
}
