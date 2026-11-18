# Tests for the email-sending module: what it creates in Cloudflare, and which permission the senders' tokens get. The
# Cloudflare provider is mocked, so nothing is created and no email is sent.
#
# Run from infra/:  terraform init -backend=false && terraform test

mock_provider "cloudflare" {
  source = "./tests/mocks/cloudflare"
}

variables {
  account_id = "abcdef0123456789abcdef0123456789"
  zone_id    = "0123456789abcdef0123456789abcdef"
  domain     = "biffkittz.com"
  recipients = ["owner@example.com", "second@example.org"]
  tokens = {
    app     = "loginapp-demo app: send registration emails"
    grafana = "loginapp-demo Grafana: send alert emails"
  }
  preferred_permission_groups = ["Email Sending Send", "Email Sending Write"]

  # A slice of Cloudflare's catalogue: both email sending groups, and one that has nothing to do with email.
  permission_groups = [
    { id = "1111111111111111111111111111aaaa", name = "DNS Write", is_selectable = true, scopes = ["com.cloudflare.api.account.zone"] },
    { id = "2222222222222222222222222222bbbb", name = "Email Sending Write", is_selectable = true, scopes = ["com.cloudflare.api.account"] },
    { id = "3333333333333333333333333333cccc", name = "Email Sending Send", is_selectable = true, scopes = ["com.cloudflare.api.account"] },
  ]
}

run "onboards_the_domain_without_previews_and_registers_each_recipient" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  assert {
    condition = (
      cloudflare_email_sending_subdomain.this.zone_id == "0123456789abcdef0123456789abcdef" &&
      cloudflare_email_sending_subdomain.this.name == "biffkittz.com" &&
      cloudflare_email_sending_subdomain.this.preview_enabled == false
    )
    error_message = "The domain must be onboarded for Email Sending, without previews that would copy the emails into Cloudflare's activity log."
  }

  assert {
    condition = (
      toset(keys(cloudflare_email_routing_address.recipients)) == toset(["owner@example.com", "second@example.org"]) &&
      alltrue([for address in cloudflare_email_routing_address.recipients : address.account_id == "abcdef0123456789abcdef0123456789"])
    )
    error_message = "Each recipient must become a destination address of the account."
  }
}

run "each_sender_gets_a_token_that_can_only_send_email_for_this_account" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  assert {
    condition = (
      cloudflare_account_token.this["app"].name == "loginapp-demo app: send registration emails" &&
      cloudflare_account_token.this["grafana"].name == "loginapp-demo Grafana: send alert emails"
    )
    error_message = "Each sender must get a token of its own, so either can be revoked alone."
  }

  assert {
    condition = alltrue([
      for token in cloudflare_account_token.this : (
        token.account_id == "abcdef0123456789abcdef0123456789" &&
        length(token.policies) == 1 &&
        token.policies[0].effect == "allow" &&
        [for group in token.policies[0].permission_groups : group.id] == ["3333333333333333333333333333cccc"] &&
        jsondecode(token.policies[0].resources) == { "com.cloudflare.api.account.abcdef0123456789abcdef0123456789" = "*" }
      )
    ])
    error_message = "Every token must hold the narrowest sending permission (Email Sending Send) alone, for this account alone."
  }

  assert {
    condition     = alltrue([for token in cloudflare_account_token.this : token.condition == null && token.expires_on == null])
    error_message = "The tokens have no IP condition or expiry: the senders' outbound addresses aren't fixed, and rotation is a replace."
  }
}

run "the_token_falls_back_to_the_broader_sending_permission" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  variables {
    permission_groups = [
      { id = "2222222222222222222222222222bbbb", name = "Email Sending Write", is_selectable = true, scopes = ["com.cloudflare.api.account"] },
    ]
  }

  assert {
    condition     = [for group in cloudflare_account_token.this["app"].policies[0].permission_groups : group.id] == ["2222222222222222222222222222bbbb"]
    error_message = "Without a send-only group, the token must get Email Sending Write."
  }
}

run "permission_groups_a_token_cannot_be_given_are_passed_over" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  variables {
    permission_groups = [
      { id = "3333333333333333333333333333cccc", name = "Email Sending Send", is_selectable = false, scopes = ["com.cloudflare.api.account"] },
      { id = "2222222222222222222222222222bbbb", name = "Email Sending Write" },
    ]
  }

  assert {
    condition     = [for group in cloudflare_account_token.this["app"].policies[0].permission_groups : group.id] == ["2222222222222222222222222222bbbb"]
    error_message = "A group the caller can't select must be skipped; one that doesn't say must be tried."
  }
}

run "a_zone_scoped_sending_permission_is_limited_to_the_zone" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  variables {
    permission_groups = [
      { id = "3333333333333333333333333333cccc", name = "Email Sending Send", is_selectable = true, scopes = ["com.cloudflare.api.account.zone"] },
    ]
  }

  assert {
    condition     = jsondecode(cloudflare_account_token.this["grafana"].policies[0].resources) == { "com.cloudflare.api.account.zone.0123456789abcdef0123456789abcdef" = "*" }
    error_message = "Should the permission be a zone one, the token must cover this zone alone."
  }
}

run "fails_clearly_when_the_account_cannot_send_email" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  variables {
    permission_groups = [
      { id = "1111111111111111111111111111aaaa", name = "DNS Write", is_selectable = true, scopes = ["com.cloudflare.api.account.zone"] },
    ]
  }

  expect_failures = [cloudflare_account_token.this["app"], cloudflare_account_token.this["grafana"]]
}

run "outputs_report_recipients_still_to_confirm" {
  command = apply

  module {
    source = "./modules/email-sending"
  }

  assert {
    condition     = output.recipients == { "owner@example.com" = "awaiting confirmation", "second@example.org" = "awaiting confirmation" }
    error_message = "Recipients that haven't followed Cloudflare's confirmation link must show as awaiting confirmation."
  }

  assert {
    condition = (
      nonsensitive(output.api_tokens) == { app = "mock-cloudflare-email-sending-token", grafana = "mock-cloudflare-email-sending-token" } &&
      output.token_names == { app = "loginapp-demo app: send registration emails", grafana = "loginapp-demo Grafana: send alert emails" } &&
      output.permission_group == "Email Sending Send" &&
      output.sending_domain == "biffkittz.com"
    )
    error_message = "The tokens, their names and permission, and the sending domain must be passed on."
  }
}

run "rejects_recipients_not_in_lower_case" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  variables {
    recipients = ["Owner@Example.com"]
  }

  expect_failures = [var.recipients]
}

run "rejects_an_empty_set_of_tokens" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  variables {
    tokens = {}
  }

  expect_failures = [var.tokens]
}

run "rejects_a_malformed_account_id" {
  command = plan

  module {
    source = "./modules/email-sending"
  }

  variables {
    account_id = "not-an-account"
  }

  expect_failures = [var.account_id]
}
