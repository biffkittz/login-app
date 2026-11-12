terraform {
  # Terraform 1.17 is the current release line. The upper bound keeps a future major version, which may contain
  # breaking changes, from being used by accident.
  required_version = ">= 1.17.1, < 2.0.0"

  required_providers {
    # Every Azure resource. azurerm covers all of them, so the azapi provider isn't needed.
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.12"
    }
    # Approved addition: the proxied app.biffkittz.com DNS record and Cloudflare's published IP ranges.
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 5.28"
    }
    # Approved addition: generated database passwords and the JWT signing key.
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
    # Approved addition: short waits that let new Azure role assignments propagate before they are used. Only the
    # modules use it; it is declared here so that the root module pins every provider version in one place.
    # tflint-ignore: terraform_unused_required_providers
    time = {
      source  = "hashicorp/time"
      version = "~> 0.14"
    }
  }
}

provider "azurerm" {
  subscription_id = var.subscription_id

  # azurerm 5 registers no resource providers unless asked. Register exactly the ones this configuration uses (a
  # no-op when they're already registered).
  resource_provider_registrations = "none"
  resource_providers_to_register = [
    "Microsoft.App",
    "Microsoft.ContainerRegistry",
    "Microsoft.DBforPostgreSQL",
    "Microsoft.Insights",
    "Microsoft.KeyVault",
    "Microsoft.ManagedIdentity",
    "Microsoft.Network",
    "Microsoft.OperationalInsights",
    "Microsoft.Storage", # the observability stack's NFS share
  ]

  features {
    key_vault {
      # Destroy only soft-deletes; nothing is purged. Re-creating the same names within the retention period
      # recovers the vault and its secrets instead of failing on a name conflict. Purging is impossible anyway
      # while purge protection is on (the default here).
      # NOTE: For the demo app, set purging to true so the key vault resource is entirely destroyed.
      purge_soft_delete_on_destroy               = true # initially false
      purge_soft_deleted_secrets_on_destroy      = true # initially false
      purge_soft_deleted_certificates_on_destroy = true # initially false
      recover_soft_deleted_key_vaults            = true
      recover_soft_deleted_secrets               = true
      recover_soft_deleted_certificates          = true
    }

    resource_group {
      # Refuse to delete a resource group that still holds resources Terraform doesn't know about.
      prevent_deletion_if_contains_resources = true
    }
  }
}

# Authenticates with the API token in the CLOUDFLARE_API_TOKEN environment variable (see docs/AUTHENTICATION.md).
# The token is deliberately not a Terraform variable, so it can't end up in a tfvars file or in the state.
provider "cloudflare" {}
