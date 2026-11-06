# registry

Azure Container Registry for the app's images. The admin user is disabled; pulls use managed identities with
`AcrPull` (`pull_principal_ids`). The module waits `rbac_propagation_wait` after creating the role assignments, and
`login_server` becomes available only after that wait, so a Container App created in the same apply doesn't fail its
first image pull.

The Basic SKU has no network rules or private endpoints, so the registry endpoint is public; every push and pull
still requires a Microsoft Entra ID token. Premium adds both.

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
| [azurerm_container_registry.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_registry) | resource |
| [azurerm_role_assignment.acr_pull](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/role_assignment) | resource |
| [time_sleep.acr_pull_propagation](https://registry.terraform.io/providers/hashicorp/time/latest/docs/resources/sleep) | resource |

### Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_location"></a> [location](#input\_location) | Azure region of the registry. | `string` | n/a | yes |
| <a name="input_name"></a> [name](#input\_name) | Globally unique registry name: 5-50 alphanumeric characters. | `string` | n/a | yes |
| <a name="input_resource_group_name"></a> [resource\_group\_name](#input\_resource\_group\_name) | Resource group that holds the registry. | `string` | n/a | yes |
| <a name="input_tags"></a> [tags](#input\_tags) | Tags applied to the registry. | `map(string)` | n/a | yes |
| <a name="input_pull_principal_ids"></a> [pull\_principal\_ids](#input\_pull\_principal\_ids) | Principals (managed identities) granted AcrPull, keyed by a static name such as "app". | `map(string)` | `{}` | no |
| <a name="input_rbac_propagation_wait"></a> [rbac\_propagation\_wait](#input\_rbac\_propagation\_wait) | How long to wait after creating role assignments before anything relies on them, e.g. "60s". | `string` | `"60s"` | no |
| <a name="input_sku"></a> [sku](#input\_sku) | Registry SKU. Basic has no network rules or private endpoints; Premium adds both. | `string` | `"Basic"` | no |

### Outputs

| Name | Description |
| ---- | ----------- |
| <a name="output_id"></a> [id](#output\_id) | Resource ID of the registry. |
| <a name="output_login_server"></a> [login\_server](#output\_login\_server) | Registry host name, e.g. crloginappdemo1a2b3c.azurecr.io. Available once AcrPull assignments have propagated. |
| <a name="output_name"></a> [name](#output\_name) | Name of the registry (for az acr commands). |
<!-- END_TF_DOCS -->
