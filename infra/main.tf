# Login App on Azure Container Apps.
#
#   visitor --HTTPS--> Cloudflare (proxied DNS, edge TLS) --HTTPS :443, with Cloudflare's client certificate-->
#   public Container Apps environment (TCP ingress, TLS passed through) --> Login App (Kestrel terminates TLS with
#   the origin certificate from Key Vault) --TLS--> PostgreSQL flexible server (private access, no public endpoint)
#
# The root module names everything, creates the shared pieces (resource group, Log Analytics, managed identities,
# generated secrets, the Cloudflare DNS record) and wires the modules in ./modules together.
# docs/ARCHITECTURE.md explains each design decision.

data "azurerm_client_config" "current" {}

# Cloudflare's published edge ranges. The app's ingress accepts connections only from these, and the app trusts
# their X-Forwarded-For entries so it can resolve the real client IP.
data "cloudflare_ip_ranges" "this" {}

data "cloudflare_zone" "this" {
  filter = {
    name = var.dns_zone_name
  }
}

locals {
  name         = "${var.project}-${var.environment}"         # e.g. loginapp-demo
  compact_name = substr(replace(local.name, "-", ""), 0, 13) # for names that allow no hyphens
  # Deterministic, so every plan computes the same names. (A changing value such as timestamp() would rename, and
  # so replace, the registry, Key Vault and PostgreSQL server on every apply.)
  suffix = coalesce(var.name_suffix, substr(sha256("${data.azurerm_client_config.current.subscription_id}/${local.name}"), 0, 6))

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
    identity_app               = "id-${local.name}-app"
    identity_db_bootstrap      = "id-${local.name}-dbinit"
    # Observability (observability_enabled). The apps are named like additional instances, whose keys can't be
    # "prometheus" or "grafana" (validated).
    storage_account_observability = "st${local.compact_name}${local.suffix}"
    prometheus_app                = "ca-${local.name}-prometheus"
    grafana_app                   = "ca-${local.name}-grafana"
    identity_prometheus           = "id-${local.name}-prometheus"
    identity_grafana              = "id-${local.name}-grafana"
  }

  cloudflare_ipv4_cidrs = data.cloudflare_ip_ranges.this.ipv4_cidrs

  # IP restrictions for the app's public ingress, keyed by rule name. No rules means anyone may connect (and the
  # client-certificate check alone keeps them out). The transitional Container Apps ranges are explained on their
  # variable.
  app_ingress_allowed_cidrs = var.restrict_ingress_to_cloudflare ? merge(
    { for index, cidr in local.cloudflare_ipv4_cidrs : "cloudflare-${index}" => cidr },
    { for index, cidr in var.ingress_additional_allowed_cidrs : "additional-${index}" => cidr },
    { for index, cidr in var.container_apps_transitional_ingress_cidrs : "aca-infrastructure-${index}" => cidr },
  ) : {}

  deploy_app = var.app_image_tag != null

  # Every instance of the app: the main one at app_hostname plus additional_app_instances. They all run the same image
  # with the same identity, Key Vault secrets and settings. Each has its own Container App, host name (DNS record and
  # Origin Rule), exposed port and database, and the Key Vault secret with the connection string to that database.
  app_instances = merge(
    {
      main = {
        hostname                 = var.app_hostname
        exposed_port             = var.app_exposed_port
        database_name            = var.database_name
        app_name                 = local.names.container_app
        connection_string_secret = "db_connection_string"
      }
    },
    {
      for key, instance in var.additional_app_instances : key => merge(instance, {
        app_name                 = "${local.names.container_app}-${key}"
        connection_string_secret = "${key}_db_connection_string"
      })
    },
  )

  # Whether the app gets a Gemini API key, which travels like the other secrets: Key Vault, a Container App secret,
  # then the environment variable ImageGeneration__ApiKey. Only the presence of a key is unwrapped from "sensitive"
  # here, never the key: it decides which of those exist, and for_each refuses sensitive values.
  image_generation_configured = nonsensitive(var.gemini_api_key != null)

  # Registration emails, on when anyone is to receive them. The app sends them through Cloudflare Email Service's REST
  # API with an account token of its own, which travels like the other secrets: Key Vault, a Container App secret,
  # then the environment variable Email__ApiToken. Addresses are kept in lower case, as Cloudflare keeps destination
  # addresses, so a second plan compares like with like.
  registration_notifications_enabled   = length(var.registration_notification_recipients) > 0
  registration_notification_recipients = [for address in var.registration_notification_recipients : lower(address)]
  notification_sender_address          = "${var.notification_sender_username}@${var.dns_zone_name}"
  cloudflare_account_id                = data.cloudflare_zone.this.account.id

  # The settings every instance shares. The recipients become RegistrationNotifications:Recipients:0, :1, ...
  registration_notification_settings = local.registration_notifications_enabled ? merge(
    {
      Email__AccountId     = local.cloudflare_account_id
      Email__SenderAddress = local.notification_sender_address
    },
    { for index, address in local.registration_notification_recipients : "RegistrationNotifications__Recipients__${index}" => address },
  ) : {}

  # Each instance's secrets: Container App secrets that reference Key Vault, the same ones for every instance except the
  # connection string, which names the instance's own database. The environment variables below take their values
  # from them. The certificate's secret holds the PFX (Base64, no password), which the app loads as its HTTPS
  # certificate; it is a *.biffkittz.com certificate, so it serves every instance's host name.
  app_secrets = {
    for key, instance in local.app_instances : key => merge(
      {
        db-connection-string = module.key_vault.secret_ids[instance.connection_string_secret]
        jwt-signing-key      = module.key_vault.secret_ids["jwt_signing_key"]
        origin-certificate   = module.key_vault.certificate_secret_id
      },
      local.image_generation_configured ? { gemini-api-key = module.key_vault.secret_ids["gemini_api_key"] } : {},
      local.registration_notifications_enabled ? { email-api-token = module.key_vault.secret_ids["email_api_token"] } : {},
      local.observability_enabled ? { telemetry-otlp-password = module.key_vault.secret_ids["prometheus_app_password"] } : {},
    )
  }
  app_secret_environment_variables = merge(
    {
      ConnectionStrings__DefaultConnection = "db-connection-string"
      Jwt__SigningKey                      = "jwt-signing-key"
      OriginTls__CertificatePfxBase64      = "origin-certificate"
    },
    local.image_generation_configured ? { ImageGeneration__ApiKey = "gemini-api-key" } : {},
    local.registration_notifications_enabled ? { Email__ApiToken = "email-api-token" } : {},
    local.observability_enabled ? { Telemetry__OtlpPassword = "telemetry-otlp-password" } : {},
  )

  # Settings of each instance's own. The instances share the JWT signing key, so each additional one names itself as
  # its tokens' audience: a session from one instance is then refused by another (their accounts are separate anyway).
  # The main instance keeps the app's default audience, so its existing sessions stay valid. Registration emails name
  # the site that was registered on, since every instance sends them to the same people.
  app_instance_environment_variables = {
    for key, instance in local.app_instances : key => merge(
      key == "main" ? {} : { Jwt__Audience = instance.hostname },
      local.registration_notifications_enabled ? { RegistrationNotifications__SiteUrl = "https://${instance.hostname}" } : {},
      # The site label on every metric, which keeps the instances' series apart in Prometheus.
      local.observability_enabled ? { Telemetry__Site = instance.hostname } : {},
    )
  }

  # Npgsql connection strings, stored in Key Vault, one per instance: the same server, role and password, each naming
  # the instance's own database. VerifyFull encrypts the connection and checks the server's certificate and host name
  # against the CA bundle in the runtime image.
  app_connection_strings = {
    for key, database in merge({ main = module.database.database_name }, module.database.additional_database_names) : key => join(";", [
      "Host=${module.database.fqdn}",
      "Port=5432",
      "Database=${database}",
      "Username=${var.postgresql_app_role}",
      "Password=${random_password.postgresql_app.result}",
      "SSL Mode=VerifyFull",
    ])
  }

  # Network:InternalNetworks in the app lists the proxies whose X-Forwarded-For entries it trusts (it also guards the
  # TCP peer, which in Container Apps is the platform's own ingress proxy). Requests arrive through Cloudflare's
  # edge, which appends the visitor's IP. Should the ingress ever hand over Cloudflare's address as the peer instead
  # of its own, these entries let the app walk back past the edge to the visitor's real IP, which drives per-client
  # rate limiting. They start at index 100 so they come after the defaults in appsettings.json instead of replacing
  # them.
  app_trusted_proxy_settings = {
    for index, cidr in local.cloudflare_ipv4_cidrs : "Network__InternalNetworks__${100 + index}" => cidr
  }

  # Prometheus and Grafana (modules/observability). The app pushes its metrics to Prometheus' OTLP receiver at its
  # internal address, signing in as Prometheus' user "app"; Prometheus' users and their passwords are generated here
  # (Grafana's "grafana", Prometheus' own "prometheus" for scraping itself).
  observability_enabled = var.observability_enabled
  prometheus_users      = local.observability_enabled ? toset(["app", "grafana", "prometheus"]) : toset([])
  app_telemetry_settings = local.observability_enabled ? {
    Telemetry__OtlpEndpoint = "https://${local.names.prometheus_app}.internal.${module.container_apps.default_domain}/api/v1/otlp/v1/metrics"
    Telemetry__OtlpUsername = "app"
  } : {}
}

