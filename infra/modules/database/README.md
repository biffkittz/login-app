# database

Azure Database for PostgreSQL flexible server with **private access (VNet integration)**:

- The server gets a private IP in a delegated subnet and has no public endpoint.
- A private DNS zone (`<server>.private.postgres.database.azure.com`), linked to the VNet, makes
  `<server>.postgres.database.azure.com` resolve to that IP. Connection strings therefore keep the real host name, and
  `SSL Mode=VerifyFull` works.

Flexible server doesn't support virtual network service endpoints. Its only network models are this one and public
access with a private endpoint. Both keep traffic on Microsoft's network; this one also removes the public endpoint.

The module also creates the application database (plus any in `additional_database_names`, one per app instance) and pins `require_secure_transport = on`. Azure's choice of
availability zone is ignored, so later plans don't try to move the server.

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
| [azurerm_postgresql_flexible_server.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server) | resource |
| [azurerm_postgresql_flexible_server_configuration.require_secure_transport](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server_configuration) | resource |
| [azurerm_postgresql_flexible_server_database.additional](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server_database) | resource |
| [azurerm_postgresql_flexible_server_database.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server_database) | resource |
| [azurerm_private_dns_zone.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_dns_zone) | resource |
| [azurerm_private_dns_zone_virtual_network_link.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/private_dns_zone_virtual_network_link) | resource |

### Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_administrator_login"></a> [administrator\_login](#input\_administrator\_login) | Login name of the server administrator. | `string` | n/a | yes |
| <a name="input_administrator_password"></a> [administrator\_password](#input\_administrator\_password) | Password of the server administrator. | `string` | n/a | yes |
| <a name="input_backup_retention_days"></a> [backup\_retention\_days](#input\_backup\_retention\_days) | Days of point-in-time-restore backups to keep (7-35). | `number` | n/a | yes |
| <a name="input_database_name"></a> [database\_name](#input\_database\_name) | Name of the application database. | `string` | n/a | yes |
| <a name="input_delegated_subnet_id"></a> [delegated\_subnet\_id](#input\_delegated\_subnet\_id) | Subnet delegated to Microsoft.DBforPostgreSQL/flexibleServers, where the server gets its private IP. | `string` | n/a | yes |
| <a name="input_location"></a> [location](#input\_location) | Azure region of the server. Must match the region of the virtual network. | `string` | n/a | yes |
| <a name="input_postgresql_version"></a> [postgresql\_version](#input\_postgresql\_version) | PostgreSQL major version. | `string` | n/a | yes |
| <a name="input_resource_group_name"></a> [resource\_group\_name](#input\_resource\_group\_name) | Resource group that holds the server and its private DNS zone. | `string` | n/a | yes |
| <a name="input_server_name"></a> [server\_name](#input\_server\_name) | Globally unique server name (3-63 lowercase letters, digits and hyphens). Clients connect to <server\_name>.postgres.database.azure.com. | `string` | n/a | yes |
| <a name="input_sku_name"></a> [sku\_name](#input\_sku\_name) | Compute SKU in tier\_family\_size form, e.g. B\_Standard\_B1ms (Burstable, 1 vCore, 2 GiB). | `string` | n/a | yes |
| <a name="input_storage_mb"></a> [storage\_mb](#input\_storage\_mb) | Allocated storage in MB. | `number` | n/a | yes |
| <a name="input_tags"></a> [tags](#input\_tags) | Tags applied to every taggable resource in this module. | `map(string)` | n/a | yes |
| <a name="input_virtual_network_id"></a> [virtual\_network\_id](#input\_virtual\_network\_id) | Virtual network to link the private DNS zone to, so clients in it resolve the server's private IP. | `string` | n/a | yes |
| <a name="input_additional_database_names"></a> [additional\_database\_names](#input\_additional\_database\_names) | More databases on the same server, one per additional instance of the app: map of a static key (the instance) to the database name. | `map(string)` | `{}` | no |

### Outputs

| Name | Description |
| ---- | ----------- |
| <a name="output_additional_database_names"></a> [additional\_database\_names](#output\_additional\_database\_names) | Names of the additional databases, keyed like additional\_database\_names. Referencing them makes consumers wait until the databases exist. |
| <a name="output_administrator_login"></a> [administrator\_login](#output\_administrator\_login) | Login name of the server administrator. |
| <a name="output_database_name"></a> [database\_name](#output\_database\_name) | Name of the application database. |
| <a name="output_fqdn"></a> [fqdn](#output\_fqdn) | Host name for connection strings. It resolves to the private IP inside the linked virtual network. |
| <a name="output_server_id"></a> [server\_id](#output\_server\_id) | Resource ID of the server. |
| <a name="output_server_name"></a> [server\_name](#output\_server\_name) | Name of the server. |
<!-- END_TF_DOCS -->
