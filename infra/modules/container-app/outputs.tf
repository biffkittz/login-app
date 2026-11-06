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
  description = "Private IP of the environment's internal load balancer."
  value       = azurerm_container_app_environment.this.static_ip_address
}

output "app_fqdn" {
  description = "Private FQDN of the app. Known before the app exists, so Application Gateway can be configured first. Available once its private DNS records exist."
  value       = "${var.app_name}.${azurerm_container_app_environment.this.default_domain}"

  depends_on = [
    azurerm_private_dns_a_record.wildcard,
    azurerm_private_dns_a_record.apex,
    azurerm_private_dns_zone_virtual_network_link.environment,
  ]
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
