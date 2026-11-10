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
### Requirements

| Name | Version |
| ---- | ------- |
| <a name="requirement_terraform"></a> [terraform](#requirement\_terraform) | >= 1.17.1 |
| <a name="requirement_cloudflare"></a> [cloudflare](#requirement\_cloudflare) | >= 5.28 |

### Providers

| Name | Version |
| ---- | ------- |
| <a name="provider_cloudflare"></a> [cloudflare](#provider\_cloudflare) | >= 5.28 |

### Resources

| Name | Type |
| ---- | ---- |
| [cloudflare_account_token.this](https://registry.terraform.io/providers/cloudflare/cloudflare/latest/docs/resources/account_token) | resource |
| [cloudflare_email_routing_address.recipients](https://registry.terraform.io/providers/cloudflare/cloudflare/latest/docs/resources/email_routing_address) | resource |
| [cloudflare_email_sending_subdomain.this](https://registry.terraform.io/providers/cloudflare/cloudflare/latest/docs/resources/email_sending_subdomain) | resource |

### Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_account_id"></a> [account\_id](#input\_account\_id) | Cloudflare account that owns the zone: the recipients become its destination addresses and the token is its own. | `string` | n/a | yes |
| <a name="input_domain"></a> [domain](#input\_domain) | The domain to send from, onboarded for Email Sending: the zone apex (e.g. biffkittz.com) or a name inside it. | `string` | n/a | yes |
| <a name="input_permission_groups"></a> [permission\_groups](#input\_permission\_groups) | Cloudflare's catalogue of API token permission groups (the cloudflare\_account\_api\_token\_permission\_groups\_list data source's result), from which the token's permission is chosen. | <pre>list(object({<br/>    id            = string<br/>    name          = string<br/>    is_selectable = optional(bool)<br/>    scopes        = optional(list(string), [])<br/>  }))</pre> | n/a | yes |
| <a name="input_preferred_permission_groups"></a> [preferred\_permission\_groups](#input\_preferred\_permission\_groups) | Names of the permission groups that allow sending email, narrowest first. The token gets the first one in permission\_groups. | `list(string)` | n/a | yes |
| <a name="input_recipients"></a> [recipients](#input\_recipients) | Addresses that receive the emails, in lower case. Each becomes a verified destination address of the account once its owner follows the confirmation link Cloudflare sends it. | `list(string)` | n/a | yes |
| <a name="input_token_name"></a> [token\_name](#input\_token\_name) | Name of the app's API token, as the Cloudflare dashboard lists it. | `string` | n/a | yes |
| <a name="input_zone_id"></a> [zone\_id](#input\_zone\_id) | Cloudflare zone that holds domain. Cloudflare Email Service needs the domain's DNS to be on Cloudflare, where it adds the records sending needs. | `string` | n/a | yes |

### Outputs

| Name | Description |
| ---- | ----------- |
| <a name="output_api_token"></a> [api\_token](#output\_api\_token) | The app's token for sending email: Authorization: Bearer <token> on Cloudflare's email sending API. |
| <a name="output_permission_group"></a> [permission\_group](#output\_permission\_group) | The permission group the token was given. |
| <a name="output_recipients"></a> [recipients](#output\_recipients) | Each recipient and whether its owner has confirmed it ("verified") or not yet ("awaiting confirmation"), as of the last refresh. |
| <a name="output_sending_domain"></a> [sending\_domain](#output\_sending\_domain) | The domain onboarded for Email Sending. |
| <a name="output_token_name"></a> [token\_name](#output\_token\_name) | Name of the app's API token, as the Cloudflare dashboard lists it. |
<!-- END_TF_DOCS -->
