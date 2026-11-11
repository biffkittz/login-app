# Tests for the security-relevant settings inside each module. Every run plans one module on its own (the `module`
# block), so its resources can be asserted directly. Providers are mocked: nothing touches Azure.
#
# Run from infra/:  terraform init -backend=false && terraform test

# Realistic computed values (resource IDs, FQDNs, ...) live in tests/mocks.
mock_provider "azurerm" {
  source = "./tests/mocks/azurerm"
}

mock_provider "time" {}

variables {
  location            = "eastus"
  resource_group_name = "rg-test"
  tags                = { project = "loginapp" }
}

# ---------------------------------------------------------------------------------------------------------------------
# networking
# ---------------------------------------------------------------------------------------------------------------------

run "networking_restricts_every_subnet" {
  command = plan

  module {
    source = "./modules/networking"
  }

  variables {
    name          = "loginapp-test"
    address_space = ["10.40.0.0/16"]
    subnet_address_prefixes = {
      container_apps = "10.40.1.0/24"
      postgresql     = "10.40.2.0/24"
    }
    app_ingress_ports         = [8443, 8444]
    app_ingress_allowed_cidrs = ["173.245.48.0/20", "103.21.244.0/22"]
  }

  assert {
    condition = (
      azurerm_network_security_rule.inbound["container_apps/AllowAppIngressInbound"].source_address_prefixes == toset(["173.245.48.0/20", "103.21.244.0/22"]) &&
      azurerm_network_security_rule.inbound["container_apps/AllowAppIngressInbound"].destination_port_ranges == toset(["8443", "8444", "30000-32767"]) &&
      azurerm_network_security_rule.inbound["container_apps/DenyVirtualNetworkInbound"].access == "Deny"
    )
    error_message = "The apps' ingress must be open to the allowed (Cloudflare) ranges only, on every exposed port and the node-port range."
  }

  assert {
    condition = (
      azurerm_network_security_rule.inbound["postgresql/AllowContainerAppsPostgresInbound"].source_address_prefix == "10.40.1.0/24" &&
      azurerm_network_security_rule.inbound["postgresql/AllowContainerAppsPostgresInbound"].destination_port_range == "5432" &&
      azurerm_network_security_rule.inbound["postgresql/DenyVirtualNetworkInbound"].access == "Deny"
    )
    error_message = "Only the Container Apps subnet may reach PostgreSQL."
  }

  assert {
    condition = alltrue([
      for key, rule in azurerm_network_security_rule.inbound :
      rule.priority < azurerm_network_security_rule.inbound[format("%s/DenyVirtualNetworkInbound", split("/", key)[0])].priority
      if rule.access == "Allow"
    ])
    error_message = "Every allow rule must take precedence over its subnet's deny rule."
  }

  assert {
    condition = (
      azurerm_subnet.container_apps.delegation[0].service_delegation[0].name == "Microsoft.App/environments" &&
      azurerm_subnet.postgresql.delegation[0].service_delegation[0].name == "Microsoft.DBforPostgreSQL/flexibleServers"
    )
    error_message = "The Container Apps and PostgreSQL subnets must be delegated to their services."
  }

  assert {
    condition = (
      [for endpoint in azurerm_subnet.container_apps.service_endpoint : endpoint.service] == ["Microsoft.KeyVault", "Microsoft.Storage"] &&
      [for endpoint in azurerm_subnet.postgresql.service_endpoint : endpoint.service] == ["Microsoft.Storage"]
    )
    error_message = "Unexpected service endpoints."
  }

  assert {
    condition     = length(azurerm_subnet_network_security_group_association.this) == 2
    error_message = "Every subnet needs an NSG."
  }
}

