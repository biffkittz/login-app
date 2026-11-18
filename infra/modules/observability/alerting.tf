# Grafana's alerting, provisioned from a file like its dashboards: the rules below, an email contact point and the
# notification policy. Grafana evaluates the rules against Prometheus every minute and emails
# grafana_alerting.recipients through SMTP (Cloudflare Email Service by default) when an alert starts firing, every
# 12 hours while it keeps firing, and when it resolves.
#
# Provisioned rules can't be changed in Grafana's UI, and Grafana keeps one after it leaves the file. So removing a rule
# means moving its UID to grafana_retired_alert_rule_uids, and the rules of a group that is off (or all of them, with
# alerting off) are deleted by UID too.

locals {
  grafana_alerting_enabled          = var.grafana_alerting != null
  grafana_prometheus_datasource_uid = "prometheus"
  grafana_alert_contact_point       = "Email"

  # UIDs of rules that were provisioned once and have since been removed from grafana_alert_groups.
  grafana_retired_alert_rule_uids = []

  # The rules, by group. Each rule's PromQL (expr) is query A, evaluated at the moment; condition is applied to its
  # value ($A), and every series that meets it is an alert, which fires once the condition has held for `for`. None
  # fire on missing data: a query that returns nothing means nothing is wrong. If Prometheus can't be queried, each
  # rule fires a DatasourceError alert after its `for` instead, which is how an outage of Prometheus itself shows.
  # Every `for` is at least 5 minutes, so a new Prometheus revision (a minute or two without it) doesn't email.
  #
  # Labels name sites, jobs, apps and Temporal namespaces and task queues only: fixed values, like the metrics'.
  #
  # A summary is expanded inside {{ with $values.A }}, so .Value is query A's value. When the query fails there is no
  # value, and the summary says so instead (formatting a missing value would fail).
  grafana_alert_groups = {
    site = {
      name    = "Site"
      folder  = "Nibbles"
      enabled = true
      rules = {
        nibbles-database-unhealthy = {
          title       = "Database unreachable from the app"
          expr        = "min by (site) (nibbles_health_status{check=\"database\"})"
          condition   = "< 1"
          for         = "5m"
          severity    = "critical"
          summary     = "{{ $labels.site }} can't use its database"
          description = "The app's database health check has failed for 5 minutes, so most requests fail. Check the Azure infrastructure dashboard (Database) and the app's logs."
        }
        # Series pushed over OTLP go stale 5 minutes after their last sample: a site that has reported in the last day
        # but has no current series has stopped pushing. A site removed for good stops firing a day later.
        nibbles-not-reporting = {
          title       = "Site stopped reporting"
          expr        = <<-EOT
            count by (site) (max_over_time(nibbles_health_status[1d]))
              unless
            count by (site) (nibbles_health_status)
          EOT
          condition   = "> 0"
          for         = "5m"
          severity    = "critical"
          summary     = "{{ $labels.site }} has sent no metrics for about 10 minutes"
          description = "The app is down, restarting over and over, or can't reach Prometheus. Check the container app's replicas and logs (Azure infrastructure dashboard: Replicas, Replica restarts)."
        }
        # Cloudflare's requests refused by the app: without a client certificate (403 before routing, event 4000) or
        # with one the app doesn't trust (the TLS handshake fails, event 4001). Only Cloudflare can connect (the
        # ingress admits its ranges alone), so either means Authenticated Origin Pulls is broken and visitors get
        # errors. The sum, or whichever of the two exists.
        nibbles-origin-refusing-cloudflare = {
          title       = "Cloudflare's requests refused"
          expr        = <<-EOT
            (
              sum by (site) (increase(http_server_request_duration_seconds_count{http_response_status_code="403", http_route=""}[10m]))
                +
              sum by (site) (increase(kestrel_tls_handshake_duration_seconds_count{error_type!=""}[10m]))
            )
            or sum by (site) (increase(http_server_request_duration_seconds_count{http_response_status_code="403", http_route=""}[10m]))
            or sum by (site) (increase(kestrel_tls_handshake_duration_seconds_count{error_type!=""}[10m]))
          EOT
          condition   = "> 5"
          for         = "5m"
          severity    = "critical"
          summary     = "{{ $labels.site }} refused about {{ humanize .Value }} of Cloudflare's connections in 10 minutes"
          description = "The app refuses Cloudflare's client certificate, or Cloudflare presents none (log events 4000 and 4001), so visitors get errors. Check the zone's Authenticated Origin Pulls settings: the zone-level switch must stay off (infra/docs/DEPLOYMENT.md)."
        }
      }
    }

    errors = {
      name    = "Errors and slowness"
      folder  = "Nibbles"
      enabled = true
      rules = {
        nibbles-server-errors = {
          title       = "Server errors"
          expr        = "sum by (site) (increase(http_server_request_duration_seconds_count{http_response_status_code=~\"5..\"}[15m]))"
          condition   = ">= 5"
          for         = "5m"
          severity    = "warning"
          summary     = "{{ $labels.site }} answered about {{ humanize .Value }} requests with a server error in 15 minutes"
          description = "Requests are failing with 5xx. The Nibbles service dashboard shows which routes (Responses by status code, Exceptions); the app's logs say why."
        }
        # The p95 across routes, leaving out Estimate Cost, which waits for Gemini by design, and only with enough
        # requests for a 95th percentile to mean something.
        nibbles-slow-responses = {
          title       = "Slow responses"
          expr        = <<-EOT
            histogram_quantile(0.95, sum by (site, le) (rate(http_server_request_duration_seconds_bucket{http_route!~".*/cost-estimate"}[15m])))
              and on (site)
            sum by (site) (increase(http_server_request_duration_seconds_count{http_route!~".*/cost-estimate"}[15m])) >= 20
          EOT
          condition   = "> 2"
          for         = "10m"
          severity    = "warning"
          summary     = "{{ $labels.site }}'s 95th percentile response time is {{ humanizeDuration .Value }}"
          description = "Requests have been slow for 10 minutes. The Nibbles service dashboard shows which routes (p95 latency by route) and whether the database is the cause (Database command duration)."
        }
      }
    }

    infrastructure = {
      name    = "Database and infrastructure"
      folder  = "Nibbles"
      enabled = true
      rules = {
        postgresql-down = {
          title       = "Database server down"
          expr        = "min(azure_postgresql{metric=\"is_db_alive\", aggregation=\"average\"})"
          condition   = "< 1"
          for         = "5m"
          severity    = "critical"
          summary     = "Azure reports the PostgreSQL server down"
          description = "Azure Monitor's is_db_alive has been 0 for 5 minutes. Check the server in the Azure portal (Resource health)."
        }
        postgresql-storage-high = {
          title       = "Database storage nearly full"
          expr        = "max(azure_postgresql{metric=\"storage_percent\", aggregation=\"average\"})"
          condition   = "> 80"
          for         = "30m"
          severity    = "warning"
          summary     = "The PostgreSQL server's storage is {{ humanize .Value }}% full"
          description = "The server turns read-only when its storage is full. Raise postgresql_storage_mb (infra), or find what grew."
        }
        # Burstable servers spend credits above their baseline CPU and are held to it once they run out.
        postgresql-cpu-credits-low = {
          title       = "Database CPU credits nearly gone"
          expr        = "min(azure_postgresql{metric=\"cpu_credits_remaining\", aggregation=\"average\"})"
          condition   = "< 15"
          for         = "15m"
          severity    = "warning"
          summary     = "The PostgreSQL server has {{ humanize .Value }} CPU credits left"
          description = "When they run out, Azure holds the server to its baseline CPU and queries slow down. The Azure infrastructure dashboard shows what has been using it (Database CPU)."
        }
        container-app-memory-high = {
          title       = "Container app memory nearly exhausted"
          expr        = <<-EOT
            max by (app) (label_replace(azure_containerapp{metric="MemoryPercentage", aggregation="average"}, "app", "$1", "resourceID", "(?i).*/containerapps/([^/]+)"))
          EOT
          condition   = "> 90"
          for         = "15m"
          severity    = "warning"
          summary     = "{{ $labels.app }} is using {{ humanize .Value }}% of its memory"
          description = "Close to its limit, the container is killed and restarted. The Azure infrastructure dashboard shows its trend (Memory)."
        }
        # Includes Temporal Cloud's endpoint (job temporal-cloud), whose 401, 403 and 429 answers infra/docs/DEPLOYMENT.md
        # explains.
        prometheus-scrape-failing = {
          title       = "Prometheus can't collect metrics"
          expr        = "min by (job) (up)"
          condition   = "< 1"
          for         = "10m"
          severity    = "warning"
          summary     = "Prometheus has failed to scrape {{ $labels.job }} for 10 minutes"
          description = "That job's metrics, and the alerts that use them, are missing until it recovers. The Azure infrastructure dashboard lists the targets (Scrape targets up); the reason is in Prometheus' targets page (infra/docs/DEPLOYMENT.md, Troubleshooting)."
        }
      }
    }

    grocery-runs = {
      name    = "Grocery runs"
      folder  = "Nibbles"
      enabled = true
      rules = {
        nibbles-temporal-worker-down = {
          title       = "Grocery run worker not polling"
          expr        = "min by (site) (nibbles_health_status{check=\"temporal\"})"
          condition   = "< 1"
          for         = "10m"
          severity    = "warning"
          summary     = "{{ $labels.site }}'s Temporal worker isn't polling Temporal Cloud"
          description = "Grocery runs can't start or move on until it reconnects; the list itself keeps working. Check the app's logs, and Temporal Cloud's status."
        }
        nibbles-temporal-activities-failing = {
          title       = "Grocery run activities failing"
          expr        = "sum by (site, activity_type) (increase(temporal_activity_execution_failed_total[30m]))"
          condition   = ">= 3"
          for         = "5m"
          severity    = "warning"
          summary     = "{{ $labels.activity_type }} failed about {{ humanize .Value }} times in 30 minutes on {{ $labels.site }}"
          description = "Temporal retries failed activities, but repeated failures leave runs stuck or phones out of date. The app's logs say why."
        }
      }
    }

    # Temporal Cloud's own view, every namespace in the account. Its series are per-second rates averaged over each
    # minute, one sample a minute (honor_timestamps), so sum_over_time(...) * 60 counts the events in the window.
    temporal-cloud = {
      name    = "Temporal Cloud"
      folder  = "Temporal Cloud"
      enabled = local.temporal_cloud_enabled
      rules = {
        temporal-cloud-workflows-failing = {
          title       = "Workflows failing or timing out"
          expr        = "sum by (temporal_namespace, temporal_workflow_type) (sum_over_time({__name__=~\"temporal_cloud_v1_workflow_(failed|timeout)_count\"}[15m])) * 60"
          condition   = "> 0"
          for         = "5m"
          severity    = "warning"
          summary     = "About {{ humanize .Value }} {{ $labels.temporal_workflow_type }} workflows failed or timed out in {{ $labels.temporal_namespace }} in 15 minutes"
          description = "Temporal Cloud reports workflows that ended in failure or a timeout. A grocery run should only ever end normally (finished, expired or cancelled). The Temporal Cloud dashboard has the details; Temporal Cloud's UI has each workflow's history."
        }
        # The app's task queues only (nibbles-<instance>): other namespaces' workers may scale to zero on purpose.
        temporal-cloud-tasks-without-poller = {
          title       = "Grocery run tasks with no worker"
          expr        = "sum by (temporal_namespace, temporal_task_queue) (sum_over_time(temporal_cloud_v1_no_poller_tasks_count{temporal_task_queue=~\"nibbles-.+\"}[15m])) * 60"
          condition   = "> 0"
          for         = "5m"
          severity    = "warning"
          summary     = "Tasks reached {{ $labels.temporal_task_queue }} with no worker polling it"
          description = "The instance that owns that task queue isn't polling, so its grocery runs are stuck until it does. Check that its app is running."
        }
      }
    }
  }

  grafana_alert_rule_groups = [
    for key, group in local.grafana_alert_groups : {
      orgId    = 1
      name     = group.name
      folder   = group.folder
      interval = "1m"
      rules = [
        for uid, rule in group.rules : {
          uid       = uid
          title     = rule.title
          condition = "B"
          data = [
            {
              refId             = "A"
              relativeTimeRange = { from = 600, to = 0 }
              datasourceUid     = local.grafana_prometheus_datasource_uid
              model = {
                refId         = "A"
                expr          = trimspace(rule.expr)
                instant       = true
                range         = false
                intervalMs    = 60000
                maxDataPoints = 43200
              }
            },
            {
              refId             = "B"
              relativeTimeRange = { from = 0, to = 0 }
              datasourceUid     = "__expr__"
              model = {
                refId      = "B"
                type       = "math"
                expression = "$A ${rule.condition}"
              }
            },
          ]
          noDataState  = "OK"
          execErrState = "Error"
          for          = rule.for
          annotations = {
            summary     = "{{ with $values.A }}${rule.summary}{{ else }}${rule.title}: Prometheus couldn't be queried{{ end }}"
            description = rule.description
          }
          labels   = { severity = rule.severity }
          isPaused = false
        }
      ]
    } if group.enabled && local.grafana_alerting_enabled
  ]

  grafana_provisioned_alert_rule_uids = flatten([for group in local.grafana_alert_rule_groups : [for rule in group.rules : rule.uid]])
  grafana_deleted_alert_rule_uids = setsubtract(
    concat(flatten([for group in values(local.grafana_alert_groups) : keys(group.rules)]), local.grafana_retired_alert_rule_uids),
    local.grafana_provisioned_alert_rule_uids,
  )

  # The file Grafana's init container writes to provisioning/alerting. Written with alerting off too, when it removes
  # what an earlier configuration provisioned: the rules, the contact point, and the policy (back to Grafana's default).
  grafana_alerting_provisioning = local.grafana_alerting_enabled ? yamlencode({
    apiVersion = 1
    contactPoints = [{
      orgId = 1
      name  = local.grafana_alert_contact_point
      receivers = [{
        uid  = "email"
        type = "email"
        settings = {
          addresses   = join(";", var.grafana_alerting.recipients)
          singleEmail = true
        }
        disableResolveMessage = false
      }]
    }]
    # One email per folder and alert name, gathering the sites or jobs it fires for; reminders twice a day.
    policies = [{
      orgId           = 1
      receiver        = local.grafana_alert_contact_point
      group_by        = ["grafana_folder", "alertname"]
      group_wait      = "30s"
      group_interval  = "5m"
      repeat_interval = "12h"
    }]
    groups      = local.grafana_alert_rule_groups
    deleteRules = [for uid in sort(local.grafana_deleted_alert_rule_uids) : { orgId = 1, uid = uid }]
    }) : yamlencode({
    apiVersion          = 1
    deleteRules         = [for uid in sort(local.grafana_deleted_alert_rule_uids) : { orgId = 1, uid = uid }]
    deleteContactPoints = [{ orgId = 1, uid = "email" }]
    resetPolicies       = [1]
  })

  # Grafana's SMTP settings, with alerting on. Port 465 means TLS from the start (Grafana's mail library decides
  # that by the port), the only way Cloudflare accepts mail. The password, Cloudflare's token, is a secret.
  grafana_smtp_environment = local.grafana_alerting_enabled ? {
    GF_SMTP_ENABLED       = "true"
    GF_SMTP_HOST          = var.grafana_alerting.smtp_host
    GF_SMTP_USER          = var.grafana_alerting.smtp_user
    GF_SMTP_FROM_ADDRESS  = var.grafana_alerting.sender_address
    GF_SMTP_FROM_NAME     = var.grafana_alerting.sender_name
    GF_SMTP_EHLO_IDENTITY = var.grafana_hostname
  } : {}
}
