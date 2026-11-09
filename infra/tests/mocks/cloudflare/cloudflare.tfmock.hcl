# Mock data for the Cloudflare provider: a zone ID and a small subset of Cloudflare's real IPv4 edge ranges.

mock_data "cloudflare_zone" {
  defaults = {
    id = "0123456789abcdef0123456789abcdef"
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
