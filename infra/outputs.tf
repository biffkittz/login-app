output "app_url" {
  description = "Public URL of the app."
  value       = "https://${var.app_hostname}"
}

output "resource_group_name" {
  description = "Resource group that holds everything except the Container Apps managed infrastructure group."
  value       = azurerm_resource_group.this.name
}

output "application_gateway_public_ip" {
  description = "Public IP of Application Gateway, the target of the Cloudflare DNS record."
  value       = module.application_gateway.public_ip_address
}

output "container_registry_name" {
  description = "Registry name, for az acr build / az acr login."
  value       = module.registry.name
}

output "container_registry_login_server" {
  description = "Registry host name used in image references."
  value       = module.registry.login_server
}

output "app_image" {
  description = "Image the Container App runs, or null until app_image_tag is set."
  value       = local.deploy_app ? "${module.registry.login_server}/${var.app_image_repository}:${var.app_image_tag}" : null
}

output "container_app_name" {
  description = "Name of the Container App (it exists once app_image_tag is set)."
  value       = module.container_apps.app_name
}

output "container_app_private_fqdn" {
  description = "Private FQDN of the app inside the VNet; Application Gateway's backend."
  value       = module.container_apps.app_fqdn
}

output "container_apps_environment_name" {
  description = "Name of the internal Container Apps environment."
  value       = module.container_apps.environment_name
}

output "container_apps_environment_static_ip" {
  description = "Private IP of the environment's internal load balancer."
  value       = module.container_apps.static_ip_address
}

output "db_bootstrap_job_name" {
  description = "Job that creates the app's database role. Run it after the first apply and after rotating the app's database password."
  value       = module.container_apps.db_bootstrap_job_name
}

output "key_vault_name" {
  description = "Name of the Key Vault."
  value       = module.key_vault.name
}

output "key_vault_uri" {
  description = "Data-plane URI of the Key Vault."
  value       = module.key_vault.vault_uri
}

output "postgresql_server_name" {
  description = "Name of the PostgreSQL flexible server."
  value       = module.database.server_name
}

output "postgresql_server_fqdn" {
  description = "Host name of the PostgreSQL server (resolves only inside the VNet)."
  value       = module.database.fqdn
}

output "database_name" {
  description = "Name of the application database."
  value       = module.database.database_name
}

output "virtual_network_name" {
  description = "Name of the virtual network."
  value       = module.networking.virtual_network_name
}

output "next_steps" {
  description = "Commands for the steps that follow an apply (run them from the repository root)."
  value = {
    build_image  = "az acr build --registry ${module.registry.name} --image ${var.app_image_repository}:<tag> --file app/Dockerfile app"
    bootstrap_db = "az containerapp job start --name ${module.container_apps.db_bootstrap_job_name} --resource-group ${azurerm_resource_group.this.name}"
    deploy_app   = "Set app_image_tag = \"<tag>\" in infra/environments/${var.environment}.tfvars, then terraform plan and apply."
  }
}
