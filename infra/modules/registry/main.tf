# Azure Container Registry for the app's images. Pulls authenticate with managed identities (AcrPull), never with the
# admin user, which stays disabled.
#
# The Basic SKU has no firewall rules or private endpoints, so the registry endpoint is public. Every push and pull
# still requires a Microsoft Entra ID token. Moving to Premium adds network rules and private endpoints if that ever
# matters.
resource "azurerm_container_registry" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = var.sku
  admin_enabled       = false
  tags                = var.tags
}

resource "azurerm_role_assignment" "acr_pull" {
  for_each = var.pull_principal_ids

  scope                = azurerm_container_registry.this.id
  role_definition_name = "AcrPull"
  principal_id         = each.value
  # Managed identities are service principals. Saying so skips a directory lookup that can fail for an identity
  # created moments earlier.
  principal_type = "ServicePrincipal"
}

# Role assignments take a minute or two to reach every Azure region. Without this pause, a Container App created in
# the same apply could fail its first image pull with "unauthorized".
resource "time_sleep" "acr_pull_propagation" {
  create_duration = var.rbac_propagation_wait

  triggers = {
    role_assignment_ids = join(",", [for assignment in azurerm_role_assignment.acr_pull : assignment.id])
  }
}
