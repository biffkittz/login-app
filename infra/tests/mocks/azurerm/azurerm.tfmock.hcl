# Realistic values for computed attributes of mocked azurerm resources. The provider still validates arguments
# during a mocked apply, for example that a role assignment scope or a subnet ID is a well-formed Azure resource ID.
# Random mock strings would fail those checks, so each type gets a value in its real format.
# Every instance of a type shares these values, which is fine for tests.

mock_data "azurerm_client_config" {
  defaults = {
    subscription_id = "00000000-0000-0000-0000-000000000000"
    tenant_id       = "11111111-1111-1111-1111-111111111111"
    object_id       = "22222222-2222-2222-2222-222222222222"
    client_id       = "33333333-3333-3333-3333-333333333333"
  }
}

mock_resource "azurerm_resource_group" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock"
  }
}

mock_resource "azurerm_log_analytics_workspace" {
  defaults = {
    id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.OperationalInsights/workspaces/log-mock"
    workspace_id = "66666666-6666-6666-6666-666666666666"
  }
}

mock_resource "azurerm_user_assigned_identity" {
  defaults = {
    id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-mock"
    principal_id = "44444444-4444-4444-4444-444444444444"
    client_id    = "55555555-5555-5555-5555-555555555555"
    tenant_id    = "11111111-1111-1111-1111-111111111111"
  }
}

mock_resource "azurerm_virtual_network" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.Network/virtualNetworks/vnet-mock"
  }
}

mock_resource "azurerm_subnet" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.Network/virtualNetworks/vnet-mock/subnets/snet-mock"
  }
}

mock_resource "azurerm_network_security_group" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.Network/networkSecurityGroups/nsg-mock"
  }
}

mock_resource "azurerm_network_security_rule" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.Network/networkSecurityGroups/nsg-mock/securityRules/rule-mock"
  }
}

mock_resource "azurerm_subnet_network_security_group_association" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.Network/virtualNetworks/vnet-mock/subnets/snet-mock"
  }
}

mock_resource "azurerm_container_registry" {
  defaults = {
    id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.ContainerRegistry/registries/crmock"
    login_server = "crmock.azurecr.io"
  }
}

mock_resource "azurerm_role_assignment" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/providers/Microsoft.Authorization/roleAssignments/77777777-7777-7777-7777-777777777777"
  }
}

mock_resource "azurerm_private_dns_zone" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.Network/privateDnsZones/mock.private.postgres.database.azure.com"
  }
}

mock_resource "azurerm_private_dns_zone_virtual_network_link" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.Network/privateDnsZones/mock.private.postgres.database.azure.com/virtualNetworkLinks/link-mock"
  }
}

mock_resource "azurerm_private_dns_a_record" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.Network/privateDnsZones/mock.example.com/A/mock"
  }
}

mock_resource "azurerm_postgresql_flexible_server" {
  defaults = {
    id   = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-mock"
    fqdn = "psql-mock.postgres.database.azure.com"
  }
}

mock_resource "azurerm_postgresql_flexible_server_database" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-mock/databases/loginapp"
  }
}

mock_resource "azurerm_postgresql_flexible_server_configuration" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-mock/configurations/require_secure_transport"
  }
}

mock_resource "azurerm_key_vault" {
  defaults = {
    id        = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.KeyVault/vaults/kv-mock"
    vault_uri = "https://kv-mock.vault.azure.net/"
  }
}

mock_resource "azurerm_key_vault_secret" {
  defaults = {
    id                      = "https://kv-mock.vault.azure.net/secrets/secret-mock/0123456789abcdef0123456789abcdef"
    versionless_id          = "https://kv-mock.vault.azure.net/secrets/secret-mock"
    resource_id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.KeyVault/vaults/kv-mock/secrets/secret-mock/versions/0123456789abcdef0123456789abcdef"
    resource_versionless_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.KeyVault/vaults/kv-mock/secrets/secret-mock"
    version                 = "0123456789abcdef0123456789abcdef"
  }
}

mock_resource "azurerm_key_vault_certificate" {
  defaults = {
    id                              = "https://kv-mock.vault.azure.net/certificates/cert-mock/fedcba9876543210fedcba9876543210"
    versionless_id                  = "https://kv-mock.vault.azure.net/certificates/cert-mock"
    secret_id                       = "https://kv-mock.vault.azure.net/secrets/cert-mock/fedcba9876543210fedcba9876543210"
    versionless_secret_id           = "https://kv-mock.vault.azure.net/secrets/cert-mock"
    resource_manager_id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.KeyVault/vaults/kv-mock/certificates/cert-mock/versions/fedcba9876543210fedcba9876543210"
    resource_manager_versionless_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.KeyVault/vaults/kv-mock/certificates/cert-mock"
    thumbprint                      = "0000000000000000000000000000000000000000"
    version                         = "fedcba9876543210fedcba9876543210"
  }
}

mock_resource "azurerm_monitor_diagnostic_setting" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.KeyVault/vaults/kv-mock|send-to-log-analytics"
  }
}

mock_resource "azurerm_container_app_environment" {
  defaults = {
    id                = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.App/managedEnvironments/cae-mock"
    default_domain    = "mockdomain-1a2b3c4d.eastus.azurecontainerapps.io"
    static_ip_address = "203.0.113.20" # public: the environment is external
  }
}

mock_resource "azurerm_container_app" {
  defaults = {
    id                   = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.App/containerApps/ca-mock"
    latest_revision_name = "ca-mock--0000001"
    latest_revision_fqdn = "ca-mock--0000001.mockdomain-1a2b3c4d.eastus.azurecontainerapps.io"
  }
}

mock_resource "azurerm_container_app_job" {
  defaults = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-mock/providers/Microsoft.App/jobs/caj-mock"
  }
}
