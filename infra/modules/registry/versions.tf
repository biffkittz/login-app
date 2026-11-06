terraform {
  required_version = ">= 1.17.1"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 5.12"
    }
    time = {
      source  = "hashicorp/time"
      version = ">= 0.14"
    }
  }
}
