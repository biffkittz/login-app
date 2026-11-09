output "id" {
  description = "Resource ID of the vault."
  value       = azurerm_key_vault.this.id
}

output "name" {
  description = "Name of the vault."
  value       = azurerm_key_vault.this.name
}

output "vault_uri" {
  description = "Data-plane URI of the vault, e.g. https://kv-example.vault.azure.net/."
  value       = azurerm_key_vault.this.vault_uri
}

output "secret_ids" {
  description = "Versionless secret URIs keyed like secret_names. Consumers then always resolve the latest version. Available once reader role assignments have propagated."
  value       = { for key, secret in azurerm_key_vault_secret.this : key => secret.versionless_id }

  depends_on = [time_sleep.reader_rbac]
}

output "certificate_secret_id" {
  description = "Versionless URI of the certificate's secret (a Base64, passwordless PFX), referenced by the Container App. Container Apps picks up new versions within 30 minutes and restarts the revision, so the app loads the new certificate."
  value       = azurerm_key_vault_certificate.tls.versionless_secret_id

  depends_on = [time_sleep.reader_rbac]
}

output "certificate_thumbprint" {
  description = "Thumbprint of the current certificate version."
  value       = azurerm_key_vault_certificate.tls.thumbprint
}
