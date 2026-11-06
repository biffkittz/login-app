# The Container Apps environment, the Login App itself and a one-off job that prepares its database role.
#
# The environment is "internal": its only entry point is an internal load balancer with a private IP in the
# environment's subnet, so nothing on the internet can reach it directly. Application Gateway, in the same virtual
# network, is the single route in.

resource "azurerm_container_app_environment" "this" {
  name                = var.environment_name
  location            = var.location
  resource_group_name = var.resource_group_name

  # Workload profiles (with only the serverless Consumption profile) is the current environment type. Unlike the
  # legacy Consumption-only type it supports a /27 subnet, NSGs on that subnet, user-defined routes and private
  # endpoints.
  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }

  infrastructure_subnet_id           = var.infrastructure_subnet_id
  infrastructure_resource_group_name = var.infrastructure_resource_group_name
  internal_load_balancer_enabled     = true

  # A single replica can't be spread across zones, so zone redundancy would add nothing in this demo.
  zone_redundancy_enabled = false

  logs_destination           = "log-analytics"
  log_analytics_workspace_id = var.log_analytics_workspace_id

  tags = var.tags
}

# An internal environment doesn't publish DNS for its apps. This zone is named after the environment's default
# domain and maps every app name (*) and the domain itself (@) to the internal load balancer, so Application Gateway
# can resolve <app>.<default domain>. This is the setup Microsoft documents for Application Gateway in front of an
# internal environment.
resource "azurerm_private_dns_zone" "environment" {
  name                = azurerm_container_app_environment.this.default_domain
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "environment" {
  name                 = "link-${var.environment_name}"
  private_dns_zone_id  = azurerm_private_dns_zone.environment.id
  virtual_network_id   = var.virtual_network_id
  registration_enabled = false
  tags                 = var.tags
}

resource "azurerm_private_dns_a_record" "wildcard" {
  name                = "*"
  private_dns_zone_id = azurerm_private_dns_zone.environment.id
  ttl                 = 300
  records             = [azurerm_container_app_environment.this.static_ip_address]
  tags                = var.tags
}

resource "azurerm_private_dns_a_record" "apex" {
  name                = "@"
  private_dns_zone_id = azurerm_private_dns_zone.environment.id
  ttl                 = 300
  records             = [azurerm_container_app_environment.this.static_ip_address]
  tags                = var.tags
}

locals {
  app_environment_variables = merge(
    var.app_environment_variables,
    # Kestrel's listening port and the ingress target port always move together.
    { ASPNETCORE_HTTP_PORTS = tostring(var.app_target_port) },
  )
}

# The Login App. It is created only once its image exists in the registry (deploy_app): a Container App's first
# revision fails to provision when its image can't be pulled.
resource "azurerm_container_app" "this" {
  count = var.deploy_app ? 1 : 0

  name                         = var.app_name
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  # A user-assigned identity can be granted AcrPull and Key Vault access before the app exists. A system-assigned
  # one is only created with the app, too late for the first image pull and secret lookup.
  identity {
    type         = "UserAssigned"
    identity_ids = [var.app_identity_id]
  }

  registry {
    server   = var.registry_server
    identity = var.app_identity_id
  }

  # Key Vault references: the platform reads the secret with the app's identity and exposes it as a Container App
  # secret. The URIs carry no version, so a new secret version reaches the app within 30 minutes; Container Apps
  # then restarts the revision so the app re-reads its configuration.
  dynamic "secret" {
    for_each = var.app_secrets

    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = var.app_identity_id
    }
  }

  ingress {
    # In an internal environment "external" means reachable from the virtual network (where Application Gateway
    # lives). Internal would mean reachable only from other apps in this environment.
    external_enabled = true
    target_port      = var.app_target_port
    transport        = "http"
    # TLS terminates at Application Gateway, which forwards plain HTTP on port 80 across the private network.
    # Without this, the ingress would answer that HTTP with a redirect to HTTPS.
    allow_insecure_connections = true

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }

    # Once any allow rule exists, every source not listed is refused.
    dynamic "ip_security_restriction" {
      for_each = var.ingress_allowed_cidrs

      content {
        name             = ip_security_restriction.key
        action           = "Allow"
        ip_address_range = ip_security_restriction.value
      }
    }
  }

  template {
    min_replicas = var.app_min_replicas
    max_replicas = var.app_max_replicas

    container {
      name   = "login-app"
      image  = var.app_image
      cpu    = var.app_cpu
      memory = var.app_memory

      dynamic "env" {
        for_each = local.app_environment_variables

        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = var.app_secret_environment_variables

        content {
          name        = env.key
          secret_name = env.value
        }
      }

      # Startup: up to 3 minutes (36 x 5 s) for the first start, which applies database migrations.
      startup_probe {
        transport               = "HTTP"
        port                    = var.app_target_port
        path                    = var.liveness_probe_path
        interval_seconds        = 5
        timeout                 = 3
        failure_count_threshold = 36
      }

      # Liveness ignores the database on purpose: a database outage must not cause a restart loop.
      liveness_probe {
        transport               = "HTTP"
        port                    = var.app_target_port
        path                    = var.liveness_probe_path
        interval_seconds        = 10
        timeout                 = 3
        failure_count_threshold = 3
      }

      # Readiness includes the database: a replica that can't reach PostgreSQL gets no traffic until it can.
      readiness_probe {
        transport               = "HTTP"
        port                    = var.app_target_port
        path                    = var.readiness_probe_path
        interval_seconds        = 10
        timeout                 = 3
        failure_count_threshold = 3
        success_count_threshold = 1
      }
    }
  }
}

