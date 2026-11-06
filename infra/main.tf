# Login App on Azure Container Apps.
#
#   visitor --HTTPS--> Cloudflare (proxied DNS, edge TLS) --HTTPS--> Application Gateway (public IP; origin
#   certificate from Key Vault) --HTTP :80, private network--> internal Container Apps environment --> Login App
#   --TLS--> PostgreSQL flexible server (private access, no public endpoint)
#
# The root module names everything, creates the shared pieces (resource group, Log Analytics, managed identities,
# generated secrets, the Cloudflare DNS record) and wires the modules in ./modules together.
# docs/ARCHITECTURE.md explains each design decision.

data "azurerm_client_config" "current" {}

# Cloudflare's published edge ranges. The gateway accepts HTTPS only from these, and the app trusts their
# X-Forwarded-For entries so it can resolve the real client IP.
data "cloudflare_ip_ranges" "this" {}

data "cloudflare_zone" "this" {
  filter = {
    name = var.dns_zone_name
  }
}

# substr(sha256("${data.azurerm_client_config.current.subscription_id}/${local.name}"), 0, 6)
locals {
  name         = "${var.project}-${var.environment}"         # e.g. loginapp-demo
  compact_name = substr(replace(local.name, "-", ""), 0, 13) # for names that allow no hyphens
  suffix       = coalesce(var.name_suffix, substr(sha256("${timestamp()}"), 0, 6)) # TODO: FIX

  # Every taggable resource carries these. "project" is the tag the project requires: project = "loginapp".
  tags = merge(var.tags, {
    project     = var.project
    environment = var.environment
    managed_by  = "terraform"
  })

  # Names follow the Cloud Adoption Framework abbreviations. The registry, Key Vault and PostgreSQL server need
  # globally unique names, so they carry the suffix.
  names = {
    resource_group             = "rg-${local.name}"
    log_analytics              = "log-${local.name}"
    container_registry         = "cr${local.compact_name}${local.suffix}"
    key_vault                  = "kv-${local.compact_name}-${local.suffix}"
    postgresql_server          = "psql-${local.name}-${local.suffix}"
    container_apps_environment = "cae-${local.name}"
    container_apps_managed_rg  = "rg-${local.name}-cae-managed"
    container_app              = "ca-${local.name}"
    db_bootstrap_job           = "caj-${local.name}-dbinit"
    application_gateway        = "agw-${local.name}"
    gateway_public_ip          = "pip-agw-${local.name}"
    identity_app               = "id-${local.name}-app"
    identity_db_bootstrap      = "id-${local.name}-dbinit"
    identity_gateway           = "id-${local.name}-agw"
  }

  cloudflare_ipv4_cidrs = data.cloudflare_ip_ranges.this.ipv4_cidrs

  # No list means the gateway accepts HTTPS from the whole internet.
  gateway_ingress_allowed_cidrs = var.restrict_gateway_to_cloudflare ? concat(local.cloudflare_ipv4_cidrs, var.gateway_additional_allowed_cidrs) : []

  deploy_app = var.app_image_tag != null

  # Npgsql connection string, stored in Key Vault. VerifyFull encrypts the connection and checks the server's
  # certificate and host name against the CA bundle in the runtime image.
  app_connection_string = join(";", [
    "Host=${module.database.fqdn}",
    "Port=5432",
    "Database=${module.database.database_name}",
    "Username=${var.postgresql_app_role}",
    "Password=${random_password.postgresql_app.result}",
    "SSL Mode=VerifyFull",
  ])

  # Network:InternalNetworks in the app lists the proxies whose X-Forwarded-For entries it trusts (it also guards the
  # TCP peer, which in Container Apps is always the platform's own ingress). Requests arrive through Cloudflare's
  # edge, then Application Gateway. Appending Cloudflare's ranges lets the app walk back past the edge to the
  # visitor's real IP, which drives per-client rate limiting. The entries start at index 100 so they come after the
  # defaults in appsettings.json instead of replacing them.
  app_trusted_proxy_settings = {
    for index, cidr in local.cloudflare_ipv4_cidrs : "Network__InternalNetworks__${100 + index}" => cidr
  }
}

resource "azurerm_resource_group" "this" {
  name     = local.names.resource_group
  location = var.location
  tags     = local.tags
}

