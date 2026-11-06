# application-gateway

Application Gateway Standard_v2: the public entry point behind Cloudflare.

- **Front end:** a static, zone-redundant Standard public IP with one HTTPS listener on 443 for `host_name`. TLS uses
  the `AppGwSslPolicy20220101S` policy (TLS 1.2 and 1.3, strong ciphers). The certificate is read from Key Vault with a
  user-assigned identity through a versionless secret URI, so new certificate versions are picked up automatically
  (the gateway checks every four hours).
- **Back end:** plain HTTP on port 80 to the Container App's private FQDN. The Host header is set to that FQDN,
  because the Container Apps ingress routes by host name.
- **Health and logs:** the probe checks `health_probe_path` on the same host, and access logs and metrics go to Log
  Analytics.
- **Scaling:** autoscaling between `autoscale.min_capacity` and `autoscale.max_capacity` capacity units.

There is no web application firewall at this layer. Moving to WAF_v2 means changing the SKU and attaching an
`azurerm_web_application_firewall_policy`.

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
| [azurerm_application_gateway.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/application_gateway) | resource |
| [azurerm_monitor_diagnostic_setting.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/monitor_diagnostic_setting) | resource |
| [azurerm_public_ip.this](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/public_ip) | resource |

### Inputs

| Name | Description | Type | Default | Required |
| ---- | ----------- | ---- | ------- | :------: |
| <a name="input_autoscale"></a> [autoscale](#input\_autoscale) | Autoscaling bounds in capacity units: min\_capacity 0 means no capacity is reserved (billed by use), max\_capacity is 2-125. | <pre>object({<br/>    min_capacity = number<br/>    max_capacity = number<br/>  })</pre> | n/a | yes |
| <a name="input_backend_fqdn"></a> [backend\_fqdn](#input\_backend\_fqdn) | Private FQDN of the Container App. It is also sent as the Host header, because the Container Apps ingress routes by host name. | `string` | n/a | yes |
| <a name="input_health_probe_path"></a> [health\_probe\_path](#input\_health\_probe\_path) | Path probed on the backend. A 2xx or 3xx answer marks the backend healthy. | `string` | n/a | yes |
| <a name="input_host_name"></a> [host\_name](#input\_host\_name) | Public host name served by the HTTPS listener, e.g. app.biffkittz.com. Requests for other host names get no response from the app. | `string` | n/a | yes |
| <a name="input_identity_id"></a> [identity\_id](#input\_identity\_id) | User-assigned managed identity used to read the TLS certificate from Key Vault. | `string` | n/a | yes |
| <a name="input_location"></a> [location](#input\_location) | Azure region of the gateway. | `string` | n/a | yes |
| <a name="input_log_analytics_workspace_id"></a> [log\_analytics\_workspace\_id](#input\_log\_analytics\_workspace\_id) | Log Analytics workspace that receives access logs and metrics. | `string` | n/a | yes |
| <a name="input_name"></a> [name](#input\_name) | Name of the application gateway. | `string` | n/a | yes |
| <a name="input_public_ip_name"></a> [public\_ip\_name](#input\_public\_ip\_name) | Name of the gateway's public IP address. | `string` | n/a | yes |
| <a name="input_resource_group_name"></a> [resource\_group\_name](#input\_resource\_group\_name) | Resource group that holds the gateway and its public IP. | `string` | n/a | yes |
| <a name="input_ssl_certificate_key_vault_secret_id"></a> [ssl\_certificate\_key\_vault\_secret\_id](#input\_ssl\_certificate\_key\_vault\_secret\_id) | Versionless Key Vault secret URI of the TLS certificate. | `string` | n/a | yes |
| <a name="input_subnet_id"></a> [subnet\_id](#input\_subnet\_id) | Dedicated subnet for the gateway. | `string` | n/a | yes |
| <a name="input_tags"></a> [tags](#input\_tags) | Tags applied to the gateway and its public IP. | `map(string)` | n/a | yes |
| <a name="input_backend_port"></a> [backend\_port](#input\_backend\_port) | HTTP port on the Container Apps environment's internal load balancer. | `number` | `80` | no |
| <a name="input_ssl_policy_name"></a> [ssl\_policy\_name](#input\_ssl\_policy\_name) | Predefined TLS policy for the listener. AppGwSslPolicy20220101S allows TLS 1.2 and 1.3 with strong ciphers only. | `string` | `"AppGwSslPolicy20220101S"` | no |
| <a name="input_zones"></a> [zones](#input\_zones) | Availability zones for the gateway and its public IP. Leave empty only in regions without zones. | `list(string)` | <pre>[<br/>  "1",<br/>  "2",<br/>  "3"<br/>]</pre> | no |

### Outputs

| Name | Description |
| ---- | ----------- |
| <a name="output_id"></a> [id](#output\_id) | Resource ID of the application gateway. |
| <a name="output_name"></a> [name](#output\_name) | Name of the application gateway. |
| <a name="output_public_ip_address"></a> [public\_ip\_address](#output\_public\_ip\_address) | The gateway's static public IPv4 address (the target of the Cloudflare DNS record). |
<!-- END_TF_DOCS -->
