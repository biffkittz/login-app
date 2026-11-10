output "api_token" {
  description = "The app's token for sending email: Authorization: Bearer <token> on Cloudflare's email sending API."
  value       = cloudflare_account_token.this.value
  sensitive   = true
}

output "token_name" {
  description = "Name of the app's API token, as the Cloudflare dashboard lists it."
  value       = cloudflare_account_token.this.name
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
