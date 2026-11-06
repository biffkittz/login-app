# TFLint configuration (https://github.com/terraform-linters/tflint). Run from infra/:
#   tflint --init          # once, downloads the azurerm ruleset pinned below
#   tflint --recursive     # the root module and every module under modules/

config {
  # Also inspect the local modules as they are called from the root module, with real argument values.
  call_module_type = "local"
}

# Terraform language rules. "all" adds naming, documentation and module-structure checks to the recommended set.
plugin "terraform" {
  enabled = true
  preset  = "all"
}

# Azure-specific checks, such as invalid SKUs, VM sizes and regions.
plugin "azurerm" {
  enabled = true
  version = "0.33.0"
  source  = "github.com/terraform-linters/tflint-ruleset-azurerm"
}
