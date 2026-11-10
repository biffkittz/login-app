# Key Vault holds every secret the platform needs (database passwords, the app's connection string, the JWT signing
# key) and the app's TLS certificate. Access is controlled by Azure RBAC, scoped to individual secrets wherever
# possible, so each identity can read only what it uses.

resource "azurerm_key_vault" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  tenant_id           = var.tenant_id
  sku_name            = "standard"

  rbac_authorization_enabled = true
  purge_protection_enabled   = var.purge_protection_enabled
  soft_delete_retention_days = var.soft_delete_retention_days

  # The public endpoint stays on by default and every request still needs an Entra ID token plus an RBAC role.
  # Container Apps isn't a "trusted Microsoft service" for the vault firewall, and Microsoft doesn't document whether
  # its Key Vault references reach the vault through the environment's subnet. A default-deny firewall is therefore
  # opt-in (firewall_enabled): see docs/ARCHITECTURE.md before turning it on.
  network_acls {
    bypass                     = "AzureServices"
    default_action             = var.firewall_enabled ? "Deny" : "Allow"
    ip_rules                   = var.firewall_enabled ? var.allowed_ip_ranges : []
    virtual_network_subnet_ids = var.firewall_enabled ? var.allowed_subnet_ids : []
  }

  tags = var.tags
}

locals {
  administrator_roles = toset(["Key Vault Secrets Officer", "Key Vault Certificates Officer"])
}

# The principal running Terraform manages the vault's contents. RBAC-mode vaults grant no data-plane access to
# Owners or Contributors by default, so this assignment is what lets the next resources create secrets.
resource "azurerm_role_assignment" "administrator" {
  for_each = local.administrator_roles

  scope                = azurerm_key_vault.this.id
  role_definition_name = each.key
  principal_id         = var.administrator_principal_id
}

# Everyone else who runs Terraform against the vault (a CI identity next to you, say) gets the same roles, keyed by a
# stable label so adding or removing one principal doesn't touch the others' assignments.
resource "azurerm_role_assignment" "additional_administrator" {
  for_each = {
    for pair in setproduct(keys(var.additional_administrator_principal_ids), local.administrator_roles) :
    "${pair[0]}: ${pair[1]}" => { principal_id = var.additional_administrator_principal_ids[pair[0]], role = pair[1] }
  }

  scope                = azurerm_key_vault.this.id
  role_definition_name = each.value.role
  principal_id         = each.value.principal_id
}

# New role assignments take a minute or two to take effect. Creating secrets right away would fail with 403.
resource "time_sleep" "administrator_rbac" {
  create_duration = var.rbac_propagation_wait

  triggers = {
    role_assignment_ids = join(",", concat(
      [for assignment in azurerm_role_assignment.administrator : assignment.id],
      [for assignment in azurerm_role_assignment.additional_administrator : assignment.id],
    ))
  }
}

resource "azurerm_key_vault_secret" "this" {
  for_each = var.secret_names

  name         = each.value
  value        = var.secret_values[each.key]
  content_type = "text/plain"
  key_vault_id = azurerm_key_vault.this.id
  tags         = var.tags

  depends_on = [time_sleep.administrator_rbac]
}

# The TLS certificate the app serves to Cloudflare, imported from a PFX file. Changing the file and applying imports
# a new version, which the app picks up on its own (see outputs.tf).
resource "azurerm_key_vault_certificate" "tls" {
  name         = var.certificate_name
  key_vault_id = azurerm_key_vault.this.id
  tags         = var.tags

  certificate {
    contents = var.certificate_pfx_base64
    password = var.certificate_password
  }

  depends_on = [time_sleep.administrator_rbac]
}

# Read access to one secret per assignment (least privilege). For example, the app's identity can read its
# connection string but not the PostgreSQL administrator password.
resource "azurerm_role_assignment" "secret_reader" {
  for_each = var.secret_readers

  scope                = azurerm_key_vault_secret.this[each.value.secret].resource_versionless_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value.principal_id
  principal_type       = "ServicePrincipal"
}

# A Key Vault certificate is served through a secret of the same name, which is what the Container App reads.
resource "azurerm_role_assignment" "certificate_reader" {
  for_each = var.certificate_reader_principal_ids

  scope                = "${azurerm_key_vault.this.id}/secrets/${azurerm_key_vault_certificate.tls.name}"
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value
  principal_type       = "ServicePrincipal"
}

resource "time_sleep" "reader_rbac" {
  create_duration = var.rbac_propagation_wait

  triggers = {
    role_assignment_ids = join(",", concat(
      [for assignment in azurerm_role_assignment.secret_reader : assignment.id],
      [for assignment in azurerm_role_assignment.certificate_reader : assignment.id],
    ))
  }
}

# Audit trail of every data-plane operation (who read or changed which secret, and when).
resource "azurerm_monitor_diagnostic_setting" "this" {
  name                       = "send-to-log-analytics"
  target_resource_id         = azurerm_key_vault.this.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "audit"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}