run "networking_without_a_source_list_opens_the_ingress_to_the_internet" {
  command = plan

  module {
    source = "./modules/networking"
  }

  variables {
    name          = "loginapp-test"
    address_space = ["10.40.0.0/16"]
    subnet_address_prefixes = {
      container_apps = "10.40.1.0/24"
      postgresql     = "10.40.2.0/24"
    }
    app_ingress_allowed_cidrs = []
  }

  assert {
    condition     = azurerm_network_security_rule.inbound["container_apps/AllowAppIngressInbound"].source_address_prefix == "Internet"
    error_message = "With no allowed ranges, the ingress must be open to the internet."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# database
# ---------------------------------------------------------------------------------------------------------------------

run "database_is_private_and_smallest_size" {
  command = plan

  module {
    source = "./modules/database"
  }

  variables {
    server_name               = "psql-loginapp-test-abc123"
    postgresql_version        = "18"
    sku_name                  = "B_Standard_B1ms"
    storage_mb                = 32768
    backup_retention_days     = 7
    administrator_login       = "loginapp_admin"
    administrator_password    = "not-a-real-password"
    database_name             = "loginapp"
    additional_database_names = { grocery = "grocery" }
    delegated_subnet_id       = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-psql"
    virtual_network_id        = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet"
  }

  assert {
    condition = (
      azurerm_postgresql_flexible_server.this.sku_name == "B_Standard_B1ms" &&
      azurerm_postgresql_flexible_server.this.version == "18" &&
      azurerm_postgresql_flexible_server.this.public_network_access_enabled == false
    )
    error_message = "The server must be B1ms, PostgreSQL 18 and without public access."
  }

  assert {
    condition     = azurerm_postgresql_flexible_server_database.this.name == "loginapp"
    error_message = "The database must be called loginapp."
  }

  assert {
    condition = (
      azurerm_postgresql_flexible_server_database.additional["grocery"].name == "grocery" &&
      azurerm_postgresql_flexible_server_database.additional["grocery"].charset == "UTF8" &&
      output.additional_database_names == { grocery = "grocery" }
    )
    error_message = "Each additional instance gets its own UTF-8 database on the same server."
  }

  assert {
    condition     = azurerm_private_dns_zone.this.name == "psql-loginapp-test-abc123.private.postgres.database.azure.com"
    error_message = "The private DNS zone must end in .postgres.database.azure.com and differ from the server name."
  }

  assert {
    condition     = azurerm_postgresql_flexible_server_configuration.require_secure_transport.value == "on"
    error_message = "TLS must be required."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# registry
# ---------------------------------------------------------------------------------------------------------------------

run "registry_is_basic_without_admin_user" {
  command = plan

  module {
    source = "./modules/registry"
  }

  variables {
    name               = "crloginapptestabc123"
    pull_principal_ids = { app = "44444444-4444-4444-4444-444444444444" }
  }

  assert {
    condition     = azurerm_container_registry.this.sku == "Basic" && azurerm_container_registry.this.admin_enabled == false
    error_message = "The registry must be Basic with the admin user disabled."
  }

  assert {
    condition     = azurerm_role_assignment.acr_pull["app"].role_definition_name == "AcrPull"
    error_message = "The app identity must get AcrPull and nothing more."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# key-vault
# ---------------------------------------------------------------------------------------------------------------------

# Mocked apply, so the role assignment scopes (derived from computed IDs) are known when asserted.
run "key_vault_uses_rbac_with_per_secret_access" {
  command = apply

  module {
    source = "./modules/key-vault"
  }

  variables {
    name                       = "kv-loginapptest-abc123"
    tenant_id                  = "11111111-1111-1111-1111-111111111111"
    administrator_principal_id = "22222222-2222-2222-2222-222222222222"
    secret_names               = { connection = "ConnectionStrings--DefaultConnection" }
    secret_values              = { connection = "Host=example" }
    secret_readers = {
      app = { secret = "connection", principal_id = "44444444-4444-4444-4444-444444444444" }
    }
    certificate_name                 = "app-origin"
    certificate_pfx_base64           = "cGxhY2Vob2xkZXI="
    certificate_reader_principal_ids = { app = "55555555-5555-5555-5555-555555555555" }
    log_analytics_workspace_id       = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.OperationalInsights/workspaces/log-test"
  }

  assert {
    condition     = azurerm_key_vault.this.rbac_authorization_enabled && azurerm_key_vault.this.purge_protection_enabled
    error_message = "The vault must use RBAC and purge protection."
  }

  assert {
    condition     = azurerm_key_vault.this.network_acls[0].default_action == "Allow" && azurerm_key_vault.this.network_acls[0].bypass == "AzureServices"
    error_message = "The firewall is opt-in."
  }

  assert {
    condition     = azurerm_role_assignment.secret_reader["app"].role_definition_name == "Key Vault Secrets User"
    error_message = "Readers must get Key Vault Secrets User (read-only)."
  }

  assert {
    condition     = azurerm_role_assignment.certificate_reader["app"].scope == "${azurerm_key_vault.this.id}/secrets/app-origin"
    error_message = "The app's access to the certificate must be scoped to the certificate's secret."
  }

  assert {
    condition     = endswith(azurerm_role_assignment.secret_reader["app"].scope, "/providers/Microsoft.KeyVault/vaults/kv-mock/secrets/secret-mock")
    error_message = "Secret readers must be scoped to their single secret (its ARM ID, not the vault)."
  }

  assert {
    condition     = output.secret_ids["connection"] == "https://kv-mock.vault.azure.net/secrets/secret-mock"
    error_message = "Consumers must get versionless secret URIs."
  }

  assert {
    condition     = toset(keys(azurerm_role_assignment.administrator)) == toset(["Key Vault Secrets Officer", "Key Vault Certificates Officer"])
    error_message = "The deployer must be able to manage secrets and certificates (but not keys)."
  }

  assert {
    condition     = length(azurerm_role_assignment.additional_administrator) == 0
    error_message = "Without additional administrators, only the deployer manages the vault's contents."
  }
}

run "key_vault_additional_administrators_get_the_same_roles" {
  command = apply

  module {
    source = "./modules/key-vault"
  }

  variables {
    name                                   = "kv-loginapptest-abc123"
    tenant_id                              = "11111111-1111-1111-1111-111111111111"
    administrator_principal_id             = "22222222-2222-2222-2222-222222222222"
    additional_administrator_principal_ids = { github_deploy = "66666666-6666-6666-6666-666666666666" }
    secret_names                           = { connection = "ConnectionStrings--DefaultConnection" }
    secret_values                          = { connection = "Host=example" }
    certificate_name                       = "app-origin"
    certificate_pfx_base64                 = "cGxhY2Vob2xkZXI="
    log_analytics_workspace_id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.OperationalInsights/workspaces/log-test"
  }

  assert {
    condition = (
      toset(keys(azurerm_role_assignment.additional_administrator)) == toset(["github_deploy: Key Vault Secrets Officer", "github_deploy: Key Vault Certificates Officer"]) &&
      alltrue([for assignment in azurerm_role_assignment.additional_administrator : assignment.principal_id == "66666666-6666-6666-6666-666666666666" && assignment.scope == azurerm_key_vault.this.id])
    )
    error_message = "Each additional administrator must get both administrator roles on the vault."
  }

  assert {
    condition     = toset(keys(azurerm_role_assignment.administrator)) == toset(["Key Vault Secrets Officer", "Key Vault Certificates Officer"])
    error_message = "The deployer's own assignments must keep their addresses."
  }
}

run "key_vault_rejects_the_deployer_listed_again_as_an_additional_administrator" {
  command = plan

  module {
    source = "./modules/key-vault"
  }

  variables {
    name                                   = "kv-loginapptest-abc123"
    tenant_id                              = "11111111-1111-1111-1111-111111111111"
    administrator_principal_id             = "22222222-2222-2222-2222-222222222222"
    additional_administrator_principal_ids = { me = "22222222-2222-2222-2222-222222222222" }
    secret_names                           = {}
    secret_values                          = {}
    certificate_name                       = "app-origin"
    certificate_pfx_base64                 = "cGxhY2Vob2xkZXI="
    log_analytics_workspace_id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.OperationalInsights/workspaces/log-test"
  }

  expect_failures = [var.additional_administrator_principal_ids]
}

run "key_vault_firewall_denies_by_default_when_enabled" {
  command = plan

  module {
    source = "./modules/key-vault"
  }

  variables {
    name                       = "kv-loginapptest-abc123"
    tenant_id                  = "11111111-1111-1111-1111-111111111111"
    administrator_principal_id = "22222222-2222-2222-2222-222222222222"
    firewall_enabled           = true
    allowed_ip_ranges          = ["203.0.113.10/32"]
    allowed_subnet_ids         = ["/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-aca"]
    certificate_name           = "app-origin"
    certificate_pfx_base64     = "cGxhY2Vob2xkZXI="
    log_analytics_workspace_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.OperationalInsights/workspaces/log-test"
  }

  assert {
    condition = (
      azurerm_key_vault.this.network_acls[0].default_action == "Deny" &&
      azurerm_key_vault.this.network_acls[0].ip_rules == toset(["203.0.113.10/32"]) &&
      length(azurerm_key_vault.this.network_acls[0].virtual_network_subnet_ids) == 1
    )
    error_message = "An enabled firewall must deny by default and allow only the listed ranges and subnets."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# container-app
# ---------------------------------------------------------------------------------------------------------------------

run "container_app_is_public_single_replica_and_terminates_tls" {
  command = plan

  module {
    source = "./modules/container-app"
  }

  variables {
    environment_name                   = "cae-loginapp-test"
    infrastructure_resource_group_name = "rg-loginapp-test-cae-managed"
    infrastructure_subnet_id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-aca"
    log_analytics_workspace_id         = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.OperationalInsights/workspaces/log-test"
    deploy_app                         = true
    app_image                          = "crloginapptestabc123.azurecr.io/login-app:1a2b3c4"
    app_identity_id                    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-app"
    registry_server                    = "crloginapptestabc123.azurecr.io"
    app_target_port                    = 443
    app_cpu                            = 0.5
    app_memory                         = "1Gi"
    app_min_replicas                   = 1
    app_max_replicas                   = 1
    apps = {
      main = {
        name                  = "ca-loginapp-test"
        exposed_port          = 8443
        secrets               = { db-connection-string = "https://kv-test.vault.azure.net/secrets/ConnectionStrings--DefaultConnection" }
        environment_variables = {}
      }
      grocery = {
        name                  = "ca-loginapp-test-grocery"
        exposed_port          = 8444
        secrets               = { db-connection-string = "https://kv-test.vault.azure.net/secrets/grocery-db-connection-string" }
        environment_variables = { Jwt__Audience = "grocery.example.com" }
      }
    }
    app_secret_environment_variables = { ConnectionStrings__DefaultConnection = "db-connection-string" }
    app_environment_variables        = { ASPNETCORE_ENVIRONMENT = "Production" }
    ingress_allowed_cidrs            = { cloudflare-0 = "173.245.48.0/20" }
    db_bootstrap = {
      job_name                  = "caj-loginapp-test-dbinit"
      image                     = "mcr.microsoft.com/azurelinux/base/postgres:16.15-1-azl3.0.20261027"
      identity_id               = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-dbinit"
      postgresql_host           = "psql-loginapp-test-abc123.postgres.database.azure.com"
      database_name             = "loginapp"
      app_databases             = ["loginapp", "grocery"]
      administrator_login       = "loginapp_admin"
      administrator_password_id = "https://kv-test.vault.azure.net/secrets/postgresql-admin-password"
      app_role                  = "loginapp_app"
      app_password_id           = "https://kv-test.vault.azure.net/secrets/postgresql-app-password"
    }
  }

  assert {
    condition     = !azurerm_container_app_environment.this.internal_load_balancer_enabled
    error_message = "The environment must be external: Cloudflare connects to its public IP."
  }

  assert {
    condition     = one(azurerm_container_app_environment.this.workload_profile).workload_profile_type == "Consumption"
    error_message = "The environment must use the Consumption workload profile."
  }

  assert {
    condition     = azurerm_container_app.this["main"].template[0].min_replicas == 1 && azurerm_container_app.this["main"].template[0].max_replicas == 1
    error_message = "The demo must run exactly one replica."
  }

  assert {
    condition = (
      azurerm_container_app.this["main"].ingress[0].external_enabled &&
      azurerm_container_app.this["main"].ingress[0].transport == "tcp" &&
      azurerm_container_app.this["main"].ingress[0].exposed_port == 8443 &&
      azurerm_container_app.this["main"].ingress[0].target_port == 443
    )
    error_message = "The ingress must pass TCP from 8443 straight through to the container on 443, so TLS ends in the container."
  }

  assert {
    condition = (
      length(azurerm_container_app.this["main"].ingress[0].ip_security_restriction) == 1 &&
      azurerm_container_app.this["main"].ingress[0].ip_security_restriction[0].action == "Allow" &&
      azurerm_container_app.this["main"].ingress[0].ip_security_restriction[0].ip_address_range == "173.245.48.0/20"
    )
    error_message = "The ingress must allow only the listed ranges."
  }

  assert {
    condition = contains(
      [for env in azurerm_container_app.this["main"].template[0].container[0].env : env.value if env.name == "ASPNETCORE_URLS"],
      "https://+:443",
    )
    error_message = "Kestrel must listen for HTTPS only, on the target port."
  }

  assert {
    condition = (
      azurerm_container_app.this["main"].template[0].container[0].startup_probe[0].transport == "HTTPS" &&
      azurerm_container_app.this["main"].template[0].container[0].liveness_probe[0].transport == "HTTPS" &&
      azurerm_container_app.this["main"].template[0].container[0].readiness_probe[0].transport == "HTTPS" &&
      azurerm_container_app.this["main"].template[0].container[0].readiness_probe[0].port == 443
    )
    error_message = "The probes must use HTTPS on the target port: nothing listens on plain HTTP."
  }

  assert {
    condition     = one([for secret in azurerm_container_app.this["main"].secret : secret.key_vault_secret_id]) == "https://kv-test.vault.azure.net/secrets/ConnectionStrings--DefaultConnection"
    error_message = "App secrets must be Key Vault references, not inline values."
  }

  assert {
    condition = (
      azurerm_container_app_job.db_bootstrap.manual_trigger_config[0].parallelism == 1 &&
      contains([for env in azurerm_container_app_job.db_bootstrap.template[0].container[0].env : env.value if env.name == "PGSSLMODE"], "verify-full")
    )
    error_message = "The bootstrap job must be manual and verify the server's TLS certificate."
  }

  assert {
    condition     = contains([for env in azurerm_container_app_job.db_bootstrap.template[0].container[0].env : env.value if env.name == "APP_DATABASES"], "loginapp grocery")
    error_message = "The bootstrap job must let the app's role into every instance's database."
  }

  assert {
    condition = (
      azurerm_container_app.this["grocery"].name == "ca-loginapp-test-grocery" &&
      azurerm_container_app.this["grocery"].ingress[0].exposed_port == 8444 &&
      azurerm_container_app.this["grocery"].template[0].container[0].image == azurerm_container_app.this["main"].template[0].container[0].image
    )
    error_message = "A second app runs the same image on its own exposed port."
  }

  assert {
    condition = (
      one([for secret in azurerm_container_app.this["grocery"].secret : secret.key_vault_secret_id]) == "https://kv-test.vault.azure.net/secrets/grocery-db-connection-string" &&
      contains([for env in azurerm_container_app.this["grocery"].template[0].container[0].env : env.value if env.name == "Jwt__Audience"], "grocery.example.com") &&
      contains([for env in azurerm_container_app.this["grocery"].template[0].container[0].env : env.value if env.name == "ASPNETCORE_ENVIRONMENT"], "Production") &&
      length([for env in azurerm_container_app.this["main"].template[0].container[0].env : env if env.name == "Jwt__Audience"]) == 0
    )
    error_message = "Each app gets its own secrets and extra variables on top of the shared ones."
  }

  assert {
    condition     = output.app_names == { main = "ca-loginapp-test", grocery = "ca-loginapp-test-grocery" }
    error_message = "The outputs must name every app, keyed like apps."
  }
}

run "container_apps_cannot_share_an_exposed_port" {
  command = plan

  module {
    source = "./modules/container-app"
  }

  variables {
    environment_name                   = "cae-loginapp-test"
    infrastructure_resource_group_name = "rg-loginapp-test-cae-managed"
    infrastructure_subnet_id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-aca"
    log_analytics_workspace_id         = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.OperationalInsights/workspaces/log-test"
    deploy_app                         = false
    app_identity_id                    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-app"
    registry_server                    = "crloginapptestabc123.azurecr.io"
    app_target_port                    = 443
    app_cpu                            = 0.5
    app_memory                         = "1Gi"
    app_min_replicas                   = 1
    app_max_replicas                   = 1
    apps = {
      main    = { name = "ca-loginapp-test", exposed_port = 8443, secrets = {}, environment_variables = {} }
      grocery = { name = "ca-loginapp-test-grocery", exposed_port = 8443, secrets = {}, environment_variables = {} }
    }
    ingress_allowed_cidrs = {}
    db_bootstrap = {
      job_name                  = "caj-loginapp-test-dbinit"
      image                     = "mcr.microsoft.com/azurelinux/base/postgres:16.15-1-azl3.0.20261027"
      identity_id               = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-dbinit"
      postgresql_host           = "psql-loginapp-test-abc123.postgres.database.azure.com"
      database_name             = "loginapp"
      app_databases             = ["loginapp"]
      administrator_login       = "loginapp_admin"
      administrator_password_id = "https://kv-test.vault.azure.net/secrets/postgresql-admin-password"
      app_role                  = "loginapp_app"
      app_password_id           = "https://kv-test.vault.azure.net/secrets/postgresql-app-password"
    }
  }

  expect_failures = [var.apps]
}

run "container_app_waits_for_an_image" {
  command = plan

  module {
    source = "./modules/container-app"
  }

  variables {
    environment_name                   = "cae-loginapp-test"
    infrastructure_resource_group_name = "rg-loginapp-test-cae-managed"
    infrastructure_subnet_id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-aca"
    log_analytics_workspace_id         = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.OperationalInsights/workspaces/log-test"
    deploy_app                         = false
    app_identity_id                    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-app"
    registry_server                    = "crloginapptestabc123.azurecr.io"
    app_target_port                    = 443
    apps                               = { main = { name = "ca-loginapp-test", exposed_port = 8443, secrets = {}, environment_variables = {} } }
    app_cpu                            = 0.5
    app_memory                         = "1Gi"
    app_min_replicas                   = 1
    app_max_replicas                   = 1
    ingress_allowed_cidrs              = { cloudflare-0 = "173.245.48.0/20" }
    db_bootstrap = {
      job_name                  = "caj-loginapp-test-dbinit"
      image                     = "mcr.microsoft.com/azurelinux/base/postgres:16.15-1-azl3.0.20261027"
      identity_id               = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-dbinit"
      postgresql_host           = "psql-loginapp-test-abc123.postgres.database.azure.com"
      database_name             = "loginapp"
      app_databases             = ["loginapp"]
      administrator_login       = "loginapp_admin"
      administrator_password_id = "https://kv-test.vault.azure.net/secrets/postgresql-admin-password"
      app_role                  = "loginapp_app"
      app_password_id           = "https://kv-test.vault.azure.net/secrets/postgresql-app-password"
    }
  }

  assert {
    condition     = length(azurerm_container_app.this) == 0 && length(output.app_ids) == 0
    error_message = "No Container App may be created before its image exists."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# observability (Prometheus and Grafana)
# ---------------------------------------------------------------------------------------------------------------------

run "observability_keeps_its_data_on_a_private_nfs_share" {
  command = apply

  module {
    source = "./modules/observability"
  }

  variables {
    subscription_id                          = "00000000-0000-0000-0000-000000000000"
    container_app_environment_id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.App/managedEnvironments/cae-test"
    container_app_environment_default_domain = "mockdomain-1a2b3c4d.eastus.azurecontainerapps.io"
    container_apps_subnet_id                 = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-aca"
    storage_account_name                     = "stloginapptestabc123"
    images = {
      prometheus             = "quay.io/prometheus/prometheus@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2"
      azure_metrics_exporter = "quay.io/webdevops/azure-metrics-exporter@sha256:0f26a275e593538983a13c9ebb9d229e296524a3f292e684666667f24eae2b76"
      grafana                = "docker.io/grafana/grafana@sha256:0615ec4fbd4072004b7d86b46ded63013a74b7d6eb1119273dd89e8fa25c13cf"
      init                   = "mcr.microsoft.com/azurelinux/base/core@sha256:88fc2b4a425801b3b4d8fe5342e24fb6134669fb718ec970340bdd36dee50c35"
    }
    prometheus_app_name = "ca-loginapp-test-prometheus"
    prometheus_identity = {
      id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-prometheus"
      principal_id = "44444444-4444-4444-4444-444444444444"
      client_id    = "55555555-5555-5555-5555-555555555555"
    }
    prometheus_secret_ids = {
      web_config           = "https://kv-test.vault.azure.net/secrets/prometheus-web-config"
      self_scrape_password = "https://kv-test.vault.azure.net/secrets/prometheus-self-scrape-password"
    }
    monitored_resource_group_id    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test"
    monitored_container_app_ids    = ["/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.App/containerApps/ca-loginapp-test"]
    monitored_postgresql_server_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-test"
    grafana_app_name               = "ca-loginapp-test-grafana"
    grafana_identity_id            = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-grafana"
    grafana_hostname               = "grafana.example.com"
    grafana_exposed_port           = 8445
    grafana_ingress_allowed_cidrs  = { cloudflare-0 = "173.245.48.0/20", cloudflare-1 = "103.21.244.0/22" }
    grafana_github_client_id       = "Ov23liAbCdEfGhIjKlMn"
    grafana_github_admins          = ["octocat"]
    grafana_github_viewers         = ["hubot"]
    grafana_secret_ids = {
      github_client_secret = "https://kv-test.vault.azure.net/secrets/grafana-github-client-secret"
      secret_key           = "https://kv-test.vault.azure.net/secrets/grafana-secret-key"
      prometheus_password  = "https://kv-test.vault.azure.net/secrets/prometheus-grafana-password"
      origin_certificate   = "https://kv-test.vault.azure.net/secrets/app-example-com-origin"
    }
  }

  assert {
    condition = (
      azurerm_storage_account.this.account_kind == "FileStorage" &&
      azurerm_storage_account.this.account_tier == "Premium" &&
      azurerm_storage_share.this.enabled_protocol == "NFS" &&
      azurerm_storage_share.this.quota == 100
    )
    error_message = "Prometheus' data must live on an NFS share (a POSIX file system) of a Premium FileStorage account."
  }

  assert {
    condition = (
      !azurerm_storage_account.this.shared_access_key_enabled &&
      azurerm_storage_account.this.network_rules[0].default_action == "Deny" &&
      azurerm_storage_account.this.network_rules[0].virtual_network_subnet_ids == toset([var.container_apps_subnet_id]) &&
      azurerm_storage_account.this.min_tls_version == "TLS1_2"
    )
    error_message = "The storage account must take no keys and admit only the Container Apps subnet."
  }

  assert {
    condition = (
      azurerm_container_app_environment_storage.this.nfs_server_url == "stloginapptestabc123.file.core.windows.net" &&
      azurerm_container_app_environment_storage.this.share_name == "/stloginapptestabc123/observability" &&
      azurerm_container_app_environment_storage.this.access_mode == "ReadWrite"
    )
    error_message = "Container Apps mounts an NFS share as /<account>/<share> from <account>.file.core.windows.net."
  }

  assert {
    condition = (
      azurerm_role_assignment.metrics_reader.role_definition_name == "Monitoring Reader" &&
      azurerm_role_assignment.metrics_reader.scope == var.monitored_resource_group_id
    )
    error_message = "The Azure Monitor exporter may read the resource group's metrics, and nothing more."
  }
}

run "prometheus_is_internal_single_writer_and_receives_otlp" {
  command = apply

  module {
    source = "./modules/observability"
  }

  variables {
    subscription_id                          = "00000000-0000-0000-0000-000000000000"
    container_app_environment_id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.App/managedEnvironments/cae-test"
    container_app_environment_default_domain = "mockdomain-1a2b3c4d.eastus.azurecontainerapps.io"
    container_apps_subnet_id                 = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-aca"
    storage_account_name                     = "stloginapptestabc123"
    images = {
      prometheus             = "quay.io/prometheus/prometheus@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2"
      azure_metrics_exporter = "quay.io/webdevops/azure-metrics-exporter@sha256:0f26a275e593538983a13c9ebb9d229e296524a3f292e684666667f24eae2b76"
      grafana                = "docker.io/grafana/grafana@sha256:0615ec4fbd4072004b7d86b46ded63013a74b7d6eb1119273dd89e8fa25c13cf"
      init                   = "mcr.microsoft.com/azurelinux/base/core@sha256:88fc2b4a425801b3b4d8fe5342e24fb6134669fb718ec970340bdd36dee50c35"
    }
    prometheus_app_name = "ca-loginapp-test-prometheus"
    prometheus_identity = {
      id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-prometheus"
      principal_id = "44444444-4444-4444-4444-444444444444"
      client_id    = "55555555-5555-5555-5555-555555555555"
    }
    prometheus_secret_ids = {
      web_config           = "https://kv-test.vault.azure.net/secrets/prometheus-web-config"
      self_scrape_password = "https://kv-test.vault.azure.net/secrets/prometheus-self-scrape-password"
    }
    monitored_resource_group_id    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test"
    monitored_container_app_ids    = ["/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.App/containerApps/ca-loginapp-test"]
    monitored_postgresql_server_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-test"
    grafana_app_name               = "ca-loginapp-test-grafana"
    grafana_identity_id            = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-grafana"
    grafana_hostname               = "grafana.example.com"
    grafana_exposed_port           = 8445
    grafana_github_client_id       = "Ov23liAbCdEfGhIjKlMn"
    grafana_github_admins          = ["octocat"]
    grafana_secret_ids = {
      github_client_secret = "https://kv-test.vault.azure.net/secrets/grafana-github-client-secret"
      secret_key           = "https://kv-test.vault.azure.net/secrets/grafana-secret-key"
      prometheus_password  = "https://kv-test.vault.azure.net/secrets/prometheus-grafana-password"
      origin_certificate   = "https://kv-test.vault.azure.net/secrets/app-example-com-origin"
    }
  }

  assert {
    condition = (
      !azurerm_container_app.prometheus.ingress[0].external_enabled &&
      azurerm_container_app.prometheus.ingress[0].target_port == 9090 &&
      !azurerm_container_app.prometheus.ingress[0].allow_insecure_connections
    )
    error_message = "Prometheus must be reachable from inside the environment only, over HTTPS."
  }

  assert {
    condition     = azurerm_container_app.prometheus.template[0].min_replicas == 1 && azurerm_container_app.prometheus.template[0].max_replicas == 1
    error_message = "Prometheus' database takes exactly one writer."
  }

  assert {
    condition = (
      contains(azurerm_container_app.prometheus.template[0].container[0].args, "--web.enable-otlp-receiver") &&
      contains(azurerm_container_app.prometheus.template[0].container[0].args, "--web.config.file=/etc/prometheus/config/web.yml") &&
      strcontains(azurerm_container_app.prometheus.template[0].container[0].args[0], "flock -s -n /prometheus/lock")
    )
    error_message = "Prometheus must accept OTLP pushes, require its users, and wait for the previous replica's lock (testing it with a shared lock, which busybox can take on NFS)."
  }

  assert {
    condition = alltrue([
      for probe in concat(
        azurerm_container_app.prometheus.template[0].container[0].startup_probe,
        azurerm_container_app.prometheus.template[0].container[0].liveness_probe,
        azurerm_container_app.prometheus.template[0].container[0].readiness_probe,
      ) : probe.port == 8080 && probe.transport == "TCP"
    ])
    error_message = "Prometheus' probes must watch the exporter's port, so a replica waiting for the database lock counts as ready."
  }

  assert {
    condition = one([
      for volume in azurerm_container_app.prometheus.template[0].volume : volume.storage_type if volume.name == "data"
    ]) == "NfsAzureFile"
    error_message = "Prometheus' database must be on the NFS share."
  }

  assert {
    condition = (
      yamldecode(one([for env in azurerm_container_app.prometheus.template[0].init_container[0].env : env.value if env.name == "PROMETHEUS_CONFIG"])).otlp.promote_resource_attributes == ["site"] &&
      yamldecode(one([for env in azurerm_container_app.prometheus.template[0].init_container[0].env : env.value if env.name == "PROMETHEUS_CONFIG"])).storage.tsdb.out_of_order_time_window == "30m"
    )
    error_message = "Pushed metrics must keep their site label, and slightly late samples must be accepted."
  }

  assert {
    condition = contains(
      flatten([
        for job in yamldecode(one([for env in azurerm_container_app.prometheus.template[0].init_container[0].env : env.value if env.name == "PROMETHEUS_CONFIG"])).scrape_configs :
        try(job.params.target, []) if job.job_name == "azure-containerapp"
      ]),
      "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.App/containerApps/ca-loginapp-test-grafana",
    )
    error_message = "Prometheus must collect the platform metrics of every Container App, Grafana's included."
  }

  assert {
    condition     = output.otlp_metrics_endpoint == "https://ca-loginapp-test-prometheus.internal.mockdomain-1a2b3c4d.eastus.azurecontainerapps.io/api/v1/otlp/v1/metrics"
    error_message = "The app pushes to Prometheus' OTLP receiver at its internal address."
  }
}

run "grafana_is_behind_cloudflare_and_signs_in_with_github_only" {
  command = apply

  module {
    source = "./modules/observability"
  }

  variables {
    subscription_id                          = "00000000-0000-0000-0000-000000000000"
    container_app_environment_id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.App/managedEnvironments/cae-test"
    container_app_environment_default_domain = "mockdomain-1a2b3c4d.eastus.azurecontainerapps.io"
    container_apps_subnet_id                 = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-aca"
    storage_account_name                     = "stloginapptestabc123"
    images = {
      prometheus             = "quay.io/prometheus/prometheus@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2"
      azure_metrics_exporter = "quay.io/webdevops/azure-metrics-exporter@sha256:0f26a275e593538983a13c9ebb9d229e296524a3f292e684666667f24eae2b76"
      grafana                = "docker.io/grafana/grafana@sha256:0615ec4fbd4072004b7d86b46ded63013a74b7d6eb1119273dd89e8fa25c13cf"
      init                   = "mcr.microsoft.com/azurelinux/base/core@sha256:88fc2b4a425801b3b4d8fe5342e24fb6134669fb718ec970340bdd36dee50c35"
    }
    prometheus_app_name = "ca-loginapp-test-prometheus"
    prometheus_identity = {
      id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-prometheus"
      principal_id = "44444444-4444-4444-4444-444444444444"
      client_id    = "55555555-5555-5555-5555-555555555555"
    }
    prometheus_secret_ids = {
      web_config           = "https://kv-test.vault.azure.net/secrets/prometheus-web-config"
      self_scrape_password = "https://kv-test.vault.azure.net/secrets/prometheus-self-scrape-password"
    }
    monitored_resource_group_id    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test"
    monitored_postgresql_server_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-test"
    grafana_app_name               = "ca-loginapp-test-grafana"
    grafana_identity_id            = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-grafana"
    grafana_hostname               = "grafana.example.com"
    grafana_exposed_port           = 8445
    grafana_ingress_allowed_cidrs  = { cloudflare-0 = "173.245.48.0/20", cloudflare-1 = "103.21.244.0/22" }
    grafana_github_client_id       = "Ov23liAbCdEfGhIjKlMn"
    grafana_github_admins          = ["octocat"]
    grafana_github_viewers         = ["hubot"]
    grafana_secret_ids = {
      github_client_secret = "https://kv-test.vault.azure.net/secrets/grafana-github-client-secret"
      secret_key           = "https://kv-test.vault.azure.net/secrets/grafana-secret-key"
      prometheus_password  = "https://kv-test.vault.azure.net/secrets/prometheus-grafana-password"
      origin_certificate   = "https://kv-test.vault.azure.net/secrets/app-example-com-origin"
    }
  }

  assert {
    condition = (
      azurerm_container_app.grafana.ingress[0].external_enabled &&
      azurerm_container_app.grafana.ingress[0].transport == "tcp" &&
      azurerm_container_app.grafana.ingress[0].exposed_port == 8445 &&
      azurerm_container_app.grafana.ingress[0].target_port == 3000 &&
      length(azurerm_container_app.grafana.ingress[0].ip_security_restriction) == 2 &&
      alltrue([for rule in azurerm_container_app.grafana.ingress[0].ip_security_restriction : rule.action == "Allow"])
    )
    error_message = "Grafana must be a TCP app on its own port, admitting only the allowed (Cloudflare) ranges."
  }

  assert {
    condition = (
      local.grafana_environment["GF_SERVER_PROTOCOL"] == "https" &&
      local.grafana_environment["GF_SERVER_ROOT_URL"] == "https://grafana.example.com/" &&
      local.grafana_environment["GF_AUTH_DISABLE_LOGIN_FORM"] == "true" &&
      local.grafana_environment["GF_AUTH_BASIC_ENABLED"] == "false" &&
      local.grafana_environment["GF_SECURITY_DISABLE_INITIAL_ADMIN_CREATION"] == "true" &&
      local.grafana_environment["GF_AUTH_GITHUB_ROLE_ATTRIBUTE_STRICT"] == "true" &&
      !contains(keys(local.grafana_environment), "GF_AUTH_ANONYMOUS_ENABLED")
    )
    error_message = "Grafana must serve HTTPS, and GitHub must be its only way in: no login form, no basic authentication, no admin password."
  }

  assert {
    condition     = local.grafana_environment["GF_AUTH_GITHUB_SCOPES"] == "user:email,read:org"
    error_message = "Grafana lists the user's GitHub teams at every sign-in, which an OAuth App token can only do with read:org."
  }

  assert {
    condition     = local.grafana_role_expression == "contains(`[\"octocat\"]`, login) && 'GrafanaAdmin' || contains(`[\"hubot\"]`, login) && 'Viewer' || ''"
    error_message = "Listed admins must become Grafana administrators, listed viewers viewers, and everyone else no role (refused)."
  }

  assert {
    condition = (
      length(local.grafana_dashboards) == length(fileset("./modules/observability/dashboards", "*.json")) &&
      alltrue([for name in keys(local.grafana_dashboards) : startswith(name, "GRAFANA_DASHBOARD_JSON_")])
    )
    error_message = "Every dashboard in modules/observability/dashboards must reach Grafana."
  }

  assert {
    condition = one([
      for env in azurerm_container_app.grafana.template[0].init_container[0].env : env.secret_name if env.name == "ORIGIN_CERTIFICATE_PFX_BASE64"
      ]) == "origin-certificate" && !contains([
      for env in azurerm_container_app.grafana.template[0].container[0].env : env.name
    ], "ORIGIN_CERTIFICATE_PFX_BASE64")
    error_message = "Only the init container may see the certificate's private key in its PFX form."
  }

  assert {
    condition = one([
      for volume in azurerm_container_app.grafana.template[0].volume : volume.storage_type if volume.name == "data"
    ]) == "NfsAzureFile"
    error_message = "Grafana's database must be on the NFS share."
  }
}

run "observability_refuses_an_image_without_a_digest" {
  command = plan

  module {
    source = "./modules/observability"
  }

  variables {
    subscription_id                          = "00000000-0000-0000-0000-000000000000"
    container_app_environment_id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.App/managedEnvironments/cae-test"
    container_app_environment_default_domain = "mockdomain-1a2b3c4d.eastus.azurecontainerapps.io"
    container_apps_subnet_id                 = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Network/virtualNetworks/vnet/subnets/snet-aca"
    storage_account_name                     = "stloginapptestabc123"
    images = {
      prometheus             = "quay.io/prometheus/prometheus:latest"
      azure_metrics_exporter = "quay.io/webdevops/azure-metrics-exporter@sha256:0f26a275e593538983a13c9ebb9d229e296524a3f292e684666667f24eae2b76"
      grafana                = "docker.io/grafana/grafana@sha256:0615ec4fbd4072004b7d86b46ded63013a74b7d6eb1119273dd89e8fa25c13cf"
      init                   = "mcr.microsoft.com/azurelinux/base/core@sha256:88fc2b4a425801b3b4d8fe5342e24fb6134669fb718ec970340bdd36dee50c35"
    }
    prometheus_app_name = "ca-loginapp-test-prometheus"
    prometheus_identity = {
      id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-prometheus"
      principal_id = "44444444-4444-4444-4444-444444444444"
      client_id    = "55555555-5555-5555-5555-555555555555"
    }
    prometheus_secret_ids = {
      web_config           = "https://kv-test.vault.azure.net/secrets/prometheus-web-config"
      self_scrape_password = "https://kv-test.vault.azure.net/secrets/prometheus-self-scrape-password"
    }
    monitored_resource_group_id    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test"
    monitored_postgresql_server_id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-test"
    grafana_app_name               = "ca-loginapp-test-grafana"
    grafana_identity_id            = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-grafana"
    grafana_hostname               = "grafana.example.com"
    grafana_exposed_port           = 8445
    grafana_github_client_id       = "Ov23liAbCdEfGhIjKlMn"
    grafana_github_admins          = []
    grafana_secret_ids = {
      github_client_secret = "https://kv-test.vault.azure.net/secrets/grafana-github-client-secret"
      secret_key           = "https://kv-test.vault.azure.net/secrets/grafana-secret-key"
      prometheus_password  = "https://kv-test.vault.azure.net/secrets/prometheus-grafana-password"
      origin_certificate   = "https://kv-test.vault.azure.net/secrets/app-example-com-origin"
    }
  }

  expect_failures = [var.images, var.grafana_github_admins]
}
