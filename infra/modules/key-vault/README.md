# key-vault

A Key Vault in Azure RBAC mode with purge protection. It holds:

- the secrets passed in `secret_names` / `secret_values`;
- a TLS certificate imported from a PFX file (`certificate_*`).

Access is least-privilege:

| Who | Role | Scope |
| --- | --- | --- |
| The principal running Terraform (`administrator_principal_id`) | Key Vault Secrets Officer, Key Vault Certificates Officer | vault |
| Each entry of `secret_readers` | Key Vault Secrets User | one secret |
| Each entry of `certificate_reader_principal_ids` | Key Vault Secrets User | the certificate's secret |

The module waits `rbac_propagation_wait` after creating role assignments. Its `secret_ids` and
`certificate_secret_id` outputs (versionless URIs) become available only after that wait, so consumers never race
role propagation. Audit logs and metrics go to the given Log Analytics workspace.

The firewall is opt-in (`firewall_enabled`); see [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md#key-vault-network-access)
for why it is off by default.

<!-- BEGIN_TF_DOCS -->
### Requirements

| Name | Version |
| ---- | ------- |
| <a name="requirement_terraform"></a> [terraform](#requirement\_terraform) | >= 1.17.1 |
| <a name="requirement_azurerm"></a> [azurerm](#requirement\_azurerm) | >= 5.12 |
| <a name="requirement_time"></a> [time](#requirement\_time) | >= 0.14 |

### Providers

| Name | Version |
| ---- | ------- |
| <a name="provider_azurerm"></a> [azurerm](#provider\_azurerm) | >= 5.12 |
| <a name="provider_time"></a> [time](#provider\_time) | >= 0.14 |

### Resources

| Name | Type |
| ---- | ---- |
| [azurerm_key_vault.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/key_vault) | resource |
| [azurerm_key_vault_certificate.tls](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/key_vault_certificate) | resource |
| [azurerm_key_vault_secret.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/key_vault_secret) | resource |
| [azurerm_monitor_diagnostic_setting.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/monitor_diagnostic_setting) | resource |
| [azurerm_role_assignment.administrator](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.certificate_reader](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [azurerm_role_assignment.secret_reader](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [time_sleep.administrator_rbac](https://registry.terraform.io/providers/hashicorp/time/latest/docs/resources/sleep) | resource |
| [time_sleep.reader_rbac](https://registry.terraform.io/providers/hashicorp/time/latest/docs/resources/sleep) | resource |

### Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_administrator_principal_id"></a> [administrator\_principal\_id](#input\_administrator\_principal\_id) | Object ID of the principal running Terraform. It gets Key Vault Secrets Officer and Certificates Officer so it can manage the vault's contents. | `string` | n/a | yes |
| <a name="input_certificate_name"></a> [certificate\_name](#input\_certificate\_name) | Name of the imported TLS certificate in the vault. | `string` | n/a | yes |
| <a name="input_certificate_pfx_base64"></a> [certificate\_pfx\_base64](#input\_certificate\_pfx\_base64) | Base64-encoded PFX (PKCS#12) file with the TLS certificate and its private key. | `string` | n/a | yes |
| <a name="input_location"></a> [location](#input\_location) | Azure region of the vault. | `string` | n/a | yes |
| <a name="input_log_analytics_workspace_id"></a> [log\_analytics\_workspace\_id](#input\_log\_analytics\_workspace\_id) | Log Analytics workspace that receives the vault's audit logs and metrics. | `string` | n/a | yes |
| <a name="input_name"></a> [name](#input\_name) | Globally unique vault name: 3-24 characters, letters, digits and hyphens, starting with a letter. | `string` | n/a | yes |
| <a name="input_resource_group_name"></a> [resource\_group\_name](#input\_resource\_group\_name) | Resource group that holds the vault. | `string` | n/a | yes |
| <a name="input_tags"></a> [tags](#input\_tags) | Tags applied to every taggable resource in this module. | `map(string)` | n/a | yes |
| <a name="input_tenant_id"></a> [tenant\_id](#input\_tenant\_id) | Microsoft Entra tenant that authenticates requests to the vault. | `string` | n/a | yes |
| <a name="input_allowed_ip_ranges"></a> [allowed\_ip\_ranges](#input\_allowed\_ip\_ranges) | Public ranges (CIDR, e.g. "203.0.113.10/32") allowed through the firewall: the machines that run Terraform. | `list(string)` | `[]` | no |
| <a name="input_allowed_subnet_ids"></a> [allowed\_subnet\_ids](#input\_allowed\_subnet\_ids) | Subnets (with the Microsoft.KeyVault service endpoint) allowed through the firewall. | `list(string)` | `[]` | no |
| <a name="input_certificate_password"></a> [certificate\_password](#input\_certificate\_password) | Password of the PFX file, or null when it has none. | `string` | `null` | no |
| <a name="input_certificate_reader_principal_ids"></a> [certificate\_reader\_principal\_ids](#input\_certificate\_reader\_principal\_ids) | Principals granted Key Vault Secrets User on the certificate's secret (the app), keyed by a static name. | `map(string)` | `{}` | no |
| <a name="input_firewall_enabled"></a> [firewall\_enabled](#input\_firewall\_enabled) | When true, the vault denies network access except from allowed\_ip\_ranges, allowed\_subnet\_ids and trusted Azure services. | `bool` | `false` | no |
| <a name="input_purge_protection_enabled"></a> [purge\_protection\_enabled](#input\_purge\_protection\_enabled) | Whether deleted vaults and objects can't be purged before the retention period ends. Can't be turned off once on. | `bool` | `true` | no |
| <a name="input_rbac_propagation_wait"></a> [rbac\_propagation\_wait](#input\_rbac\_propagation\_wait) | How long to wait after creating role assignments before anything relies on them, e.g. "60s". | `string` | `"60s"` | no |
| <a name="input_secret_names"></a> [secret\_names](#input\_secret\_names) | Secrets to create: map of a static key (used in secret\_readers) to the secret's name in the vault. | `map(string)` | `{}` | no |
| <a name="input_secret_readers"></a> [secret\_readers](#input\_secret\_readers) | Grants Key Vault Secrets User on a single secret. Map of a static key to the secret's key in secret\_names and the principal's object ID. | <pre>map(object({<br/>    secret       = string<br/>    principal_id = string<br/>  }))</pre> | `{}` | no |
| <a name="input_secret_values"></a> [secret\_values](#input\_secret\_values) | Secret values, keyed like secret\_names. | `map(string)` | `{}` | no |
| <a name="input_soft_delete_retention_days"></a> [soft\_delete\_retention\_days](#input\_soft\_delete\_retention\_days) | Days that deleted vaults and objects stay recoverable (7-90). Can only be set when the vault is created. | `number` | `7` | no |

### Outputs

| Name | Description |
| ---- | ----------- |
| <a name="output_certificate_secret_id"></a> [certificate\_secret\_id](#output\_certificate\_secret\_id) | Versionless URI of the certificate's secret (a Base64, passwordless PFX), referenced by the Container App. Container Apps picks up new versions within 30 minutes and restarts the revision, so the app loads the new certificate. |
| <a name="output_certificate_thumbprint"></a> [certificate\_thumbprint](#output\_certificate\_thumbprint) | Thumbprint of the current certificate version. |
| <a name="output_id"></a> [id](#output\_id) | Resource ID of the vault. |
| <a name="output_name"></a> [name](#output\_name) | Name of the vault. |
| <a name="output_secret_ids"></a> [secret\_ids](#output\_secret\_ids) | Versionless secret URIs keyed like secret\_names. Consumers then always resolve the latest version. Available once reader role assignments have propagated. |
| <a name="output_vault_uri"></a> [vault\_uri](#output\_vault\_uri) | Data-plane URI of the vault, e.g. https://kv-example.vault.azure.net/. |
<!-- END_TF_DOCS -->
