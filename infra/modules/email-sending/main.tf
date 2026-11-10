# Sending email from a Cloudflare zone through Cloudflare Email Service: the domain onboarded for Email Sending, the
# recipients as verified destination addresses, and an account token that can use Email Sending and nothing else.

# Onboards the domain for Email Sending, so the app may send from addresses in it. Cloudflare creates and locks the DNS
# records this needs, which stay outside Terraform: MX and SPF on cf-bounce.<domain> (bounces come back to Cloudflare
# there), the DKIM key at cf-bounce._domainkey.<domain>, and a DMARC policy at _dmarc.<domain>. The domain's own MX and
# SPF records, for mailboxes hosted elsewhere, are left alone.
resource "cloudflare_email_sending_subdomain" "this" {
  zone_id = var.zone_id
  name    = var.domain
  # The emails name the person who registered. Previews would keep a copy in Cloudflare's activity log.
  preview_enabled = false
}

# Each recipient becomes a verified destination address of the account. Cloudflare emails it a confirmation link when
# it is added (once). Confirmed addresses can be sent to on every plan, free and outside the sending quota; on the
# Workers Free plan they are the only addresses Email Sending reaches.
resource "cloudflare_email_routing_address" "recipients" {
  for_each = toset(var.recipients)

  account_id = var.account_id
  email      = each.value
}

locals {
  # Permission groups by name (a name can recur across scopes), leaving out those a token can't be given.
  permission_groups = {
    for group in var.permission_groups : group.name => group...
    if group.is_selectable != false
  }

  # The narrowest of preferred_permission_groups that the account offers, or null.
  permission_group = try([
    for name in var.preferred_permission_groups : local.permission_groups[name][0]
    if contains(keys(local.permission_groups), name)
  ][0], null)

  # Email Sending is an account permission. Should Cloudflare make it a zone one, the token is scoped to this zone.
  token_resources = contains(try(local.permission_group.scopes, []), "com.cloudflare.api.account.zone") ? {
    "com.cloudflare.api.account.zone.${var.zone_id}" = "*"
    } : {
    "com.cloudflare.api.account.${var.account_id}" = "*"
  }
}

# The app's own Cloudflare API token, limited to Email Sending. Cloudflare has nothing narrower that can send, so the
# token can also manage sending domains, but nothing outside Email Sending. Account-owned, so it belongs to no
# person's Cloudflare login and keeps working whoever leaves. Rotate it by replacing this resource.
resource "cloudflare_account_token" "this" {
  account_id = var.account_id
  name       = var.token_name
  policies = [{
    effect            = "allow"
    permission_groups = [{ id = try(local.permission_group.id, "") }]
    resources         = jsonencode(local.token_resources)
  }]

  lifecycle {
    precondition {
      condition     = local.permission_group != null
      error_message = "The Cloudflare account offers none of the permission groups in preferred_permission_groups (${join(", ", var.preferred_permission_groups)}). Email sending groups it does offer: ${coalesce(join(", ", [for name in keys(local.permission_groups) : name if strcontains(lower(name), "email sending")]), "none")}. Name the right one in cloudflare_email_sending_permission_groups; if none are offered, the account has no Email Sending yet (infra/docs/DEPLOYMENT.md)."
    }
  }
}
