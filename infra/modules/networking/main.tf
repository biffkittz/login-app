# One virtual network with two dedicated subnets. Each subnet hosts exactly one service (the Container Apps
# environment, PostgreSQL), so each subnet's network security group can list precisely the traffic that service
# accepts and deny everything else.

resource "azurerm_virtual_network" "this" {
  name                = "vnet-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  address_space       = var.address_space
  tags                = var.tags
}

# The Container Apps environment (workload profiles) runs its nodes and ingress proxy here. Its public IP and load
# balancer live in the platform-managed resource group.
resource "azurerm_subnet" "container_apps" {
  name                 = "snet-aca"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.subnet_address_prefixes.container_apps]

  delegation {
    name = "container-apps-environment"

    service_delegation {
      name = "Microsoft.App/environments"
      # Azure always sets this action for the delegation; declaring it avoids a perpetual diff.
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }

  # The app and the bootstrap job read their Key Vault secrets from this subnet. With a service endpoint that call
  # travels over the Microsoft backbone and can be allowed by the vault firewall's virtual network rules.
  service_endpoint {
    service = "Microsoft.KeyVault"
  }

  # The observability stack's NFS share (modules/observability) admits this subnet only, through this endpoint: NFS
  # has no keys, so the network decides who may mount it.
  service_endpoint {
    service = "Microsoft.Storage"
  }
}

# PostgreSQL flexible server with private access (VNet integration): the server gets a private IP in this delegated
# subnet and has no public endpoint at all. Flexible server doesn't support service endpoint rules ("VNet rules");
# its only network options are this one and public access plus a private endpoint. Either way the app's traffic stays
# on Microsoft's network, and this option also removes the internet-facing endpoint.
resource "azurerm_subnet" "postgresql" {
  name                 = "snet-psql"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.subnet_address_prefixes.postgresql]

  delegation {
    name = "postgresql-flexible-server"

    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }

  # Azure adds this endpoint when the first server is placed in the subnet (it archives WAL files to Storage) and
  # warns that removing it disrupts the server. Declaring it stops Terraform from removing it on the next apply.
  service_endpoint {
    service = "Microsoft.Storage"
  }
}

locals {
  subnet_ids = {
    container_apps = azurerm_subnet.container_apps.id
    postgresql     = azurerm_subnet.postgresql.id
  }

  prefixes             = var.subnet_address_prefixes
  restrict_app_ingress = length(var.app_ingress_allowed_cidrs) > 0

  # Inbound rules for each subnet, lowest priority number first. Outbound traffic keeps the platform defaults,
  # which every service here needs (Microsoft Entra ID, container registries, Azure Storage, Azure Monitor).
  #
  # Each subnet ends with an explicit deny rule. Without it, the default AllowVnetInBound rule would let any address
  # in the VNet (or a peered VNet) reach the service. The deny rules match the VirtualNetwork or Internet service
  # tags, and the VirtualNetwork tag also covers the Azure host address 168.63.129.16 that load balancer probes come
  # from. That is why every subnet explicitly re-allows AzureLoadBalancer ahead of its deny rule.
  inbound_rules = {
    container_apps = {
      # In an external workload-profiles environment, internet traffic arrives through the public IP in the managed
      # resource group and, per Microsoft, isn't subject to this NSG. The rule documents the intent and takes effect
      # should the platform ever route that traffic through the subnet: the apps' exposed ports plus the node-port
      # range that TCP ingress is mapped to.
      AllowAppIngressInbound = {
        priority    = 100
        access      = "Allow"
        protocol    = "Tcp"
        sources     = local.restrict_app_ingress ? var.app_ingress_allowed_cidrs : ["Internet"]
        destination = local.prefixes.container_apps
        ports       = concat([for port in var.app_ingress_ports : tostring(port)], ["30000-32767"])
        description = local.restrict_app_ingress ? "TLS to the app from the allowed ranges (Cloudflare's edge)." : "TLS to the app from the internet."
      }
      AllowAzureLoadBalancerInbound = {
        priority    = 110
        access      = "Allow"
        protocol    = "*"
        sources     = ["AzureLoadBalancer"]
        destination = "*"
        ports       = ["*"]
        description = "Health probes of the environment's load balancer."
      }
      AllowIntraSubnetInbound = {
        priority    = 120
        access      = "Allow"
        protocol    = "*"
        sources     = [local.prefixes.container_apps]
        destination = local.prefixes.container_apps
        ports       = ["*"]
        description = "Traffic between the environment's own nodes and proxies."
      }
      DenyVirtualNetworkInbound = {
        priority    = 4000
        access      = "Deny"
        protocol    = "*"
        sources     = ["VirtualNetwork"]
        destination = "*"
        ports       = ["*"]
        description = "Anything else from inside the network or peered networks."
      }
    }

    postgresql = {
      AllowContainerAppsPostgresInbound = {
        priority    = 100
        access      = "Allow"
        protocol    = "Tcp"
        sources     = [local.prefixes.container_apps]
        destination = local.prefixes.postgresql
        ports       = ["5432"]
        description = "The app and the bootstrap job, which both run in the Container Apps subnet."
      }
      AllowAzureLoadBalancerInbound = {
        priority    = 110
        access      = "Allow"
        protocol    = "*"
        sources     = ["AzureLoadBalancer"]
        destination = "*"
        ports       = ["*"]
        description = "Azure platform health probes."
      }
      AllowIntraSubnetInbound = {
        priority    = 120
        access      = "Allow"
        protocol    = "*"
        sources     = [local.prefixes.postgresql]
        destination = local.prefixes.postgresql
        ports       = ["*"]
        description = "Required by the service (port 5432 within the subnet, e.g. for high availability)."
      }
      DenyVirtualNetworkInbound = {
        priority    = 4000
        access      = "Deny"
        protocol    = "*"
        sources     = ["VirtualNetwork"]
        destination = "*"
        ports       = ["*"]
        description = "Anything else from inside the network or peered networks."
      }
    }
  }

  # Flattened to one map entry per rule, keyed "<subnet>/<rule name>".
  security_rules = merge([
    for subnet, rules in local.inbound_rules : {
      for rule_name, rule in rules : "${subnet}/${rule_name}" => merge(rule, { subnet = subnet, name = rule_name })
    }
  ]...)
}

resource "azurerm_network_security_group" "this" {
  for_each = local.subnet_ids

  name                = "nsg-${var.name}-${replace(each.key, "_", "-")}"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_network_security_rule" "inbound" {
  for_each = local.security_rules

  name                        = each.value.name
  description                 = each.value.description
  resource_group_name         = var.resource_group_name
  network_security_group_name = azurerm_network_security_group.this[each.value.subnet].name
  direction                   = "Inbound"
  priority                    = each.value.priority
  access                      = each.value.access
  protocol                    = each.value.protocol
  source_port_range           = "*"
  # The API takes service tags and "*" only in the singular fields, so single values go there.
  source_address_prefix      = length(each.value.sources) == 1 ? each.value.sources[0] : null
  source_address_prefixes    = length(each.value.sources) > 1 ? each.value.sources : null
  destination_address_prefix = each.value.destination
  destination_port_range     = length(each.value.ports) == 1 ? each.value.ports[0] : null
  destination_port_ranges    = length(each.value.ports) > 1 ? each.value.ports : null
}

resource "azurerm_subnet_network_security_group_association" "this" {
  for_each = local.subnet_ids

  subnet_id                 = each.value
  network_security_group_id = azurerm_network_security_group.this[each.key].id

  # Associate only once the rules exist: nothing should run behind a half-configured NSG.
  depends_on = [azurerm_network_security_rule.inbound]
}