# Logs from the Container Apps environment, Application Gateway access logs and Key Vault audit events.
resource "azurerm_log_analytics_workspace" "this" {
  name                = local.names.log_analytics
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_analytics_retention_days
  daily_quota_gb      = var.log_analytics_daily_quota_gb
  tags                = local.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# Identities. User-assigned so that their role assignments exist (and have propagated) before the resources that
# use them are created. One identity per consumer keeps each one's access to the minimum.
# ---------------------------------------------------------------------------------------------------------------------

# The Login App: pulls its image and reads its two secrets.
resource "azurerm_user_assigned_identity" "app" {
  name                = local.names.identity_app
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = local.tags
}

# The database bootstrap job: the only identity that can read the PostgreSQL administrator password.
resource "azurerm_user_assigned_identity" "db_bootstrap" {
  name                = local.names.identity_db_bootstrap
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = local.tags
}

# Application Gateway: reads its TLS certificate.
resource "azurerm_user_assigned_identity" "gateway" {
  name                = local.names.identity_gateway
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = local.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# Generated secrets. They are kept (marked sensitive) in the remote state and written to Key Vault, the only place
# workloads read them from. Rotate one with: terraform apply -replace=<address> (see docs/DEPLOYMENT.md).
# ---------------------------------------------------------------------------------------------------------------------

resource "random_password" "postgresql_admin" {
  length      = 32
  min_lower   = 4
  min_upper   = 4
  min_numeric = 4
  min_special = 2
  # Azure needs three character classes. These specials need no quoting in shells, URLs or connection strings.
  override_special = "-_.~"
}

# Alphanumeric only, because it is embedded in an Npgsql connection string.
resource "random_password" "postgresql_app" {
  length  = 40
  special = false
}

# 512-bit HMAC key for signing the app's JWTs (the app requires at least 256 bits, Base64-encoded).
resource "random_bytes" "jwt_signing_key" {
  length = 64
}

# ---------------------------------------------------------------------------------------------------------------------
# Modules
# ---------------------------------------------------------------------------------------------------------------------

module "networking" {
  source = "./modules/networking"

  name                          = local.name
  location                      = azurerm_resource_group.this.location
  resource_group_name           = azurerm_resource_group.this.name
  address_space                 = var.vnet_address_space
  subnet_address_prefixes       = var.subnet_address_prefixes
  gateway_ingress_allowed_cidrs = local.gateway_ingress_allowed_cidrs
  tags                          = local.tags
}

module "registry" {
  source = "./modules/registry"

  name                  = local.names.container_registry
  location              = azurerm_resource_group.this.location
  resource_group_name   = azurerm_resource_group.this.name
  sku                   = var.container_registry_sku
  pull_principal_ids    = { app = azurerm_user_assigned_identity.app.principal_id }
  rbac_propagation_wait = var.rbac_propagation_wait
  tags                  = local.tags
}

module "database" {
  source = "./modules/database"

  server_name            = local.names.postgresql_server
  location               = azurerm_resource_group.this.location
  resource_group_name    = azurerm_resource_group.this.name
  postgresql_version     = var.postgresql_version
  sku_name               = var.postgresql_sku_name
  storage_mb             = var.postgresql_storage_mb
  backup_retention_days  = var.postgresql_backup_retention_days
  administrator_login    = var.postgresql_admin_login
  administrator_password = random_password.postgresql_admin.result
  database_name          = var.database_name
  delegated_subnet_id    = module.networking.subnet_ids.postgresql
  virtual_network_id     = module.networking.virtual_network_id
  tags                   = local.tags
}

module "key_vault" {
  source = "./modules/key-vault"

  name                       = local.names.key_vault
  location                   = azurerm_resource_group.this.location
  resource_group_name        = azurerm_resource_group.this.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  administrator_principal_id = data.azurerm_client_config.current.object_id
  purge_protection_enabled   = var.key_vault_purge_protection_enabled

  firewall_enabled   = var.key_vault_firewall_enabled
  allowed_ip_ranges  = var.key_vault_allowed_ip_ranges
  allowed_subnet_ids = [module.networking.subnet_ids.app_gateway, module.networking.subnet_ids.container_apps]

  # The app reads the first two. Their "--" names map to the configuration keys ConnectionStrings:DefaultConnection
  # and Jwt:SigningKey, which is also what the app's optional built-in Key Vault provider expects.
  secret_names = {
    db_connection_string      = "ConnectionStrings--DefaultConnection"
    jwt_signing_key           = "Jwt--SigningKey"
    postgresql_admin_password = "postgresql-admin-password"
    postgresql_app_password   = "postgresql-app-password"
  }
  secret_values = {
    db_connection_string      = local.app_connection_string
    jwt_signing_key           = random_bytes.jwt_signing_key.base64
    postgresql_admin_password = random_password.postgresql_admin.result
    postgresql_app_password   = random_password.postgresql_app.result
  }
  secret_readers = {
    app_connection_string = { secret = "db_connection_string", principal_id = azurerm_user_assigned_identity.app.principal_id }
    app_jwt_signing_key   = { secret = "jwt_signing_key", principal_id = azurerm_user_assigned_identity.app.principal_id }
    dbinit_admin_password = { secret = "postgresql_admin_password", principal_id = azurerm_user_assigned_identity.db_bootstrap.principal_id }
    dbinit_app_password   = { secret = "postgresql_app_password", principal_id = azurerm_user_assigned_identity.db_bootstrap.principal_id }
  }

  certificate_name                 = "${replace(var.app_hostname, ".", "-")}-origin"
  certificate_pfx_base64           = filebase64(pathexpand(var.tls_certificate_pfx_path))
  certificate_password             = var.tls_certificate_pfx_password
  certificate_reader_principal_ids = { gateway = azurerm_user_assigned_identity.gateway.principal_id }

  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  rbac_propagation_wait      = var.rbac_propagation_wait
  tags                       = local.tags
}

module "container_apps" {
  source = "./modules/container-app"

  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = local.tags

  environment_name                   = local.names.container_apps_environment
  infrastructure_resource_group_name = local.names.container_apps_managed_rg
  infrastructure_subnet_id           = module.networking.subnet_ids.container_apps
  virtual_network_id                 = module.networking.virtual_network_id
  log_analytics_workspace_id         = azurerm_log_analytics_workspace.this.id

  deploy_app       = local.deploy_app
  app_name         = local.names.container_app
  app_image        = local.deploy_app ? "${module.registry.login_server}/${var.app_image_repository}:${var.app_image_tag}" : null
  app_identity_id  = azurerm_user_assigned_identity.app.id
  registry_server  = module.registry.login_server
  app_target_port  = var.app_target_port
  app_cpu          = var.app_cpu
  app_memory       = var.app_memory
  app_min_replicas = var.app_min_replicas
  app_max_replicas = var.app_max_replicas

  app_secrets = {
    db-connection-string = module.key_vault.secret_ids["db_connection_string"]
    jwt-signing-key      = module.key_vault.secret_ids["jwt_signing_key"]
  }
  app_secret_environment_variables = {
    ConnectionStrings__DefaultConnection = "db-connection-string"
    Jwt__SigningKey                      = "jwt-signing-key"
  }
  app_environment_variables = merge(local.app_trusted_proxy_settings, {
    ASPNETCORE_ENVIRONMENT = "Production"
  })

  # Defense in depth on top of the internal environment and the subnet NSG: the ingress itself refuses anything not
  # coming from the gateway subnet (see var.container_apps_transitional_ingress_cidrs for the temporary extra ranges).
  ingress_allowed_cidrs = merge(
    { app-gateway-subnet = var.subnet_address_prefixes.app_gateway },
    { for index, cidr in var.container_apps_transitional_ingress_cidrs : "aca-infrastructure-${index}" => cidr },
  )

  db_bootstrap = {
    job_name                  = local.names.db_bootstrap_job
    image                     = var.db_bootstrap_image
    identity_id               = azurerm_user_assigned_identity.db_bootstrap.id
    postgresql_host           = module.database.fqdn
    database_name             = module.database.database_name
    administrator_login       = module.database.administrator_login
    administrator_password_id = module.key_vault.secret_ids["postgresql_admin_password"]
    app_role                  = var.postgresql_app_role
    app_password_id           = module.key_vault.secret_ids["postgresql_app_password"]
  }
}

module "application_gateway" {
  source = "./modules/application-gateway"

  name                = local.names.application_gateway
  public_ip_name      = local.names.gateway_public_ip
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  subnet_id           = module.networking.subnet_ids.app_gateway
  zones               = var.app_gateway_zones
  autoscale           = var.app_gateway_autoscale

  identity_id                         = azurerm_user_assigned_identity.gateway.id
  ssl_certificate_key_vault_secret_id = module.key_vault.certificate_secret_id

  host_name         = var.app_hostname
  backend_fqdn      = module.container_apps.app_fqdn
  backend_port      = 80
  health_probe_path = "/health/ready"

  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  tags                       = local.tags
}

# ---------------------------------------------------------------------------------------------------------------------
# Public DNS: app.biffkittz.com -> Application Gateway, proxied through Cloudflare (orange cloud), so visitors only
# ever see Cloudflare's edge certificate and IPs.
# ---------------------------------------------------------------------------------------------------------------------

resource "cloudflare_dns_record" "app" {
  zone_id = data.cloudflare_zone.this.id
  name    = var.app_hostname
  type    = "A"
  content = module.application_gateway.public_ip_address
  proxied = true
  ttl     = 1 # "automatic", required for proxied records
  # Comments work on every Cloudflare plan (record tags need a paid plan), so they carry the project tag.
  comment = "Azure Application Gateway ${local.names.application_gateway}; project:${var.project}; managed by Terraform"
}