# Creates the app's own PostgreSQL login (see scripts/db-bootstrap.sh) from inside the VNet, where the private server
# is reachable. Run it once after the first apply, and again after rotating the app's database password:
#   az containerapp job start --name <job> --resource-group <group>
# It runs under its own identity, the only one that can read the administrator password, so the app never holds
# server-level credentials.
resource "azurerm_container_app_job" "db_bootstrap" {
  name                         = var.db_bootstrap.job_name
  location                     = var.location
  resource_group_name          = var.resource_group_name
  container_app_environment_id = azurerm_container_app_environment.this.id
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  replica_timeout_in_seconds = 300
  replica_retry_limit        = 1

  manual_trigger_config {
    parallelism              = 1
    replica_completion_count = 1
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [var.db_bootstrap.identity_id]
  }

  secret {
    name                = "postgresql-admin-password"
    key_vault_secret_id = var.db_bootstrap.administrator_password_id
    identity            = var.db_bootstrap.identity_id
  }

  secret {
    name                = "postgresql-app-password"
    key_vault_secret_id = var.db_bootstrap.app_password_id
    identity            = var.db_bootstrap.identity_id
  }

  template {
    container {
      name   = "psql"
      image  = var.db_bootstrap.image
      cpu    = 0.25
      memory = "0.5Gi"

      # Replaces the image's entrypoint, which would otherwise start a PostgreSQL server.
      command = ["/bin/bash", "-c"]
      args    = [file("${path.module}/scripts/db-bootstrap.sh")]

      env {
        name  = "PGHOST"
        value = var.db_bootstrap.postgresql_host
      }
      env {
        name  = "PGPORT"
        value = "5432"
      }
      env {
        name  = "PGDATABASE"
        value = var.db_bootstrap.database_name
      }
      env {
        name  = "PGUSER"
        value = var.db_bootstrap.administrator_login
      }
      env {
        name        = "PGPASSWORD"
        secret_name = "postgresql-admin-password"
      }
      # Encrypt and verify the server's certificate and host name, using the image's system CA bundle.
      env {
        name  = "PGSSLMODE"
        value = "verify-full"
      }
      env {
        name  = "PGSSLROOTCERT"
        value = "/etc/pki/tls/certs/ca-bundle.crt"
      }
      env {
        name  = "PGCONNECT_TIMEOUT"
        value = "30"
      }
      env {
        name  = "PGAPPNAME"
        value = var.db_bootstrap.job_name
      }
      env {
        name  = "APP_DB_ROLE"
        value = var.db_bootstrap.app_role
      }
      env {
        name        = "APP_DB_PASSWORD"
        secret_name = "postgresql-app-password"
      }
    }
  }
}