resource "azurerm_resource_group" "this" {
  name     = local.names.resource_group
  location = var.location
  tags     = local.tags
}

# Logs from the Container Apps environment and Key Vault audit events.
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

# The Login App: pulls its image and reads its secrets and its TLS certificate.
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

# Prometheus (observability_enabled): reads its Key Vault secrets and, for the Azure Monitor exporter, the platform
# metrics of the resource group.
resource "azurerm_user_assigned_identity" "prometheus" {
  count = local.observability_enabled ? 1 : 0

  name                = local.names.identity_prometheus
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = local.tags
}

# Grafana (observability_enabled): reads its Key Vault secrets and the TLS certificate.
resource "azurerm_user_assigned_identity" "grafana" {
  count = local.observability_enabled ? 1 : 0

  name                = local.names.identity_grafana
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

# Passwords of Prometheus' users (observability_enabled). Prometheus stores only their bcrypt hashes (bcrypt_hash
# is computed once, so plans stay stable); each user gets its password from Key Vault. Alphanumeric, because HTTP
# Basic authentication joins user and password with a colon.
resource "random_password" "prometheus_users" {
  for_each = local.prometheus_users

  length  = 40
  special = false
}

# Grafana's secret_key (observability_enabled), which encrypts what Grafana stores, such as the data source's password.
resource "random_password" "grafana_secret_key" {
  count = local.observability_enabled ? 1 : 0

  length  = 40
  special = false
}

# ---------------------------------------------------------------------------------------------------------------------
# Modules
# ---------------------------------------------------------------------------------------------------------------------

module "networking" {
  source = "./modules/networking"

  name                      = local.name
  location                  = azurerm_resource_group.this.location
  resource_group_name       = azurerm_resource_group.this.name
  address_space             = var.vnet_address_space
  subnet_address_prefixes   = var.subnet_address_prefixes
  app_ingress_ports         = concat([for instance in values(local.app_instances) : instance.exposed_port], local.observability_enabled ? [var.grafana_exposed_port] : [])
  app_ingress_allowed_cidrs = var.restrict_ingress_to_cloudflare ? concat(local.cloudflare_ipv4_cidrs, var.ingress_additional_allowed_cidrs) : []
  tags                      = local.tags
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
  # One more database per additional instance of the app.
  additional_database_names = { for key, instance in var.additional_app_instances : key => instance.database_name }
  delegated_subnet_id       = module.networking.subnet_ids.postgresql
  virtual_network_id        = module.networking.virtual_network_id
  tags                      = local.tags
}

module "key_vault" {
  source = "./modules/key-vault"

  name                       = local.names.key_vault
  location                   = azurerm_resource_group.this.location
  resource_group_name        = azurerm_resource_group.this.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  administrator_principal_id = coalesce(var.key_vault_administrator_object_id, data.azurerm_client_config.current.object_id)
  # You and the GitHub deploy identity both run Terraform once CI/CD is set up (docs/CI_CD.md). Both need to read the
  # vault's contents back, so the second gets the same roles here instead of the role moving with whoever ran last.
  additional_administrator_principal_ids = var.key_vault_additional_administrators
  purge_protection_enabled               = var.key_vault_purge_protection_enabled

  firewall_enabled   = var.key_vault_firewall_enabled
  allowed_ip_ranges  = var.key_vault_allowed_ip_ranges
  allowed_subnet_ids = [module.networking.subnet_ids.container_apps]

  # The app reads the first two, the Gemini API key when there is one, and its Cloudflare token for sending email when
  # registration emails are on. Their "--" names map to the configuration keys ConnectionStrings:DefaultConnection,
  # Jwt:SigningKey, ImageGeneration:ApiKey and Email:ApiToken, which is also what the app's optional built-in Key Vault
  # provider expects. Each additional instance of the app gets a connection string of its own,
  # "<instance>-db-connection-string", for its database; every other secret is shared.
  secret_names = merge(
    {
      db_connection_string      = "ConnectionStrings--DefaultConnection"
      jwt_signing_key           = "Jwt--SigningKey"
      postgresql_admin_password = "postgresql-admin-password"
      postgresql_app_password   = "postgresql-app-password"
    },
    { for key in keys(var.additional_app_instances) : "${key}_db_connection_string" => "${key}-db-connection-string" },
    local.image_generation_configured ? { gemini_api_key = "ImageGeneration--ApiKey" } : {},
    local.registration_notifications_enabled ? { email_api_token = "Email--ApiToken" } : {},
    # Observability: the app's password at Prometheus (configuration key Telemetry:OtlpPassword), Prometheus' web
    # configuration and self-scrape password, and Grafana's secrets.
    local.observability_enabled ? {
      prometheus_app_password         = "Telemetry--OtlpPassword"
      prometheus_web_config           = "prometheus-web-config"
      prometheus_self_scrape_password = "prometheus-self-scrape-password"
      prometheus_grafana_password     = "prometheus-grafana-password"
      grafana_github_client_secret    = "grafana-github-client-secret"
      grafana_secret_key              = "grafana-secret-key"
    } : {},
  )
  secret_values = merge(
    {
      db_connection_string      = local.app_connection_strings["main"]
      jwt_signing_key           = random_bytes.jwt_signing_key.base64
      postgresql_admin_password = random_password.postgresql_admin.result
      postgresql_app_password   = random_password.postgresql_app.result
    },
    { for key in keys(var.additional_app_instances) : "${key}_db_connection_string" => local.app_connection_strings[key] },
    local.image_generation_configured ? { gemini_api_key = var.gemini_api_key } : {},
    local.registration_notifications_enabled ? { email_api_token = module.email_sending[0].api_token } : {},
    local.observability_enabled ? {
      prometheus_app_password         = random_password.prometheus_users["app"].result
      prometheus_web_config           = yamlencode({ basic_auth_users = { for user, password in random_password.prometheus_users : user => password.bcrypt_hash } })
      prometheus_self_scrape_password = random_password.prometheus_users["prometheus"].result
      prometheus_grafana_password     = random_password.prometheus_users["grafana"].result
      grafana_github_client_secret    = var.grafana_github_client_secret
      grafana_secret_key              = random_password.grafana_secret_key[0].result
    } : {},
  )
  secret_readers = merge(
    {
      app_connection_string = { secret = "db_connection_string", principal_id = azurerm_user_assigned_identity.app.principal_id }
      app_jwt_signing_key   = { secret = "jwt_signing_key", principal_id = azurerm_user_assigned_identity.app.principal_id }
      dbinit_admin_password = { secret = "postgresql_admin_password", principal_id = azurerm_user_assigned_identity.db_bootstrap.principal_id }
      dbinit_app_password   = { secret = "postgresql_app_password", principal_id = azurerm_user_assigned_identity.db_bootstrap.principal_id }
    },
    {
      for key in keys(var.additional_app_instances) :
      "app_${key}_connection_string" => { secret = "${key}_db_connection_string", principal_id = azurerm_user_assigned_identity.app.principal_id }
    },
    local.image_generation_configured ? { app_gemini_api_key = { secret = "gemini_api_key", principal_id = azurerm_user_assigned_identity.app.principal_id } } : {},
    local.registration_notifications_enabled ? { app_email_api_token = { secret = "email_api_token", principal_id = azurerm_user_assigned_identity.app.principal_id } } : {},
    local.observability_enabled ? {
      app_prometheus_password         = { secret = "prometheus_app_password", principal_id = azurerm_user_assigned_identity.app.principal_id }
      prometheus_web_config           = { secret = "prometheus_web_config", principal_id = azurerm_user_assigned_identity.prometheus[0].principal_id }
      prometheus_self_scrape_password = { secret = "prometheus_self_scrape_password", principal_id = azurerm_user_assigned_identity.prometheus[0].principal_id }
      grafana_prometheus_password     = { secret = "prometheus_grafana_password", principal_id = azurerm_user_assigned_identity.grafana[0].principal_id }
      grafana_github_client_secret    = { secret = "grafana_github_client_secret", principal_id = azurerm_user_assigned_identity.grafana[0].principal_id }
      grafana_secret_key              = { secret = "grafana_secret_key", principal_id = azurerm_user_assigned_identity.grafana[0].principal_id }
    } : {},
  )

  certificate_name       = "${replace(var.app_hostname, ".", "-")}-origin"
  certificate_pfx_base64 = filebase64(pathexpand(var.tls_certificate_pfx_path))
  certificate_password   = var.tls_certificate_pfx_password
  # Grafana serves HTTPS with the same (wildcard) origin certificate as the app.
  certificate_reader_principal_ids = merge(
    { app = azurerm_user_assigned_identity.app.principal_id },
    local.observability_enabled ? { grafana = azurerm_user_assigned_identity.grafana[0].principal_id } : {},
  )

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
  log_analytics_workspace_id         = azurerm_log_analytics_workspace.this.id

  deploy_app       = local.deploy_app
  app_image        = local.deploy_app ? "${module.registry.login_server}/${var.app_image_repository}:${var.app_image_tag}" : null
  app_identity_id  = azurerm_user_assigned_identity.app.id
  registry_server  = module.registry.login_server
  app_target_port  = var.app_target_port
  app_cpu          = var.app_cpu
  app_memory       = var.app_memory
  app_min_replicas = var.app_min_replicas
  app_max_replicas = var.app_max_replicas

  # One Container App per instance; the settings around this map are shared by all of them.
  apps = {
    for key, instance in local.app_instances : key => {
      name                  = instance.app_name
      exposed_port          = instance.exposed_port
      secrets               = local.app_secrets[key]
      environment_variables = local.app_instance_environment_variables[key]
    }
  }
  app_secret_environment_variables = local.app_secret_environment_variables
  app_environment_variables = merge(local.app_trusted_proxy_settings, local.registration_notification_settings, local.app_telemetry_settings, {
    ASPNETCORE_ENVIRONMENT                        = "Production"
    OriginTls__RequireCloudflareClientCertificate = tostring(var.require_cloudflare_client_certificate)
  })

  ingress_allowed_cidrs = local.app_ingress_allowed_cidrs

  db_bootstrap = {
    job_name                  = local.names.db_bootstrap_job
    image                     = var.db_bootstrap_image
    identity_id               = azurerm_user_assigned_identity.db_bootstrap.id
    postgresql_host           = module.database.fqdn
    database_name             = module.database.database_name
    app_databases             = concat([module.database.database_name], values(module.database.additional_database_names))
    administrator_login       = module.database.administrator_login
    administrator_password_id = module.key_vault.secret_ids["postgresql_admin_password"]
    app_role                  = var.postgresql_app_role
    app_password_id           = module.key_vault.secret_ids["postgresql_app_password"]
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Observability: Prometheus and Grafana in the same environment (observability_enabled). The app pushes its metrics to
# Prometheus; Prometheus also collects Azure Monitor's platform metrics; Grafana, at grafana_hostname behind
# Cloudflare, shows them to the GitHub users in grafana_github_admins and grafana_github_viewers.
# ---------------------------------------------------------------------------------------------------------------------

module "observability" {
  source = "./modules/observability"
  count  = local.observability_enabled ? 1 : 0

  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  tags                = local.tags

  subscription_id                          = data.azurerm_client_config.current.subscription_id
  container_app_environment_id             = module.container_apps.environment_id
  container_app_environment_default_domain = module.container_apps.default_domain
  container_apps_subnet_id                 = module.networking.subnet_ids.container_apps
  storage_account_name                     = local.names.storage_account_observability
  images                                   = var.observability_images

  prometheus_app_name = local.names.prometheus_app
  prometheus_identity = {
    id           = azurerm_user_assigned_identity.prometheus[0].id
    principal_id = azurerm_user_assigned_identity.prometheus[0].principal_id
    client_id    = azurerm_user_assigned_identity.prometheus[0].client_id
  }
  prometheus_cpu            = var.prometheus_cpu
  prometheus_memory         = var.prometheus_memory
  prometheus_retention_time = var.prometheus_retention_time
  prometheus_retention_size = var.prometheus_retention_size
  prometheus_secret_ids = {
    web_config           = module.key_vault.secret_ids["prometheus_web_config"]
    self_scrape_password = module.key_vault.secret_ids["prometheus_self_scrape_password"]
  }
  monitored_resource_group_id    = azurerm_resource_group.this.id
  monitored_container_app_ids    = values(module.container_apps.app_ids)
  monitored_postgresql_server_id = module.database.server_id

  grafana_app_name     = local.names.grafana_app
  grafana_identity_id  = azurerm_user_assigned_identity.grafana[0].id
  grafana_cpu          = var.grafana_cpu
  grafana_memory       = var.grafana_memory
  grafana_hostname     = var.grafana_hostname
  grafana_exposed_port = var.grafana_exposed_port
  # The app's own allow list: Cloudflare's ranges (and the transitional Container Apps ranges, see their variable).
  grafana_ingress_allowed_cidrs = local.app_ingress_allowed_cidrs
  grafana_github_client_id      = var.grafana_github_client_id
  grafana_github_admins         = var.grafana_github_admins
  grafana_github_viewers        = var.grafana_github_viewers
  grafana_secret_ids = {
    github_client_secret = module.key_vault.secret_ids["grafana_github_client_secret"]
    secret_key           = module.key_vault.secret_ids["grafana_secret_key"]
    prometheus_password  = module.key_vault.secret_ids["prometheus_grafana_password"]
    origin_certificate   = module.key_vault.certificate_secret_id
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Public DNS: app.biffkittz.com (and each additional instance's host name, e.g. grocery.biffkittz.com) -> the Container
# Apps environment's public IP, proxied through Cloudflare (orange cloud), so visitors only ever see Cloudflare's edge
# certificate and IPs. All instances share the one IP; the Origin Rules below send each host name to its app's port.
# ---------------------------------------------------------------------------------------------------------------------

resource "cloudflare_dns_record" "app" {
  for_each = local.app_instances

  zone_id = data.cloudflare_zone.this.id
  name    = each.value.hostname
  type    = "A"
  content = module.container_apps.static_ip_address
  proxied = true
  ttl     = 1 # "automatic", required for proxied records
  # Comments work on every Cloudflare plan (record tags need a paid plan), so they carry the project tag.
  comment = "Azure Container Apps environment ${local.names.container_apps_environment}; project:${var.project}; managed by Terraform"

  # Turn on the client certificate before traffic moves to an app that refuses requests without it, and send that
  # traffic to the app's port: without the Origin Rule, Cloudflare would knock on 443, the environment's HTTP ingress.
  depends_on = [cloudflare_zone_setting.authenticated_origin_pulls, cloudflare_ruleset.origin_port]
}

# Origin Rules: Cloudflare connects to each instance's exposed port (app_exposed_port for app.biffkittz.com) instead of
# the visitor's port (443). The environment's HTTP ingress owns 80 and 443 on its public IP, so the apps' TCP ingress
# can't take 443 (Azure: "exposedPort already in use"), and two TCP apps can't share a port. Visitors keep using
# https://<host name> on 443. The main instance's rule comes first and keeps its ref, so adding instances leaves it as
# it was.
#
# A zone has one ruleset per phase. Origin Rules created by hand in the dashboard live in that same ruleset; import it
# (terraform import cloudflare_ruleset.origin_port zones/<zone id>/<ruleset id>) and add them here, or the apply fails.
resource "cloudflare_ruleset" "origin_port" {
  zone_id     = data.cloudflare_zone.this.id
  name        = "Origin rules"
  description = "Managed by Terraform (project:${var.project})"
  kind        = "zone"
  phase       = "http_request_origin"

  # Grafana's rule, when there is one, comes last, so turning it on or off leaves the instances' rules as they were.
  rules = concat(
    [
      for key in concat(["main"], keys(var.additional_app_instances)) : {
        ref         = key == "main" ? "${var.project}_${var.environment}_app_origin_port" : "${var.project}_${var.environment}_${key}_origin_port"
        description = "${local.app_instances[key].hostname}: connect to the Container Apps TCP ingress on ${local.app_instances[key].exposed_port}"
        expression  = "http.host eq \"${local.app_instances[key].hostname}\""
        action      = "route"
        action_parameters = {
          origin = {
            port = local.app_instances[key].exposed_port
          }
        }
      }
    ],
    local.observability_enabled ? [
      {
        ref         = "${var.project}_${var.environment}_grafana_origin_port"
        description = "${var.grafana_hostname}: connect to Grafana's TCP ingress on ${var.grafana_exposed_port}"
        expression  = "http.host eq \"${var.grafana_hostname}\""
        action      = "route"
        action_parameters = {
          origin = {
            port = var.grafana_exposed_port
          }
        }
      }
    ] : [],
  )
}

# grafana.biffkittz.com, proxied like the app's host names, to the same public IP; the Origin Rule above sends it to
# Grafana's port.
resource "cloudflare_dns_record" "grafana" {
  count = local.observability_enabled ? 1 : 0

  zone_id = data.cloudflare_zone.this.id
  name    = var.grafana_hostname
  type    = "A"
  content = module.container_apps.static_ip_address
  proxied = true
  ttl     = 1 # "automatic", required for proxied records
  # Cloudflare refuses comments over 100 characters (error 9313); with the longest project and environment names
  # this one stays under 100.
  comment = "Grafana (Azure Container Apps ${local.names.container_apps_environment}); project:${var.project}; managed by Terraform"

  depends_on = [cloudflare_ruleset.origin_port]
}

# The main instance's Container App and DNS record became the "main" entries of maps when instances were added. These
# blocks keep the existing objects (and app.biffkittz.com) in place, instead of replacing them.
moved {
  from = module.container_apps.azurerm_container_app.this[0]
  to   = module.container_apps.azurerm_container_app.this["main"]
}

moved {
  from = cloudflare_dns_record.app
  to   = cloudflare_dns_record.app["main"]
}

# Authenticated Origin Pulls ("global" AOP): Cloudflare presents its origin-pull client certificate on every
# connection to this zone's origins, and the app refuses requests without it. It's a zone-wide switch, but origins
# that don't ask for a client certificate simply never see it, so other records in the zone are unaffected.
#
# The global certificate is shared by all Cloudflare accounts: it proves a request came through Cloudflare, not
# through this zone. Zone-level AOP with your own certificate closes that gap (docs/ARCHITECTURE.md).
resource "cloudflare_zone_setting" "authenticated_origin_pulls" {
  count = var.require_cloudflare_client_certificate ? 1 : 0

  zone_id    = data.cloudflare_zone.this.id
  setting_id = "tls_client_auth"
  value      = "on"
}

# ---------------------------------------------------------------------------------------------------------------------
# Registration emails. Whenever someone registers on any instance, the app emails registration_notification_recipients
# from notifications@biffkittz.com through Cloudflare Email Service's REST API. Nothing here exists while the list of
# recipients is empty. Azure Communication Services Email was passed over: Microsoft is retiring it (September 2028),
# and it would need the domain's own SPF record, which already serves the domain's mail host.
# ---------------------------------------------------------------------------------------------------------------------

# Cloudflare's catalogue of permissions a token can be given, from which the module picks the one that sends email.
data "cloudflare_account_api_token_permission_groups_list" "this" {
  count = local.registration_notifications_enabled ? 1 : 0

  account_id = local.cloudflare_account_id
}

# The zone apex onboarded for Email Sending, the recipients as verified destination addresses, and the app's own
# API token for Email Sending. Its value goes to Key Vault (Email--ApiToken) and from there to the app. Rotate it with:
#   terraform apply -replace='module.email_sending[0].cloudflare_account_token.this'
module "email_sending" {
  source = "./modules/email-sending"
  count  = local.registration_notifications_enabled ? 1 : 0

  account_id                  = local.cloudflare_account_id
  zone_id                     = data.cloudflare_zone.this.id
  domain                      = var.dns_zone_name
  recipients                  = local.registration_notification_recipients
  token_name                  = "${local.name} app: send registration emails (managed by Terraform, project:${var.project})"
  permission_groups           = data.cloudflare_account_api_token_permission_groups_list.this[0].result
  preferred_permission_groups = var.cloudflare_email_sending_permission_groups
}
