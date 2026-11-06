output "id" {
  description = "Resource ID of the registry."
  value       = azurerm_container_registry.this.id
}

output "name" {
  description = "Name of the registry (for az acr commands)."
  value       = azurerm_container_registry.this.name
}

output "login_server" {
  description = "Registry host name, e.g. crloginappdemo1a2b3c.azurecr.io. Available once AcrPull assignments have propagated."
  value       = azurerm_container_registry.this.login_server

  depends_on = [time_sleep.acr_pull_propagation]
}
