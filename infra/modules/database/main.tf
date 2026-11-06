# Azure Database for PostgreSQL flexible server with private access (VNet integration). The server has no public
# endpoint. Clients inside the VNet resolve <server>.postgres.database.azure.com to the server's private IP through
# the private DNS zone below, so the hostname in the connection string matches the server certificate and
# SSL Mode=VerifyFull works.

# For servers created with Terraform, Microsoft asks for a zone ending in .postgres.database.azure.com whose name
# differs from the server's own. "<server>.private.postgres.database.azure.com" satisfies both rules.
resource "azurerm_private_dns_zone" "this" {
  name                = "${var.server_name}.private.postgres.database.azure.com"
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "this" {
  name                 = "link-${var.server_name}"
  private_dns_zone_id  = azurerm_private_dns_zone.this.id
  virtual_network_id   = var.virtual_network_id
  registration_enabled = false
  tags                 = var.tags
}

resource "azurerm_postgresql_flexible_server" "this" {
  name                = var.server_name
  location            = var.location
  resource_group_name = var.resource_group_name
  version             = var.postgresql_version
  sku_name            = var.sku_name
  storage_mb          = var.storage_mb

  backup_retention_days        = var.backup_retention_days
  geo_redundant_backup_enabled = false

  delegated_subnet_id           = var.delegated_subnet_id
  private_dns_zone_id           = azurerm_private_dns_zone.this.id
  public_network_access_enabled = false

  administrator_login    = var.administrator_login
  administrator_password = var.administrator_password

  authentication {
    password_auth_enabled         = true
    active_directory_auth_enabled = false
  }

  tags = var.tags

  # Microsoft recommends linking the zone before the server is created, so it can register its record.
  depends_on = [azurerm_private_dns_zone_virtual_network_link.this]

  lifecycle {
    # Azure picks an availability zone when none is given and reports it afterwards. Ignoring it keeps later plans
    # from trying to move the server (see the azurerm documentation for this resource).
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_database" "this" {
  name      = var.database_name
  server_id = azurerm_postgresql_flexible_server.this.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# Already the default; managing it here records the requirement and reverts manual changes. Clients that don't use
# TLS are refused.
resource "azurerm_postgresql_flexible_server_configuration" "require_secure_transport" {
  name      = "require_secure_transport"
  server_id = azurerm_postgresql_flexible_server.this.id
  value     = "on"
}
