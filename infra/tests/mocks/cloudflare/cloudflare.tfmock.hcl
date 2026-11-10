# Mock data for the Cloudflare provider: a zone ID and its account, and a small subset of Cloudflare's real IPv4 edge
# ranges. cloudflare_account_api_token_permission_groups_list isn't mocked: mocks can't return a list of permission
# groups (computed lists come back empty), so the email-sending module takes the catalogue as an input instead.

mock_data "cloudflare_zone" {
  defaults = {
    id = "0123456789abcdef0123456789abcdef"
    account = {
      id   = "abcdef0123456789abcdef0123456789"
      name = "Mock account"
    }
  }
}

mock_data "cloudflare_ip_ranges" {
  defaults = {
    ipv4_cidrs = ["173.245.48.0/20", "103.21.244.0/22", "104.16.0.0/13"]
  }
}

mock_resource "cloudflare_dns_record" {
  defaults = {
    id = "fedcba9876543210fedcba9876543210"
  }
}

mock_resource "cloudflare_zone_setting" {
  defaults = {
    id = "tls_client_auth"
  }
}

mock_resource "cloudflare_ruleset" {
  defaults = {
    id = "2f2feab2026849078ba485f918791bdc"
  }
}

mock_resource "cloudflare_email_sending_subdomain" {
  defaults = {
    id                 = "4444444444444444444444444444dddd"
    enabled            = true
    return_path_domain = "cf-bounce.biffkittz.com"
  }
}

# Not yet verified: the recipient hasn't followed Cloudflare's confirmation link.
mock_resource "cloudflare_email_routing_address" {
  defaults = {
    id       = "5555555555555555555555555555eeee"
    verified = null
  }
}

mock_resource "cloudflare_account_token" {
  defaults = {
    id     = "6666666666666666666666666666ffff"
    value  = "mock-cloudflare-email-sending-token"
    status = "active"
  }
}
