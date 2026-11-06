# Tests for the root module: wiring, naming, tags, the two-phase app deployment and input validation.
#
# Every provider is mocked, so `terraform test` needs no Azure or Cloudflare credentials and creates nothing.
# Run from infra/:  terraform init -backend=false && terraform test

# Realistic computed values (resource IDs, FQDNs, ...) live in tests/mocks.
mock_provider "azurerm" {
  source = "./tests/mocks/azurerm"
}

mock_provider "cloudflare" {
  source = "./tests/mocks/cloudflare"
}

mock_provider "random" {}

# Mocked so the mocked apply below doesn't actually wait for RBAC propagation.
mock_provider "time" {}

variables {
  subscription_id          = "00000000-0000-0000-0000-000000000000"
  tls_certificate_pfx_path = "tests/fixtures/placeholder-certificate.txt"
}

run "defaults_target_eastus_with_required_tags" {
  command = plan

  assert {
    condition     = azurerm_resource_group.this.location == "eastus"
    error_message = "Resources must be created in eastus by default."
  }

  assert {
    condition = alltrue([
      for tags in [
        azurerm_resource_group.this.tags,
        azurerm_log_analytics_workspace.this.tags,
        azurerm_user_assigned_identity.app.tags,
        azurerm_user_assigned_identity.db_bootstrap.tags,
        azurerm_user_assigned_identity.gateway.tags,
      ] : tags["project"] == "loginapp"
    ])
    error_message = "Every resource must carry the tag project:loginapp."
  }

  assert {
    condition     = azurerm_resource_group.this.tags["environment"] == "demo" && azurerm_resource_group.this.tags["managed_by"] == "terraform"
    error_message = "The environment and managed_by tags must be set."
  }
}

run "extra_tags_cannot_override_the_project_tag" {
  command = plan

  variables {
    tags = {
      project    = "something-else"
      cost_owner = "platform-team"
    }
  }

  assert {
    condition     = azurerm_resource_group.this.tags["project"] == "loginapp" && azurerm_resource_group.this.tags["cost_owner"] == "platform-team"
    error_message = "Extra tags must be added, but project must stay loginapp."
  }
}

run "names_follow_the_naming_convention" {
  command = plan

  assert {
    condition     = azurerm_resource_group.this.name == "rg-loginapp-demo"
    error_message = "Unexpected resource group name."
  }

  assert {
    condition     = output.container_app_name == "ca-loginapp-demo" && output.db_bootstrap_job_name == "caj-loginapp-demo-dbinit"
    error_message = "Unexpected Container App or job name."
  }

  assert {
    condition     = can(regex("^kv-loginappdemo-[0-9a-f]{6}$", output.key_vault_name)) && length(output.key_vault_name) <= 24
    error_message = "The Key Vault name must be kv-<project><environment>-<suffix> and at most 24 characters."
  }

  assert {
    condition     = can(regex("^crloginappdemo[0-9a-f]{6}$", output.container_registry_name))
    error_message = "The registry name must be cr<project><environment><suffix>, alphanumeric only."
  }

  assert {
    condition     = can(regex("^psql-loginapp-demo-[0-9a-f]{6}$", output.postgresql_server_name))
    error_message = "Unexpected PostgreSQL server name."
  }
}

run "name_suffix_can_be_pinned" {
  command = plan

  variables {
    name_suffix = "abc123"
  }

  assert {
    condition     = output.key_vault_name == "kv-loginappdemo-abc123" && output.postgresql_server_name == "psql-loginapp-demo-abc123"
    error_message = "An explicit name_suffix must be used verbatim."
  }
}

run "first_apply_creates_everything_but_the_app" {
  command = plan

  assert {
    condition     = output.app_image == null && module.container_apps.app_id == null
    error_message = "Without app_image_tag there is no image yet, so the Container App must not be created."
  }

  assert {
    condition     = output.database_name == "loginapp"
    error_message = "The database must be called loginapp."
  }

  assert {
    condition     = output.app_url == "https://app.biffkittz.com"
    error_message = "The app must be served at https://app.biffkittz.com."
  }

  assert {
    condition     = output.next_steps.bootstrap_db == "az containerapp job start --name caj-loginapp-demo-dbinit --resource-group rg-loginapp-demo"
    error_message = "The bootstrap command must name the job and resource group."
  }
}

