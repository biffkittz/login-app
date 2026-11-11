# Prometheus and Grafana in the app's Container Apps environment.
#
#   app instances --OTLP over HTTPS (Basic auth)--> Prometheus (internal ingress only) <--PromQL-- Grafana
#   Azure Monitor <--Monitoring Reader-- azure-metrics-exporter (sidecar) <--scrape-- Prometheus
#   visitor --> Cloudflare (grafana.<zone>) --> TCP ingress (Cloudflare's ranges only) --> Grafana (TLS, GitHub login)
#
# Both keep their data on one NFS share of a Premium FileStorage account: Container Apps can't mount Azure Disks, and
# NFS is the POSIX file system among its options (Prometheus' database needs one). docs/ARCHITECTURE.md (infra) has
# the reasoning and the trade-offs.

locals {
  # Internal ingress: reachable from the environment's own apps only, never from the internet.
  prometheus_fqdn          = "${var.prometheus_app_name}.internal.${var.container_app_environment_default_domain}"
  prometheus_url           = "https://${local.prometheus_fqdn}"
  prometheus_port          = 9090
  azure_metrics_port       = 8080
  grafana_port             = 3000
  share_name               = "observability"
  prometheus_data_dir      = "prometheus"
  grafana_data_dir         = "grafana"
  prometheus_config_dir    = "/etc/prometheus/config"
  self_scrape_password_key = "self-scrape-password"

  # ---------------------------------------------------------------------------------------------------------------
  # Prometheus' configuration, rendered here and written to disk by the init container. Container Apps has no config
  # maps, an NFS share can't be written through Azure's API, and an image of our own would need a build pipeline for
  # every change; this way a change goes through the reviewed plan like any other.
  # ---------------------------------------------------------------------------------------------------------------

  # Azure Monitor metrics, read through the exporter for explicit resources (no discovery, so it needs no rights
  # beyond reading these). Platform metrics arrive a minute or more late, so each scrape asks for the last five
  # minutes at one-minute grain and keeps the newest point.
  azure_metric_jobs = {
    containerapp = {
      targets      = distinct(concat(var.monitored_container_app_ids, [local.prometheus_app_id, local.grafana_app_id]))
      metrics      = ["Replicas", "RestartCount", "UsageNanoCores", "WorkingSetBytes", "CpuPercentage", "MemoryPercentage", "RxBytes", "TxBytes"]
      aggregations = ["average", "maximum"]
      interval     = "PT1M"
      timespan     = "PT5M"
      every        = "1m"
    }
    postgresql = {
      targets      = [var.monitored_postgresql_server_id]
      metrics      = ["cpu_percent", "memory_percent", "storage_percent", "active_connections", "is_db_alive", "cpu_credits_remaining"]
      aggregations = ["average", "maximum"]
      interval     = "PT1M"
      timespan     = "PT5M"
      every        = "1m"
    }
    # Failed connections only have a total, and Azure refuses a query that asks a metric for an aggregation it lacks.
    postgresql_connections = {
      targets      = [var.monitored_postgresql_server_id]
      metrics      = ["connections_failed", "connections_succeeded"]
      aggregations = ["total"]
      interval     = "PT1M"
      timespan     = "PT5M"
      every        = "1m"
    }
    # The share's used capacity is reported hourly.
    storage = {
      targets      = ["${azurerm_storage_account.this.id}/fileServices/default"]
      metrics      = ["FileCapacity"]
      aggregations = ["average"]
      interval     = "PT1H"
      timespan     = "PT2H"
      every        = "15m"
    }
  }

  prometheus_config = yamlencode({
    global = {
      scrape_interval     = "30s"
      scrape_timeout      = "10s"
      evaluation_interval = "30s"
    }

    # The app pushes its metrics (Telemetry:OtlpEndpoint in the app). Its resource attributes become labels: job from
    # service.name, instance from service.instance.id (the replica), and site, promoted here, from Telemetry:Site.
    otlp = {
      promote_resource_attributes = ["site"]
      translation_strategy        = "UnderscoreEscapingWithSuffixes"
    }

    # Pushed samples can arrive a little out of order (a retried export, two replicas while a revision changes).
    storage = {
      tsdb = {
        out_of_order_time_window = "30m"
      }
    }

    scrape_configs = concat(
      [
        {
          job_name       = "prometheus"
          static_configs = [{ targets = ["localhost:${local.prometheus_port}"] }]
          basic_auth = {
            username      = "prometheus"
            password_file = "${local.prometheus_config_dir}/${local.self_scrape_password_key}"
          }
        },
        {
          # The exporter's own health: Azure Monitor API errors and rate limits.
          job_name       = "azure-metrics-exporter"
          static_configs = [{ targets = ["localhost:${local.azure_metrics_port}"] }]
        },
      ],
      [
        for key, job in local.azure_metric_jobs : {
          job_name        = "azure-${replace(key, "_", "-")}"
          scrape_interval = job.every
          scrape_timeout  = "50s"
          metrics_path    = "/probe/metrics/resource"
          params = {
            name         = ["azure_${key}"]
            subscription = [var.subscription_id]
            target       = job.targets
            metric       = job.metrics
            aggregation  = job.aggregations
            interval     = [job.interval]
            timespan     = [job.timespan]
          }
          static_configs = [{ targets = ["localhost:${local.azure_metrics_port}"] }]
        }
      ],
    )
  })

  # Resource IDs of the observability apps themselves, built from their names so the configuration can list them
  # without depending on the apps it configures.
  resource_group_id = "/subscriptions/${var.subscription_id}/resourceGroups/${var.resource_group_name}"
  prometheus_app_id = "${local.resource_group_id}/providers/Microsoft.App/containerApps/${var.prometheus_app_name}"
  grafana_app_id    = "${local.resource_group_id}/providers/Microsoft.App/containerApps/${var.grafana_app_name}"

  # Container Apps starts a new revision's replica before it stops the old one, so for a minute two Prometheus
  # processes would open the same database on the share, which corrupts it. Prometheus locks its data directory
  # (<data>/lock); this waits for the old replica to let go before starting. The probes therefore watch the exporter's
  # port, not Prometheus': a new replica waiting for the lock must count as ready, or the platform would keep the old
  # one running and the two would wait on each other forever.
  prometheus_start_script = <<-EOT
    until flock -n /prometheus/lock true; do
      echo "Waiting for the previous Prometheus replica to release /prometheus/lock"
      sleep 5
    done
    exec /bin/prometheus "$@"
  EOT

  prometheus_flags = [
    "--config.file=${local.prometheus_config_dir}/prometheus.yml",
    "--web.config.file=${local.prometheus_config_dir}/web.yml",
    "--web.listen-address=:${local.prometheus_port}",
    "--web.enable-otlp-receiver",
    "--storage.tsdb.path=/prometheus",
    "--storage.tsdb.retention.time=${var.prometheus_retention_time}",
    "--storage.tsdb.retention.size=${var.prometheus_retention_size}",
  ]

  # ---------------------------------------------------------------------------------------------------------------
  # Grafana's provisioning: the Prometheus data source and the dashboards in ./dashboards, written to disk by the
  # init container. Grafana treats them as code: they can't be changed or deleted in its UI.
  # ---------------------------------------------------------------------------------------------------------------

  grafana_datasources = yamlencode({
    apiVersion = 1
    datasources = [{
      name          = "Prometheus"
      uid           = "prometheus"
      type          = "prometheus"
      access        = "proxy"
      url           = local.prometheus_url
      isDefault     = true
      editable      = false
      basicAuth     = true
      basicAuthUser = "grafana"
      jsonData = {
        httpMethod     = "POST"
        prometheusType = "Prometheus"
        # The app pushes every 15 seconds, so $__rate_interval covers at least four samples.
        timeInterval = "15s"
      }
      # Grafana expands the variable when it reads the file, so the password is only ever in its environment.
      secureJsonData = {
        basicAuthPassword = "$${PROMETHEUS_PASSWORD}"
      }
    }]
  })

  grafana_dashboards_dir = "/etc/grafana/provisioning/dashboards-json"

  grafana_dashboard_providers = yamlencode({
    apiVersion = 1
    providers = [{
      name                  = "nibbles"
      orgId                 = 1
      folder                = "Nibbles"
      folderUid             = "nibbles"
      type                  = "file"
      disableDeletion       = true
      allowUiUpdates        = false
      updateIntervalSeconds = 3600
      options = {
        path                      = local.grafana_dashboards_dir
        foldersFromFilesStructure = false
      }
    }]
  })

  # One environment variable per dashboard: GRAFANA_DASHBOARD_JSON_<NAME> holds ./dashboards/<name>.json, minified
  # (which also checks that it is JSON) and compressed, so a dozen panels stay a few kilobytes in the app's definition.
  grafana_dashboards = {
    for file in fileset("${path.module}/dashboards", "*.json") :
    "GRAFANA_DASHBOARD_JSON_${upper(replace(trimsuffix(file, ".json"), "-", "_"))}" => base64gzip(jsonencode(jsondecode(file("${path.module}/dashboards/${file}"))))
  }

  # GitHub decides who may sign in: listed administrators, then listed viewers, and nobody else (with
  # role_attribute_strict, an empty role refuses the sign-in). The lists are JSON literals in the JMESPath expression.
  grafana_role_expression = join(" || ", concat(
    ["contains(`${jsonencode(var.grafana_github_admins)}`, login) && 'GrafanaAdmin'"],
    length(var.grafana_github_viewers) > 0 ? ["contains(`${jsonencode(var.grafana_github_viewers)}`, login) && 'Viewer'"] : [],
    ["''"],
  ))

  grafana_environment = {
    # HTTPS with the Cloudflare origin certificate, which the init container turns into the PEM files Grafana reads.
    GF_SERVER_PROTOCOL        = "https"
    GF_SERVER_HTTP_PORT       = tostring(local.grafana_port)
    GF_SERVER_CERT_FILE       = "/etc/grafana/tls/origin.crt"
    GF_SERVER_CERT_KEY        = "/etc/grafana/tls/origin.key"
    GF_SERVER_MIN_TLS_VERSION = "TLS1.2"
    GF_SERVER_DOMAIN          = var.grafana_hostname
    GF_SERVER_ROOT_URL        = "https://${var.grafana_hostname}/"
    GF_SERVER_ENABLE_GZIP     = "true"

    # SQLite on the NFS share: Grafana keeps users, sessions and preferences; dashboards and the data source come
    # from provisioning. One replica writes it.
    GF_DATABASE_TYPE = "sqlite3"

    # GitHub is the only way in: no login form, no basic authentication, no initial admin user with a password.
    GF_SECURITY_DISABLE_INITIAL_ADMIN_CREATION       = "true"
    GF_AUTH_DISABLE_LOGIN_FORM                       = "true"
    GF_AUTH_BASIC_ENABLED                            = "false"
    GF_AUTH_OAUTH_AUTO_LOGIN                         = "true"
    GF_AUTH_LOGIN_MAXIMUM_INACTIVE_LIFETIME_DURATION = "2d"
    GF_AUTH_LOGIN_MAXIMUM_LIFETIME_DURATION          = "7d"
    GF_AUTH_GITHUB_ENABLED                           = "true"
    GF_AUTH_GITHUB_CLIENT_ID                         = var.grafana_github_client_id
    # Grafana reads the user's teams at every sign-in and refuses it if it can't (GET /user/teams), and GitHub lets an
    # OAuth App token do that only with read:org (or broader scopes). user:email reads a private email address.
    GF_AUTH_GITHUB_SCOPES                     = "user:email,read:org"
    GF_AUTH_GITHUB_ALLOW_SIGN_UP              = "true"
    GF_AUTH_GITHUB_ROLE_ATTRIBUTE_PATH        = local.grafana_role_expression
    GF_AUTH_GITHUB_ROLE_ATTRIBUTE_STRICT      = "true"
    GF_AUTH_GITHUB_ALLOW_ASSIGN_GRAFANA_ADMIN = "true"
    GF_USERS_ALLOW_SIGN_UP                    = "false"
    GF_USERS_ALLOW_ORG_CREATE                 = "false"

    GF_SECURITY_COOKIE_SECURE             = "true"
    GF_SECURITY_COOKIE_SAMESITE           = "lax" # the OAuth redirect back from GitHub needs lax, not strict
    GF_SECURITY_STRICT_TRANSPORT_SECURITY = "true"
    GF_SECURITY_CONTENT_SECURITY_POLICY   = "true"
    GF_SECURITY_DISABLE_GRAVATAR          = "true" # would send a hash of each user's email address to Gravatar

    # Nothing leaves for Grafana Labs or the internet at large, and nothing public is served.
    GF_ANALYTICS_REPORTING_ENABLED        = "false"
    GF_ANALYTICS_CHECK_FOR_UPDATES        = "false"
    GF_ANALYTICS_CHECK_FOR_PLUGIN_UPDATES = "false"
    GF_PLUGINS_PREINSTALL_DISABLED        = "true"
    GF_NEWS_NEWS_FEED_ENABLED             = "false"
    GF_SNAPSHOTS_EXTERNAL_ENABLED         = "false"
    GF_PUBLIC_DASHBOARDS_ENABLED          = "false"
    GF_METRICS_ENABLED                    = "false" # /metrics would be public through Cloudflare

    GF_LOG_MODE = "console"
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Storage: an NFS share on a Premium FileStorage account
# ---------------------------------------------------------------------------------------------------------------------

resource "azurerm_storage_account" "this" {
  name                     = var.storage_account_name
  resource_group_name      = var.resource_group_name
  location                 = var.location
  account_kind             = "FileStorage"
  account_tier             = "Premium"
  account_replication_type = "LRS"
  tags                     = var.tags

  # NFS has no keys or Entra ID sign-in: the network decides who mounts. So the account takes no shared keys at all
  # and admits only the Container Apps subnet (through its service endpoint).
  shared_access_key_enabled        = false
  public_network_access            = "Enabled" # "selected networks": the rules below deny everything else
  allow_nested_items_to_be_public  = false
  cross_tenant_replication_enabled = false
  min_tls_version                  = "TLS1_2"

  # Container Apps can't encrypt NFS in transit, and Azure refuses its mount while "secure transfer required" is on
  # (the NFS-only setting that would override it isn't in the azurerm provider). The traffic stays on the subnet's
  # service endpoint over Azure's backbone, and nothing else may reach the account.
  https_traffic_only_enabled = false

  network_rules {
    default_action             = "Deny"
    bypass                     = ["None"]
    virtual_network_subnet_ids = [var.container_apps_subnet_id]
  }
}

resource "azurerm_storage_share" "this" {
  name               = local.share_name
  storage_account_id = azurerm_storage_account.this.id
  enabled_protocol   = "NFS"
  quota              = var.share_quota_gib
}

# The share as the environment mounts it. For NFS, Container Apps wants the share as /<account>/<share>.
resource "azurerm_container_app_environment_storage" "this" {
  name                         = "observability-nfs"
  container_app_environment_id = var.container_app_environment_id
  nfs_server_url               = "${azurerm_storage_account.this.name}.file.core.windows.net"
  share_name                   = "/${azurerm_storage_account.this.name}/${azurerm_storage_share.this.name}"
  access_mode                  = "ReadWrite"
}

# Read access to the platform metrics of everything in the app's resource group, for the Azure Monitor exporter.
resource "azurerm_role_assignment" "metrics_reader" {
  scope                = var.monitored_resource_group_id
  role_definition_name = "Monitoring Reader"
  principal_id         = var.prometheus_identity.principal_id
  principal_type       = "ServicePrincipal"
}

# ---------------------------------------------------------------------------------------------------------------------
# Prometheus
# ---------------------------------------------------------------------------------------------------------------------

resource "azurerm_container_app" "prometheus" {
  name                         = var.prometheus_app_name
  container_app_environment_id = var.container_app_environment_id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.prometheus_identity.id]
  }

  secret {
    name                = "web-config"
    key_vault_secret_id = var.prometheus_secret_ids.web_config
    identity            = var.prometheus_identity.id
  }

  secret {
    name                = local.self_scrape_password_key
    key_vault_secret_id = var.prometheus_secret_ids.self_scrape_password
    identity            = var.prometheus_identity.id
  }

  # Internal only: the app pushes and Grafana queries from inside the environment, over HTTPS with the platform's
  # certificate. Every request also needs one of Prometheus' users (web.yml).
  ingress {
    external_enabled           = false
    transport                  = "http"
    target_port                = local.prometheus_port
    allow_insecure_connections = false

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # One writer for the database, always running: the app pushes to it every 15 seconds.
    min_replicas                     = 1
    max_replicas                     = 1
    termination_grace_period_seconds = 60

    init_container {
      name    = "prepare"
      image   = var.images.init
      cpu     = 0.25
      memory  = "0.5Gi"
      command = ["/bin/bash", "-c"]
      args    = [file("${path.module}/scripts/prometheus-init.sh")]

      env {
        name  = "PROMETHEUS_CONFIG"
        value = local.prometheus_config
      }
      env {
        name        = "PROMETHEUS_WEB_CONFIG"
        secret_name = "web-config"
      }
      env {
        name        = "PROMETHEUS_SELF_SCRAPE_PASSWORD"
        secret_name = local.self_scrape_password_key
      }
      env {
        name  = "PROMETHEUS_DATA_DIR"
        value = local.prometheus_data_dir
      }

      volume_mounts {
        name = "config"
        path = "/config"
      }
      volume_mounts {
        name = "data"
        path = "/data"
      }
    }

    # The main container: Container Apps adds default probes on the ingress port to the first container for every
    # probe type it doesn't define, so all three are defined (see prometheus_start_script for why they watch the
    # exporter's port).
    container {
      name    = "prometheus"
      image   = var.images.prometheus
      cpu     = var.prometheus_cpu
      memory  = var.prometheus_memory
      command = ["/bin/sh", "-c"]
      args    = concat([local.prometheus_start_script, "prometheus"], local.prometheus_flags)

      volume_mounts {
        name = "config"
        path = local.prometheus_config_dir
      }
      volume_mounts {
        name     = "data"
        path     = "/prometheus"
        sub_path = local.prometheus_data_dir
      }

      startup_probe {
        transport               = "TCP"
        port                    = local.azure_metrics_port
        interval_seconds        = 5
        failure_count_threshold = 24
      }

      liveness_probe {
        transport               = "TCP"
        port                    = local.azure_metrics_port
        interval_seconds        = 30
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "TCP"
        port                    = local.azure_metrics_port
        interval_seconds        = 10
        failure_count_threshold = 3
        success_count_threshold = 1
      }
    }

    # Reads Azure Monitor with Prometheus' identity (DefaultAzureCredential: the user-assigned identity named by
    # AZURE_CLIENT_ID) and serves the metrics to Prometheus on localhost. It has no ingress of its own.
    container {
      name   = "azure-metrics-exporter"
      image  = var.images.azure_metrics_exporter
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "AZURE_CLIENT_ID"
        value = var.prometheus_identity.client_id
      }
      env {
        name  = "SERVER_BIND"
        value = ":${local.azure_metrics_port}"
      }

      liveness_probe {
        transport               = "TCP"
        port                    = local.azure_metrics_port
        interval_seconds        = 30
        failure_count_threshold = 3
      }
    }

    volume {
      name         = "config"
      storage_type = "EmptyDir"
    }

    volume {
      name         = "data"
      storage_type = "NfsAzureFile"
      storage_name = azurerm_container_app_environment_storage.this.name
    }
  }

  depends_on = [azurerm_role_assignment.metrics_reader]
}

