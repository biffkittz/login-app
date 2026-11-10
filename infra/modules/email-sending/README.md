# email-sending

Sending email from a Cloudflare zone with [Cloudflare Email Service](https://developers.cloudflare.com/email-service/):
the app emails everyone in `recipients` whenever someone registers.

- **The domain** is onboarded for Email Sending. Cloudflare adds and locks the records this needs, outside Terraform:
  MX and SPF on `cf-bounce.<domain>`, DKIM at `cf-bounce._domainkey.<domain>`, and DMARC at `_dmarc.<domain>`. The
  domain's own MX and SPF records (mail hosted elsewhere) are untouched.
- **Each recipient** becomes a verified destination address of the account. Cloudflare emails it a confirmation link
  once; until its owner follows it, sending to it may be refused on the Workers Free plan. Sending to verified
  addresses is free on every plan.
- **The token** is account-owned and limited to Email Sending (nothing narrower can send, so it can also manage
  sending domains). It gets the first of `preferred_permission_groups` found in
  `permission_groups` (Cloudflare's catalogue, passed in by the root module, which reads it with
  `cloudflare_account_api_token_permission_groups_list`); a plan fails, naming the groups found, when none match.
  The catalogue is an input rather than read here so that the choice can be tested: `terraform test` mocks can't
  return a list from a data source.

<!-- BEGIN_TF_DOCS -->
<!-- END_TF_DOCS -->
