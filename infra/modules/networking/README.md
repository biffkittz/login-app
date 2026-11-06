# networking

The virtual network and its three dedicated subnets, each with a network security group that allows only the
traffic its service needs:

| Subnet | Hosts | Delegation | Service endpoints | Inbound allowed (everything else from the VNet or internet is denied) |
| --- | --- | --- | --- | --- |
| `snet-appgw` | Application Gateway | none | `Microsoft.KeyVault` | TCP 443 from `gateway_ingress_allowed_cidrs` (Cloudflare) or the internet; GatewayManager 65200-65535; Azure Load Balancer |
| `snet-aca` | Container Apps environment | `Microsoft.App/environments` | `Microsoft.KeyVault` | TCP 80 and 31080 from `snet-appgw`; Azure Load Balancer; within the subnet |
| `snet-psql` | PostgreSQL flexible server | `Microsoft.DBforPostgreSQL/flexibleServers` | `Microsoft.Storage` (Azure adds it; declared so it isn't removed) | TCP 5432 from `snet-aca`; Azure Load Balancer; within the subnet |

Outbound traffic keeps the platform defaults. The `subnet_ids` output is available only after each subnet's NSG is
attached, so nothing is ever deployed into an unprotected subnet.

<!-- BEGIN_TF_DOCS -->
### Requirements

| Name | Version |
| ---- | ------- |
| <a name="requirement_terraform"></a> [terraform](#requirement\_terraform) | >= 1.17.1 |
| <a name="requirement_azurerm"></a> [azurerm](#requirement\_azurerm) | >= 5.12 |

### Providers

| Name | Version |
| ---- | ------- |
| <a name="provider_azurerm"></a> [azurerm](#provider\_azurerm) | >= 5.12 |

### Resources

| Name | Type |
| ---- | ---- |
| [azurerm_network_security_group.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/network_security_group) | resource |
| [azurerm_network_security_rule.inbound](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/network_security_rule) | resource |
| [azurerm_subnet.app_gateway](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet) | resource |
| [azurerm_subnet.container_apps](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet) | resource |
| [azurerm_subnet.postgresql](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet) | resource |
| [azurerm_subnet_network_security_group_association.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/subnet_network_security_group_association) | resource |
| [azurerm_virtual_network.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/virtual_network) | resource |

### Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_address_space"></a> [address\_space](#input\_address\_space) | Address space of the virtual network, in CIDR notation. | `list(string)` | n/a | yes |
| <a name="input_location"></a> [location](#input\_location) | Azure region for the virtual network and its network security groups. | `string` | n/a | yes |
| <a name="input_name"></a> [name](#input\_name) | Base name used in resource names, for example "loginapp-demo". | `string` | n/a | yes |
| <a name="input_resource_group_name"></a> [resource\_group\_name](#input\_resource\_group\_name) | Resource group that holds the network resources. | `string` | n/a | yes |
| <a name="input_subnet_address_prefixes"></a> [subnet\_address\_prefixes](#input\_subnet\_address\_prefixes) | Address prefix of each dedicated subnet. Each must lie inside address\_space and must not overlap the others. | <pre>object({<br/>    app_gateway    = string<br/>    container_apps = string<br/>    postgresql     = string<br/>  })</pre> | n/a | yes |
| <a name="input_tags"></a> [tags](#input\_tags) | Tags applied to every taggable resource in this module. | `map(string)` | n/a | yes |
| <a name="input_gateway_ingress_allowed_cidrs"></a> [gateway\_ingress\_allowed\_cidrs](#input\_gateway\_ingress\_allowed\_cidrs) | Source ranges allowed to reach Application Gateway on TCP 443. An empty list allows the whole internet. | `list(string)` | `[]` | no |

### Outputs

| Name | Description |
| ---- | ----------- |
| <a name="output_network_security_group_ids"></a> [network\_security\_group\_ids](#output\_network\_security\_group\_ids) | Network security group IDs, keyed like subnet\_ids. |
| <a name="output_subnet_address_prefixes"></a> [subnet\_address\_prefixes](#output\_subnet\_address\_prefixes) | Subnet address prefixes, keyed like subnet\_ids. |
| <a name="output_subnet_ids"></a> [subnet\_ids](#output\_subnet\_ids) | Subnet resource IDs, keyed app\_gateway, container\_apps and postgresql. Available once each subnet's NSG is attached. |
| <a name="output_virtual_network_id"></a> [virtual\_network\_id](#output\_virtual\_network\_id) | Resource ID of the virtual network. |
| <a name="output_virtual_network_name"></a> [virtual\_network\_name](#output\_virtual\_network\_name) | Name of the virtual network. |
<!-- END_TF_DOCS -->