# ---------------------------------------------------------------------------------------------------------------------
# Grafana
# ---------------------------------------------------------------------------------------------------------------------

resource "azurerm_container_app" "grafana" {
  name                         = var.grafana_app_name
  container_app_environment_id = var.container_app_environment_id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.grafana_identity_id]
  }

  secret {
    name                = "github-client-secret"
    key_vault_secret_id = var.grafana_secret_ids.github_client_secret
    identity            = var.grafana_identity_id
  }

  secret {
    name                = "secret-key"
    key_vault_secret_id = var.grafana_secret_ids.secret_key
    identity            = var.grafana_identity_id
  }

  secret {
    name                = "prometheus-password"
    key_vault_secret_id = var.grafana_secret_ids.prometheus_password
    identity            = var.grafana_identity_id
  }

  secret {
    name                = "origin-certificate"
    key_vault_secret_id = var.grafana_secret_ids.origin_certificate
    identity            = var.grafana_identity_id
  }

  # Like the app: TCP ingress passes TLS through to Grafana, on a port of its own that Cloudflare's Origin Rule names.
  ingress {
    external_enabled = true
    transport        = "tcp"
    exposed_port     = var.grafana_exposed_port
    target_port      = local.grafana_port

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }

    # Once any allow rule exists, every source not listed is refused. No rules means everyone may connect.
    dynamic "ip_security_restriction" {
      for_each = var.grafana_ingress_allowed_cidrs

      content {
        name             = ip_security_restriction.key
        action           = "Allow"
        ip_address_range = ip_security_restriction.value
      }
    }
  }

  template {
    min_replicas = 1
    max_replicas = 1

    init_container {
      name    = "prepare"
      image   = var.images.init
      cpu     = 0.25
      memory  = "0.5Gi"
      command = ["/bin/bash", "-c"]
      args    = [file("${path.module}/scripts/grafana-init.sh")]

      env {
        name        = "ORIGIN_CERTIFICATE_PFX_BASE64"
        secret_name = "origin-certificate"
      }
      env {
        name  = "GRAFANA_DATASOURCES"
        value = local.grafana_datasources
      }
      env {
        name  = "GRAFANA_DASHBOARD_PROVIDERS"
        value = local.grafana_dashboard_providers
      }
      env {
        name  = "GRAFANA_DATA_DIR"
        value = local.grafana_data_dir
      }

      dynamic "env" {
        for_each = local.grafana_dashboards

        content {
          name  = env.key
          value = env.value
        }
      }

      volume_mounts {
        name = "tls"
        path = "/tls"
      }
      volume_mounts {
        name = "provisioning"
        path = "/provisioning"
      }
      volume_mounts {
        name = "data"
        path = "/data"
      }
    }

    container {
      name   = "grafana"
      image  = var.images.grafana
      cpu    = var.grafana_cpu
      memory = var.grafana_memory

      dynamic "env" {
        for_each = local.grafana_environment

        content {
          name  = env.key
          value = env.value
        }
      }

      env {
        name        = "GF_AUTH_GITHUB_CLIENT_SECRET"
        secret_name = "github-client-secret"
      }
      env {
        name        = "GF_SECURITY_SECRET_KEY"
        secret_name = "secret-key"
      }
      env {
        name        = "PROMETHEUS_PASSWORD"
        secret_name = "prometheus-password"
      }

      volume_mounts {
        name = "tls"
        path = "/etc/grafana/tls"
      }
      volume_mounts {
        name = "provisioning"
        path = "/etc/grafana/provisioning"
      }
      volume_mounts {
        name     = "data"
        path     = "/var/lib/grafana"
        sub_path = local.grafana_data_dir
      }

      # /api/health answers without signing in. The probes skip certificate validation.
      startup_probe {
        transport               = "HTTPS"
        port                    = local.grafana_port
        path                    = "/api/health"
        interval_seconds        = 5
        timeout                 = 3
        failure_count_threshold = 36
      }

      liveness_probe {
        transport               = "HTTPS"
        port                    = local.grafana_port
        path                    = "/api/health"
        interval_seconds        = 30
        timeout                 = 5
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTPS"
        port                    = local.grafana_port
        path                    = "/api/health"
        interval_seconds        = 10
        timeout                 = 3
        failure_count_threshold = 3
        success_count_threshold = 1
      }
    }

    volume {
      name         = "tls"
      storage_type = "EmptyDir"
    }

    volume {
      name         = "provisioning"
      storage_type = "EmptyDir"
    }

    volume {
      name         = "data"
      storage_type = "NfsAzureFile"
      storage_name = azurerm_container_app_environment_storage.this.name
    }
  }
}