run "dns_record_points_at_the_gateway_through_cloudflare" {
  command = plan

  assert {
    condition = (
      cloudflare_dns_record.app.name == "app.biffkittz.com" &&
      cloudflare_dns_record.app.type == "A" &&
      cloudflare_dns_record.app.proxied == true &&
      cloudflare_dns_record.app.ttl == 1 &&
      cloudflare_dns_record.app.zone_id == "0123456789abcdef0123456789abcdef"
    )
    error_message = "app.biffkittz.com must be a proxied A record in the biffkittz.com zone."
  }

  assert {
    condition     = strcontains(cloudflare_dns_record.app.comment, "project:loginapp") && length(cloudflare_dns_record.app.comment) <= 100
    error_message = "The record comment must carry the project tag and fit Cloudflare's 100-character limit."
  }
}

run "generated_secrets_are_strong" {
  command = plan

  assert {
    condition     = random_password.postgresql_app.length >= 32 && random_password.postgresql_app.special == false
    error_message = "The app's database password must be long and alphanumeric (it is embedded in a connection string)."
  }

  assert {
    condition     = random_password.postgresql_admin.length >= 32 && random_bytes.jwt_signing_key.length >= 32
    error_message = "The admin password and the JWT signing key must have at least 256 bits."
  }
}

# Mocked apply: computed values (registry host name, FQDNs, ...) get placeholder values, so the second phase of
# the deployment can be checked end to end.
run "setting_an_image_tag_deploys_the_app" {
  command = apply

  variables {
    app_image_tag = "1a2b3c4"
  }

  assert {
    condition     = module.container_apps.app_id != null
    error_message = "With app_image_tag set, the Container App must be created."
  }

  assert {
    condition     = output.app_image == "crmock.azurecr.io/login-app:1a2b3c4"
    error_message = "The app must run <registry>/login-app:<app_image_tag>."
  }

  assert {
    condition     = output.container_app_private_fqdn == "ca-loginapp-demo.mockdomain-1a2b3c4d.eastus.azurecontainerapps.io"
    error_message = "The gateway's backend must be <app name>.<environment default domain>."
  }

  assert {
    condition     = cloudflare_dns_record.app.content == output.application_gateway_public_ip
    error_message = "The DNS record must point at the gateway's public IP."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Input validation
# ---------------------------------------------------------------------------------------------------------------------

run "rejects_a_host_name_outside_the_dns_zone" {
  command = plan

  variables {
    app_hostname = "app.example.com"
  }

  expect_failures = [var.app_hostname]
}

run "rejects_an_invalid_environment_name" {
  command = plan

  variables {
    environment = "Demo-Env"
  }

  expect_failures = [var.environment]
}

run "rejects_memory_that_does_not_match_the_cpu" {
  command = plan

  variables {
    app_cpu    = 0.5
    app_memory = "2Gi"
  }

  expect_failures = [var.app_memory]
}

run "rejects_more_minimum_than_maximum_replicas" {
  command = plan

  variables {
    app_min_replicas = 2
    app_max_replicas = 1
  }

  expect_failures = [var.app_max_replicas]
}

run "rejects_a_container_apps_subnet_smaller_than_27" {
  command = plan

  variables {
    subnet_address_prefixes = {
      app_gateway    = "10.40.0.0/24"
      container_apps = "10.40.1.0/28"
      postgresql     = "10.40.2.0/24"
    }
  }

  expect_failures = [var.subnet_address_prefixes]
}

run "rejects_a_missing_certificate_file" {
  command = plan

  variables {
    tls_certificate_pfx_path = "tests/fixtures/does-not-exist.pfx"
  }

  expect_failures = [var.tls_certificate_pfx_path]
}

run "rejects_a_reserved_postgresql_admin_login" {
  command = plan

  variables {
    postgresql_admin_login = "azure_superuser"
  }

  expect_failures = [var.postgresql_admin_login]
}
