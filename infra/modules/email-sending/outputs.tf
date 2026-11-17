output "api_tokens" {
  description = "Each sender's token, by the keys of tokens: Authorization: Bearer <token> on Cloudflare's email sending API, or the password of SMTP user api_token at smtp.mx.cloudflare.net:465."
  value       = { for key, token in cloudflare_account_token.this : key => token.value }
  sensitive   = true
}

output "token_names" {
  description = "Each sender's token name, as the Cloudflare dashboard lists it, by the keys of tokens."
  value       = { for key, token in cloudflare_account_token.this : key => token.name }
}

output "permission_group" {
  description = "The permission group the token was given."
  value       = try(local.permission_group.name, null)
}

output "sending_domain" {
  description = "The domain onboarded for Email Sending."
  value       = cloudflare_email_sending_subdomain.this.name
}

output "recipients" {
  description = "Each recipient and whether its owner has confirmed it (\"verified\") or not yet (\"awaiting confirmation\"), as of the last refresh."
  value = {
    for address, destination in cloudflare_email_routing_address.recipients :
    address => destination.verified == null ? "awaiting confirmation" : "verified"
  }
}
