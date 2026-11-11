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
    condition     = output.app_image == null && length(module.container_apps.app_ids) == 0
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

run "dns_record_is_proxied_through_cloudflare" {
  command = plan

  assert {
    condition = (
      cloudflare_dns_record.app["main"].name == "app.biffkittz.com" &&
      cloudflare_dns_record.app["main"].type == "A" &&
      cloudflare_dns_record.app["main"].proxied == true &&
      cloudflare_dns_record.app["main"].ttl == 1 &&
      cloudflare_dns_record.app["main"].zone_id == "0123456789abcdef0123456789abcdef"
    )
    error_message = "app.biffkittz.com must be a proxied A record in the biffkittz.com zone."
  }

  assert {
    condition     = strcontains(cloudflare_dns_record.app["main"].comment, "project:loginapp") && length(cloudflare_dns_record.app["main"].comment) <= 100
    error_message = "The record comment must carry the project tag and fit Cloudflare's 100-character limit."
  }
}

run "cloudflare_presents_its_client_certificate_by_default" {
  command = plan

  assert {
    condition = (
      one(cloudflare_zone_setting.authenticated_origin_pulls[*].setting_id) == "tls_client_auth" &&
      one(cloudflare_zone_setting.authenticated_origin_pulls[*].value) == "on"
    )
    error_message = "Authenticated Origin Pulls must be on, or the app would refuse every request."
  }
}

run "cloudflare_reaches_the_app_on_its_exposed_port" {
  command = plan

  assert {
    condition = (
      cloudflare_ruleset.origin_port.phase == "http_request_origin" &&
      length(cloudflare_ruleset.origin_port.rules) == 1 &&
      cloudflare_ruleset.origin_port.rules[0].action == "route" &&
      cloudflare_ruleset.origin_port.rules[0].expression == "http.host eq \"app.biffkittz.com\"" &&
      cloudflare_ruleset.origin_port.rules[0].action_parameters.origin.port == 8443
    )
    error_message = "An Origin Rule must send app.biffkittz.com traffic to the app's exposed port (8443 by default)."
  }
}

run "client_certificate_requirement_can_be_turned_off" {
  command = plan

  variables {
    require_cloudflare_client_certificate = false
  }

  assert {
    condition     = length(cloudflare_zone_setting.authenticated_origin_pulls) == 0
    error_message = "Without the requirement, Terraform must leave the zone's Authenticated Origin Pulls setting alone."
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
    condition     = module.container_apps.app_ids["main"] != null
    error_message = "With app_image_tag set, the Container App must be created."
  }

  assert {
    condition     = output.app_image == "crmock.azurecr.io/login-app:1a2b3c4"
    error_message = "The app must run <registry>/login-app:<app_image_tag>."
  }

  assert {
    condition     = output.container_app_fqdn == "ca-loginapp-demo.mockdomain-1a2b3c4d.eastus.azurecontainerapps.io"
    error_message = "The app's FQDN must be <app name>.<environment default domain>."
  }

  assert {
    condition     = cloudflare_dns_record.app["main"].content == output.origin_public_ip && output.origin_public_ip == "203.0.113.20"
    error_message = "The DNS record must point at the Container Apps environment's public IP."
  }
}

run "pictures_are_unavailable_without_a_gemini_key" {
  command = apply

  variables {
    app_image_tag = "1a2b3c4"
  }

  assert {
    condition     = !contains(keys(module.key_vault.secret_ids), "gemini_api_key")
    error_message = "Without a Gemini API key there must be no Key Vault secret for one (the app then offers no pictures)."
  }

  assert {
    condition     = !contains(keys(local.app_secret_environment_variables), "ImageGeneration__ApiKey")
    error_message = "Without a Gemini API key the app must get no ImageGeneration__ApiKey variable."
  }
}

