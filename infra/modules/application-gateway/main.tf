# The only entry point from the internet. Cloudflare connects to the gateway's public IP over HTTPS, using the
# Cloudflare origin certificate stored in Key Vault. The gateway terminates TLS and forwards plain HTTP on port 80 to
# the internal Container Apps environment across the private network.

resource "azurerm_public_ip" "this" {
  name                = var.public_ip_name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "Standard"
  allocation_method   = "Static"
  ip_version          = "IPv4"
  # Stated explicitly: Azure makes Standard public IPs zone-redundant by default, and leaving this unset would let
  # Terraform and Azure disagree about a setting that forces replacement.
  zones = var.zones
  tags  = var.tags
}

locals {
  gateway_ip_configuration_name  = "gateway-subnet"
  frontend_ip_configuration_name = "public"
  frontend_port_name             = "https"
  ssl_certificate_name           = "origin-certificate"
  listener_name                  = "https-${replace(var.host_name, ".", "-")}"
  backend_pool_name              = "container-app"
  backend_settings_name          = "container-app-http"
  probe_name                     = "container-app-ready"
  routing_rule_name              = "https-to-container-app"
}

resource "azurerm_application_gateway" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  zones               = var.zones
  http2_enabled       = true
  tags                = var.tags

  # Standard_v2: no web application firewall at the gateway; Cloudflare's edge filters traffic first. Moving to
  # WAF_v2 means changing both values and attaching an azurerm_web_application_firewall_policy (firewall_policy_id).
  sku {
    name = "Standard_v2"
    tier = "Standard_v2"
  }

  autoscale_configuration {
    min_capacity = var.autoscale.min_capacity
    max_capacity = var.autoscale.max_capacity
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  gateway_ip_configuration {
    name      = local.gateway_ip_configuration_name
    subnet_id = var.subnet_id
  }

  frontend_ip_configuration {
    name                 = local.frontend_ip_configuration_name
    public_ip_address_id = azurerm_public_ip.this.id
  }

  # HTTPS only. Cloudflare terminates the visitor's connection, redirects plain HTTP itself ("Always Use HTTPS") and
  # always reaches the origin on 443, so no port-80 listener is exposed.
  frontend_port {
    name = local.frontend_port_name
    port = 443
  }

  ssl_certificate {
    name                = local.ssl_certificate_name
    key_vault_secret_id = var.ssl_certificate_key_vault_secret_id
  }

  ssl_policy {
    policy_type = "Predefined"
    policy_name = var.ssl_policy_name
  }

  # Multi-site listener: only requests whose SNI and Host match the public host name are routed to the app.
  http_listener {
    name                           = local.listener_name
    frontend_ip_configuration_name = local.frontend_ip_configuration_name
    frontend_port_name             = local.frontend_port_name
    protocol                       = "Https"
    ssl_certificate_name           = local.ssl_certificate_name
    host_names                     = [var.host_name]
  }

  backend_address_pool {
    name  = local.backend_pool_name
    fqdns = [var.backend_fqdn]
  }

  # Probes go to the same host name as real traffic (picked from the backend settings), so they pass through the
  # Container Apps ingress exactly like a user's request would.
  probe {
    name                                      = local.probe_name
    protocol                                  = "Http"
    path                                      = var.health_probe_path
    interval                                  = 30
    timeout                                   = 30
    unhealthy_threshold                       = 3
    pick_host_name_from_backend_http_settings = true

    match {
      status_code = ["200-399"]
    }
  }

  backend_http_settings {
    name                                = local.backend_settings_name
    protocol                            = "Http"
    port                                = var.backend_port
    cookie_based_affinity               = "Disabled"
    request_timeout                     = 30
    pick_host_name_from_backend_address = true
    probe_name                          = local.probe_name
  }

  request_routing_rule {
    name                       = local.routing_rule_name
    rule_type                  = "Basic"
    priority                   = 100
    http_listener_name         = local.listener_name
    backend_address_pool_name  = local.backend_pool_name
    backend_http_settings_name = local.backend_settings_name
  }
}

# Access logs (client IP, host, status, latency per request) and metrics such as backend health.
resource "azurerm_monitor_diagnostic_setting" "this" {
  name                       = "send-to-log-analytics"
  target_resource_id         = azurerm_application_gateway.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}
