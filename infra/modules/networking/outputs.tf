output "virtual_network_id" {
  description = "Resource ID of the virtual network."
  value       = azurerm_virtual_network.this.id
}

output "virtual_network_name" {
  description = "Name of the virtual network."
  value       = azurerm_virtual_network.this.name
}

output "subnet_ids" {
  description = "Subnet resource IDs, keyed container_apps and postgresql. Available once each subnet's NSG is attached."
  value       = local.subnet_ids

  # Consumers (the environment, the database) must not start before their subnet is protected.
  depends_on = [azurerm_subnet_network_security_group_association.this]
}

output "subnet_address_prefixes" {
  description = "Subnet address prefixes, keyed like subnet_ids."
  value       = var.subnet_address_prefixes
}

output "network_security_group_ids" {
  description = "Network security group IDs, keyed like subnet_ids."
  value       = { for key, nsg in azurerm_network_security_group.this : key => nsg.id }
}