run "a_gemini_key_reaches_the_app_through_key_vault" {
  command = apply

  variables {
    app_image_tag  = "1a2b3c4"
    gemini_api_key = "test-gemini-key"
  }

  assert {
    condition     = contains(keys(module.key_vault.secret_ids), "gemini_api_key")
    error_message = "A Gemini API key must be stored in Key Vault, like the app's other secrets."
  }

  assert {
    condition     = local.app_secrets["main"]["gemini-api-key"] == module.key_vault.secret_ids["gemini_api_key"]
    error_message = "The Container App must reference the key's Key Vault secret."
  }

  assert {
    condition     = local.app_secret_environment_variables["ImageGeneration__ApiKey"] == "gemini-api-key"
    error_message = "The app reads the key from ImageGeneration__ApiKey (configuration key ImageGeneration:ApiKey)."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Registration emails (Cloudflare Email Service)
# ---------------------------------------------------------------------------------------------------------------------

run "registration_emails_are_off_without_recipients" {
  command = plan

  variables {
    app_image_tag = "1a2b3c4"
  }

  assert {
    condition     = length(module.email_sending) == 0 && length(data.cloudflare_account_api_token_permission_groups_list.this) == 0
    error_message = "Without recipients, Cloudflare Email Service must be left unconfigured."
  }

  assert {
    condition = (
      !contains(keys(local.app_secret_environment_variables), "Email__ApiToken") &&
      length(local.registration_notification_settings) == 0 &&
      !contains(keys(local.app_instance_environment_variables["main"]), "RegistrationNotifications__SiteUrl")
    )
    error_message = "Without recipients, the app must get no email settings."
  }

  assert {
    condition     = output.registration_notifications == null && !contains(keys(output.next_steps), "confirm_recipients")
    error_message = "Without recipients there is nothing to report or confirm."
  }
}

# The module's own runs (tests/email-sending.tftest.hcl) cover what it creates in Cloudflare. Here its outputs are
# fixed, as mocks can't fill the permission catalogue it chooses from, and the run checks how they reach the app.
run "registration_emails_reach_every_instance_through_key_vault" {
  command = apply

  variables {
    app_image_tag                        = "1a2b3c4"
    registration_notification_recipients = ["Owner@Example.com", "second@example.org"]
    additional_app_instances = {
      grocery = {
        hostname      = "grocery.biffkittz.com"
        exposed_port  = 8444
        database_name = "grocery"
      }
    }
  }

  override_module {
    target = module.email_sending
    outputs = {
      api_token        = "mock-cloudflare-email-sending-token"
      token_name       = "loginapp-demo app: send registration emails (managed by Terraform, project:loginapp)"
      permission_group = "Email Sending Send"
      sending_domain   = "biffkittz.com"
      recipients       = { "owner@example.com" = "awaiting confirmation", "second@example.org" = "awaiting confirmation" }
    }
  }

  assert {
    condition = (
      contains(keys(module.key_vault.secret_ids), "email_api_token") &&
      local.app_secrets["main"]["email-api-token"] == module.key_vault.secret_ids["email_api_token"] &&
      local.app_secrets["grocery"]["email-api-token"] == module.key_vault.secret_ids["email_api_token"] &&
      local.app_secret_environment_variables["Email__ApiToken"] == "email-api-token"
    )
    error_message = "The token must reach every instance through Key Vault, as Email__ApiToken."
  }

  assert {
    condition = local.registration_notification_settings == tomap({
      Email__AccountId                         = "abcdef0123456789abcdef0123456789"
      Email__SenderAddress                     = "notifications@biffkittz.com"
      RegistrationNotifications__Recipients__0 = "owner@example.com"
      RegistrationNotifications__Recipients__1 = "second@example.org"
    })
    error_message = "Every instance must send from notifications@biffkittz.com, for the zone's account, to the recipients in lower case and in order."
  }

  assert {
    condition = (
      local.app_instance_environment_variables["main"]["RegistrationNotifications__SiteUrl"] == "https://app.biffkittz.com" &&
      local.app_instance_environment_variables["grocery"]["RegistrationNotifications__SiteUrl"] == "https://grocery.biffkittz.com" &&
      local.app_instance_environment_variables["grocery"]["Jwt__Audience"] == "grocery.biffkittz.com"
    )
    error_message = "Each instance's emails must name its own site, and the grocery instance keeps its own audience."
  }

  assert {
    condition = (
      output.registration_notifications.sender == "notifications@biffkittz.com" &&
      output.registration_notifications.recipients["owner@example.com"] == "awaiting confirmation" &&
      output.registration_notifications.permission == "Email Sending Send" &&
      strcontains(output.next_steps.confirm_recipients, "owner@example.com, second@example.org")
    )
    error_message = "The outputs must show the sender, and who is still to confirm their address with Cloudflare."
  }
}

# ---------------------------------------------------------------------------------------------------------------------
# Input validation
# ---------------------------------------------------------------------------------------------------------------------

# A second instance of the app, as in environments/demo.tfvars: its own Container App, host name and database, the same
# image, identity and secrets.
run "a_second_instance_gets_its_own_app_host_name_and_database" {
  command = apply

  variables {
    app_image_tag = "1a2b3c4"
    additional_app_instances = {
      grocery = {
        hostname      = "grocery.biffkittz.com"
        exposed_port  = 8444
        database_name = "grocery"
      }
    }
  }

  assert {
    condition = (
      module.container_apps.app_names == { main = "ca-loginapp-demo", grocery = "ca-loginapp-demo-grocery" } &&
      length(module.container_apps.app_ids) == 2
    )
    error_message = "Each instance must get a Container App; the main one keeps its name."
  }

  assert {
    condition = (
      cloudflare_dns_record.app["grocery"].name == "grocery.biffkittz.com" &&
      cloudflare_dns_record.app["grocery"].proxied == true &&
      cloudflare_dns_record.app["grocery"].content == cloudflare_dns_record.app["main"].content &&
      cloudflare_dns_record.app["main"].name == "app.biffkittz.com"
    )
    error_message = "Each instance's host name must be a proxied record for the environment's one public IP."
  }

  assert {
    condition = (
      length(cloudflare_ruleset.origin_port.rules) == 2 &&
      cloudflare_ruleset.origin_port.rules[0].ref == "loginapp_demo_app_origin_port" &&
      cloudflare_ruleset.origin_port.rules[0].action_parameters.origin.port == 8443 &&
      cloudflare_ruleset.origin_port.rules[1].expression == "http.host eq \"grocery.biffkittz.com\"" &&
      cloudflare_ruleset.origin_port.rules[1].action_parameters.origin.port == 8444
    )
    error_message = "Cloudflare must send each host name to its instance's port, with the main instance's rule first and unchanged."
  }

  assert {
    condition     = module.database.additional_database_names == { grocery = "grocery" }
    error_message = "The second instance must get its own database on the existing server."
  }

  assert {
    condition = (
      local.app_secrets["grocery"]["db-connection-string"] == module.key_vault.secret_ids["grocery_db_connection_string"] &&
      local.app_secrets["main"]["db-connection-string"] == module.key_vault.secret_ids["db_connection_string"] &&
      local.app_secrets["grocery"]["jwt-signing-key"] == local.app_secrets["main"]["jwt-signing-key"] &&
      local.app_secrets["grocery"]["origin-certificate"] == local.app_secrets["main"]["origin-certificate"]
    )
    error_message = "The instances must share the Key Vault secrets, except for a connection string each."
  }

  assert {
    condition = (
      strcontains(local.app_connection_strings["grocery"], "Database=grocery;") &&
      strcontains(local.app_connection_strings["main"], "Database=loginapp;")
    )
    error_message = "Each connection string must name its instance's own database."
  }

  assert {
    condition = (
      local.app_instance_environment_variables["grocery"]["Jwt__Audience"] == "grocery.biffkittz.com" &&
      length(local.app_instance_environment_variables["grocery"]) == 1 &&
      length(local.app_instance_environment_variables["main"]) == 0
    )
    error_message = "Sessions must not carry over between instances (own token audience), while the main instance's stay valid."
  }

  assert {
    condition = (
      output.app_instances["grocery"].url == "https://grocery.biffkittz.com" &&
      output.app_instances["grocery"].database_name == "grocery" &&
      output.app_instances["main"].container_app_name == "ca-loginapp-demo"
    )
    error_message = "The outputs must list every instance."
  }
}

run "rejects_an_instance_on_the_main_instances_port" {
  command = plan

  variables {
    additional_app_instances = {
      grocery = { hostname = "grocery.biffkittz.com", exposed_port = 8443, database_name = "grocery" }
    }
  }

  expect_failures = [var.additional_app_instances]
}

run "rejects_an_instance_outside_the_dns_zone" {
  command = plan

  variables {
    additional_app_instances = {
      grocery = { hostname = "grocery.example.com", exposed_port = 8444, database_name = "grocery" }
    }
  }

  expect_failures = [var.additional_app_instances]
}

run "rejects_an_instance_sharing_the_main_database" {
  command = plan

  variables {
    additional_app_instances = {
      grocery = { hostname = "grocery.biffkittz.com", exposed_port = 8444, database_name = "loginapp" }
    }
  }

  expect_failures = [var.additional_app_instances]
}

run "rejects_an_instance_called_main" {
  command = plan

  variables {
    additional_app_instances = {
      main = { hostname = "grocery.biffkittz.com", exposed_port = 8444, database_name = "grocery" }
    }
  }

  expect_failures = [var.additional_app_instances]
}

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

run "rejects_a_second_replica_that_realtime_updates_would_not_reach" {
  command = plan

  variables {
    app_max_replicas = 2
  }

  expect_failures = [var.app_max_replicas]
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

run "rejects_the_ports_held_by_the_environments_http_ingress" {
  command = plan

  variables {
    app_exposed_port = 443
  }

  expect_failures = [var.app_exposed_port]
}

run "rejects_the_port_reserved_for_container_apps_health_checks" {
  command = plan

  variables {
    app_exposed_port = 36985
  }

  expect_failures = [var.app_exposed_port]
}

run "rejects_a_malformed_notification_recipient" {
  command = plan

  variables {
    registration_notification_recipients = ["Owner <owner@example.com>"]
  }

  expect_failures = [var.registration_notification_recipients]
}

run "rejects_a_notification_recipient_listed_twice" {
  command = plan

  variables {
    registration_notification_recipients = ["owner@example.com", "OWNER@example.com"]
  }

  expect_failures = [var.registration_notification_recipients]
}

run "rejects_a_sender_username_that_is_not_an_address_part" {
  command = plan

  variables {
    notification_sender_username = "notifications@biffkittz.com"
  }

  expect_failures = [var.notification_sender_username]
}

run "rejects_an_empty_gemini_key" {
  command = plan

  variables {
    gemini_api_key = " "
  }

  expect_failures = [var.gemini_api_key]
}

run "rejects_a_reserved_postgresql_admin_login" {
  command = plan

  variables {
    postgresql_admin_login = "azure_superuser"
  }

  expect_failures = [var.postgresql_admin_login]
}

run "rejects_a_key_vault_administrator_that_is_not_an_object_id" {
  command = plan

  variables {
    key_vault_administrator_object_id = "me@example.com"
  }

  expect_failures = [var.key_vault_administrator_object_id]
}

run "rejects_an_additional_key_vault_administrator_that_is_not_an_object_id" {
  command = plan

  variables {
    key_vault_additional_administrators = { github_deploy = "id-loginapp-github-deploy" }
  }

  expect_failures = [var.key_vault_additional_administrators]
}

run "rejects_the_key_vault_administrator_listed_again_as_an_additional_one" {
  command = plan

  variables {
    key_vault_administrator_object_id   = "22222222-2222-2222-2222-222222222222"
    key_vault_additional_administrators = { me = "22222222-2222-2222-2222-222222222222" }
  }

  expect_failures = [var.key_vault_additional_administrators]
}

# ---------------------------------------------------------------------------------------------------------------------
# Observability (Prometheus and Grafana)
# ---------------------------------------------------------------------------------------------------------------------

run "observability_is_off_by_default" {
  command = plan

  assert {
    condition = (
      length(module.observability) == 0 &&
      length(cloudflare_dns_record.grafana) == 0 &&
      length(cloudflare_ruleset.origin_port.rules) == 1 &&
      output.observability == null
    )
    error_message = "Without observability_enabled there must be no Prometheus, Grafana, DNS record or Origin Rule for them."
  }

  assert {
    condition = (
      length(local.app_telemetry_settings) == 0 &&
      !contains(keys(local.app_secret_environment_variables), "Telemetry__OtlpPassword") &&
      !contains(keys(local.app_secrets["main"]), "telemetry-otlp-password")
    )
    error_message = "Without observability_enabled the app must export no metrics."
  }
}

run "observability_adds_grafana_behind_cloudflare_and_points_the_app_at_prometheus" {
  command = apply

  variables {
    app_image_tag                = "1a2b3c4"
    observability_enabled        = true
    grafana_github_client_id     = "Ov23liAbCdEfGhIjKlMn"
    grafana_github_client_secret = "0123456789abcdef0123456789abcdef01234567"
    grafana_github_admins        = ["biffkittz"]
  }

  assert {
    condition = (
      cloudflare_dns_record.grafana[0].name == "grafana.biffkittz.com" &&
      cloudflare_dns_record.grafana[0].proxied == true &&
      cloudflare_dns_record.grafana[0].content == output.origin_public_ip
    )
    error_message = "grafana.biffkittz.com must be a proxied record for the environment's public IP."
  }

  assert {
    condition = alltrue([
      for comment in concat([for record in cloudflare_dns_record.app : record.comment], [cloudflare_dns_record.grafana[0].comment]) :
      length(comment) <= 100
    ])
    error_message = "Cloudflare refuses DNS record comments over 100 characters (error 9313)."
  }

  assert {
    condition = (
      length(cloudflare_ruleset.origin_port.rules) == 2 &&
      cloudflare_ruleset.origin_port.rules[0].ref == "loginapp_demo_app_origin_port" &&
      cloudflare_ruleset.origin_port.rules[1].ref == "loginapp_demo_grafana_origin_port" &&
      cloudflare_ruleset.origin_port.rules[1].expression == "http.host eq \"grafana.biffkittz.com\"" &&
      cloudflare_ruleset.origin_port.rules[1].action_parameters.origin.port == 8445
    )
    error_message = "Cloudflare must send grafana.biffkittz.com to Grafana's port, after the app's rules, which stay as they were."
  }

  assert {
    condition = (
      local.app_telemetry_settings["Telemetry__OtlpEndpoint"] == "https://ca-loginapp-demo-prometheus.internal.mockdomain-1a2b3c4d.eastus.azurecontainerapps.io/api/v1/otlp/v1/metrics" &&
      local.app_telemetry_settings["Telemetry__OtlpUsername"] == "app" &&
      local.app_secret_environment_variables["Telemetry__OtlpPassword"] == "telemetry-otlp-password" &&
      local.app_secrets["main"]["telemetry-otlp-password"] == module.key_vault.secret_ids["prometheus_app_password"] &&
      local.app_instance_environment_variables["main"]["Telemetry__Site"] == "app.biffkittz.com"
    )
    error_message = "The app must push to Prometheus' internal OTLP receiver as its user \"app\", with its password from Key Vault and its host name as the site."
  }

  assert {
    condition = alltrue([
      for key in ["prometheus_app_password", "prometheus_web_config", "prometheus_self_scrape_password", "prometheus_grafana_password", "grafana_github_client_secret", "grafana_secret_key"] :
      contains(keys(module.key_vault.secret_ids), key)
    ])
    error_message = "Every observability secret must be kept in Key Vault."
  }

  assert {
    condition = (
      length(random_password.prometheus_users) == 3 &&
      alltrue([for password in random_password.prometheus_users : password.length == 40 && !password.special])
    )
    error_message = "Prometheus' users need long, colon-free generated passwords."
  }

  assert {
    condition = (
      output.observability.grafana_url == "https://grafana.biffkittz.com" &&
      output.observability.github_callback_url == "https://grafana.biffkittz.com/login/github" &&
      output.observability.prometheus_app_name == "ca-loginapp-demo-prometheus" &&
      output.observability.grafana_app_name == "ca-loginapp-demo-grafana" &&
      can(regex("^stloginappdemo[0-9a-f]{6}$", output.observability.storage_account_name))
    )
    error_message = "The outputs must name Grafana's URL, the OAuth callback and the observability resources."
  }
}

run "rejects_observability_without_a_github_oauth_app" {
  command = plan

  variables {
    observability_enabled = true
    grafana_github_admins = ["biffkittz"]
  }

  expect_failures = [var.grafana_github_client_id, var.grafana_github_client_secret]
}

run "rejects_observability_without_a_grafana_administrator" {
  command = plan

  variables {
    observability_enabled        = true
    grafana_github_client_id     = "Ov23liAbCdEfGhIjKlMn"
    grafana_github_client_secret = "0123456789abcdef0123456789abcdef01234567"
  }

  expect_failures = [var.grafana_github_admins]
}

run "rejects_a_github_username_that_cannot_exist" {
  command = plan

  variables {
    grafana_github_viewers = ["-not-a-user-"]
  }

  expect_failures = [var.grafana_github_viewers]
}

run "rejects_grafana_on_an_app_instances_port" {
  command = plan

  variables {
    grafana_exposed_port = 8443
  }

  expect_failures = [var.grafana_exposed_port]
}

run "rejects_grafana_on_an_app_instances_host_name" {
  command = plan

  variables {
    grafana_hostname = "app.biffkittz.com"
  }

  expect_failures = [var.grafana_hostname]
}

run "rejects_an_instance_named_like_an_observability_app" {
  command = plan

  variables {
    additional_app_instances = {
      grafana = {
        hostname      = "dashboards.biffkittz.com"
        exposed_port  = 8446
        database_name = "dashboards"
      }
    }
  }

  expect_failures = [var.additional_app_instances]
}
