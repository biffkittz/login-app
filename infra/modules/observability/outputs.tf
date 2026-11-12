output "prometheus_url" {
  description = "Prometheus' internal URL, reachable from the environment's own apps only."
  value       = local.prometheus_url
}

output "otlp_metrics_endpoint" {
  description = "Where the app pushes its metrics (Prometheus' OTLP receiver), its Telemetry:OtlpEndpoint."
  value       = "${local.prometheus_url}/api/v1/otlp/v1/metrics"
}

output "prometheus_app_name" {
  description = "Name of Prometheus' Container App."
  value       = azurerm_container_app.prometheus.name
}

output "prometheus_app_id" {
  description = "Resource ID of Prometheus' Container App."
  value       = azurerm_container_app.prometheus.id
}

output "grafana_app_name" {
  description = "Name of Grafana's Container App."
  value       = azurerm_container_app.grafana.name
}

output "grafana_app_id" {
  description = "Resource ID of Grafana's Container App."
  value       = azurerm_container_app.grafana.id
}

output "grafana_url" {
  description = "Grafana's public URL."
  value       = "https://${var.grafana_hostname}"
}

output "github_callback_url" {
  description = "Authorization callback URL to register in the GitHub OAuth App."
  value       = "https://${var.grafana_hostname}/login/github"
}

output "storage_account_name" {
  description = "Name of the storage account that holds the NFS share."
  value       = azurerm_storage_account.this.name
}

output "share_name" {
  description = "Name of the NFS share (directories prometheus/ and grafana/)."
  value       = azurerm_storage_share.this.name
}
