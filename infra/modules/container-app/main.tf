# The Container Apps environment, the Login App itself and a one-off job that prepares its database role.
#
# The environment is "external": it has a public IP, which Cloudflare connects to. The app uses TCP ingress, so the
# platform's proxy passes the TLS connection through without decrypting it and Kestrel terminates TLS in the
# container. Only Cloudflare gets in: the ingress IP restrictions allow Cloudflare's ranges, and the app refuses
# requests that don't carry Cloudflare's client certificate. (An NSG can't do this here: inbound internet traffic
# to an external workload-profiles environment doesn't pass through the subnet.)

resource "azurerm_container_app_environment" "this" {
  name                = var.environment_name
  location            = var.location
  resource_group_name = var.resource_group_name

  # Workload profiles (with only the serverless Consumption profile) is the current environment type. Unlike the
  # legacy Consumption-only type it supports a /27 subnet, NSGs on that subnet, user-defined routes and private
  # endpoints. External TCP ingress needs an environment in a custom virtual network, which this is.
  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }

  infrastructure_subnet_id           = var.infrastructure_subnet_id
  infrastructure_resource_group_name = var.infrastructure_resource_group_name
  # Public. Changing this replaces the environment (and with it the app and the job).
  internal_load_balancer_enabled = false

  # A single replica can't be spread across zones, so zone redundancy would add nothing in this demo.
  zone_redundancy_enabled = false

  logs_destination           = "log-analytics"
  log_analytics_workspace_id = var.log_analytics_workspace_id

  tags = var.tags
}

locals {
  app_environment_variables = merge(
    var.app_environment_variables,
    # Kestrel listens for HTTPS only, on the port the ingress targets. ASPNETCORE_URLS takes precedence over the
    # image's ASPNETCORE_HTTP_PORTS=80, so nothing listens on plain HTTP.
    { ASPNETCORE_URLS = "https://+:${var.app_target_port}" },
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
    # Published on the environment's public IP. TCP rather than HTTP ingress, so the platform passes the encrypted
    # connection through and TLS (with Cloudflare's client certificate) is handled by Kestrel.
    external_enabled = true
    transport        = "tcp"
    exposed_port     = var.app_exposed_port
    target_port      = var.app_target_port

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }

    # Once any allow rule exists, every source not listed is refused. No rules means everyone may connect.
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

      # The probes connect straight to the container over HTTPS (they skip certificate validation) and present no
      # client certificate, which is why the app serves the health endpoints without one.
      # Startup: up to 3 minutes (36 x 5 s) for the first start, which applies database migrations.
      startup_probe {
        transport               = "HTTPS"
        port                    = var.app_target_port
        path                    = var.liveness_probe_path
        interval_seconds        = 5
        timeout                 = 3
        failure_count_threshold = 36
      }

      # Liveness ignores the database on purpose: a database outage must not cause a restart loop.
      liveness_probe {
        transport               = "HTTPS"
        port                    = var.app_target_port
        path                    = var.liveness_probe_path
        interval_seconds        = 10
        timeout                 = 3
        failure_count_threshold = 3
      }

      # Readiness includes the database: a replica that can't reach PostgreSQL gets no traffic until it can.
      readiness_probe {
        transport               = "HTTPS"
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
