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

output "app_fqdns" {
  description = "Each app's platform FQDN (<app name>.<default domain>), keyed like apps and known before the apps exist. With TCP ingress it answers on the app's exposed port; users reach the apps through Cloudflare instead."
  value       = { for key, app in var.apps : key => "${app.name}.${azurerm_container_app_environment.this.default_domain}" }
}

output "app_names" {
  description = "Each app's Container App name, keyed like apps, whether or not it has been deployed yet."
  value       = { for key, app in var.apps : key => app.name }
}

output "app_ids" {
  description = "Resource IDs of the Container Apps, keyed like apps; empty while deploy_app is false."
  value       = { for key, app in azurerm_container_app.this : key => app.id }
}

output "app_latest_revision_names" {
  description = "Names of the apps' latest revisions, keyed like apps; empty while deploy_app is false."
  value       = { for key, app in azurerm_container_app.this : key => app.latest_revision_name }
}

output "db_bootstrap_job_name" {
  description = "Name of the database bootstrap job."
  value       = azurerm_container_app_job.db_bootstrap.name
}
