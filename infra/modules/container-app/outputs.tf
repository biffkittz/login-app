output "environment_id" {
  description = "Resource ID of the Container Apps environment."
  value       = azurerm_container_app_environment.this.id
}

output "environment_name" {
  description = "Name of the Container Apps environment."
  value       = azurerm_container_app_environment.this.name
}

output "default_domain" {
  description = "The environment's default domain. Apps are served at <app name>.<default domain>."
  value       = azurerm_container_app_environment.this.default_domain
}

output "static_ip_address" {
  description = "Public IP of the environment, which Cloudflare's DNS record points to."
  value       = azurerm_container_app_environment.this.static_ip_address
}

output "app_fqdn" {
  description = "The app's platform FQDN (<app name>.<default domain>), known before the app exists. With TCP ingress it answers on app_exposed_port; users reach the app through Cloudflare instead."
  value       = "${var.app_name}.${azurerm_container_app_environment.this.default_domain}"
}

output "app_name" {
  description = "Name of the Container App, whether or not it has been deployed yet."
  value       = var.app_name
}

output "app_id" {
  description = "Resource ID of the Container App, or null while deploy_app is false."
  value       = one(azurerm_container_app.this[*].id)
}

output "app_latest_revision_name" {
  description = "Name of the app's latest revision, or null while deploy_app is false."
  value       = one(azurerm_container_app.this[*].latest_revision_name)
}

output "db_bootstrap_job_name" {
  description = "Name of the database bootstrap job."
  value       = azurerm_container_app_job.db_bootstrap.name
}
