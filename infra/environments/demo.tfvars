# Settings for the demo environment. Committed to the repository, so it must never contain secrets.
#
#   terraform plan  -var-file=environments/demo.tfvars -out=demo.tfplan
#   terraform apply demo.tfplan
#
# Supply the rest through the environment, never through this file:
#   export CLOUDFLARE_API_TOKEN=...                                   # Cloudflare provider
#   export TF_VAR_tls_certificate_pfx_path=~/.secrets/biffkittz-origin.pfx
#   export TF_VAR_tls_certificate_pfx_password=...                    # if the PFX has one
# Every other variable keeps its default from variables.tf.

subscription_id = "3c3f379f-8161-4631-a30c-a3af82622a6a"
location        = "centralus"
project         = "loginapp"
environment     = "demo"

dns_zone_name = "biffkittz.com"
app_hostname  = "app.biffkittz.com"

# Image tag of the app to run, e.g. the short commit SHA pushed with `az acr build`. Leave it commented out for the
# first apply (no image exists yet). Once set, keep it set: removing it again would delete the Container App.
# app_image_tag = "1a2b3c4"

# One replica: this is a demonstration environment.
app_min_replicas = 1
app_max_replicas = 1

# Smallest PostgreSQL size available.
postgresql_sku_name = "B_Standard_B1ms"
