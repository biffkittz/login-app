output "app_url" {
  description = "Public URL of the app's main instance (app_instances lists them all)."
  value       = "https://${var.app_hostname}"
}

output "resource_group_name" {
  description = "Resource group that holds everything except the Container Apps managed infrastructure group."
  value       = azurerm_resource_group.this.name
}

output "origin_public_ip" {
  description = "Public IP of the Container Apps environment (the origin), the target of the Cloudflare DNS record."
  value       = module.container_apps.static_ip_address
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
  description = "Image every instance's Container App runs, or null until app_image_tag is set."
  value       = local.deploy_app ? "${module.registry.login_server}/${var.app_image_repository}:${var.app_image_tag}" : null
}

output "container_app_name" {
  description = "Name of the main instance's Container App (it exists once app_image_tag is set)."
  value       = module.container_apps.app_names["main"]
}

output "container_app_fqdn" {
  description = "The main instance's platform FQDN. It answers TLS on the exposed port but serves only the health endpoints to anyone without Cloudflare's client certificate."
  value       = module.container_apps.app_fqdns["main"]
}

output "app_instances" {
  description = "Every instance of the app (\"main\" is app_hostname): its public URL, Container App, exposed port and database."
  value = {
    for key, instance in local.app_instances : key => {
      url                = "https://${instance.hostname}"
      container_app_name = module.container_apps.app_names[key]
      exposed_port       = instance.exposed_port
      database_name      = instance.database_name
    }
  }
}

output "container_apps_environment_name" {
  description = "Name of the Container Apps environment."
  value       = module.container_apps.environment_name
}

output "db_bootstrap_job_name" {
  description = "Job that creates the app's database role and lets it into every instance's database. Run it after the first apply, after adding an instance, and after rotating the app's database password."
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
  description = "Name of the main instance's database (app_instances lists every instance's)."
  value       = module.database.database_name
}

output "virtual_network_name" {
  description = "Name of the virtual network."
  value       = module.networking.virtual_network_name
}

output "registration_notifications" {
  description = "Who gets an email when someone registers, and the address it comes from, or null while registration_notification_recipients is empty. Each recipient shows \"verified\" once its owner has followed the link in Cloudflare's confirmation email (as of the last plan or apply; terraform apply -refresh-only updates it)."
  value = local.registration_notifications_enabled ? {
    sender           = local.notification_sender_address
    recipients       = module.email_sending[0].recipients
    cloudflare_token = module.email_sending[0].token_name
    permission       = module.email_sending[0].permission_group
  } : null
}

output "observability" {
  description = "Grafana's URL and the GitHub OAuth App's callback URL, Prometheus' internal URL, and the Container Apps and storage account behind them, or null while observability_enabled is false."
  value = local.observability_enabled ? {
    grafana_url          = module.observability[0].grafana_url
    github_callback_url  = module.observability[0].github_callback_url
    grafana_app_name     = module.observability[0].grafana_app_name
    prometheus_url       = module.observability[0].prometheus_url
    prometheus_app_name  = module.observability[0].prometheus_app_name
    storage_account_name = module.observability[0].storage_account_name
  } : null
}

output "next_steps" {
  description = "Commands for the steps that follow an apply (run them from the repository root)."
  value = merge(
    {
      build_image  = "az acr build --registry ${module.registry.name} --image ${var.app_image_repository}:<tag> --file app/Dockerfile app"
      bootstrap_db = "az containerapp job start --name ${module.container_apps.db_bootstrap_job_name} --resource-group ${azurerm_resource_group.this.name}"
      deploy_app   = "Set app_image_tag = \"<tag>\" in infra/environments/${var.environment}.tfvars, then terraform plan and apply."
    },
    local.registration_notifications_enabled ? {
      confirm_recipients = "Each of ${join(", ", local.registration_notification_recipients)} gets an email from Cloudflare asking to verify the address: follow its link once. terraform output registration_notifications shows who has."
    } : {},
    local.observability_enabled ? {
      open_grafana = "Open https://${var.grafana_hostname} and sign in with GitHub as one of ${join(", ", var.grafana_github_admins)}. The dashboards are in the Nibbles folder."
    } : {},
  )
}
