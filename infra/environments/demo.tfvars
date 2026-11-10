# Settings for the demo environment. Committed to the repository, so it must never contain secrets.
#
#   terraform plan  -var-file=environments/demo.tfvars -out=demo.tfplan
#   terraform apply demo.tfplan
#
# Supply the rest through the environment, never through this file:
#   export CLOUDFLARE_API_TOKEN=...                                   # Cloudflare provider
#   export TF_VAR_tls_certificate_pfx_path=~/.secrets/biffkittz-origin.pfx
#   export TF_VAR_tls_certificate_pfx_password=...                    # if the PFX has one
#   export TF_VAR_gemini_api_key=...                                  # optional: AI pictures of items
# Every other variable keeps its default from variables.tf.

subscription_id = "3c3f379f-8161-4631-a30c-a3af82622a6a"
location        = "centralus"
name_suffix     = "7fyguj" #TODO: REMOVE
project         = "loginapp"
environment     = "demo"

dns_zone_name = "biffkittz.com"
app_hostname  = "app.biffkittz.com"

# Image tag of the app to run, e.g. the short commit SHA pushed with `az acr build`. Leave it commented out for the
# first apply (no image exists yet). Once set, keep it set: removing it again would delete the Container App.
app_image_tag = "v8"

# One replica: this is a demonstration environment.
app_min_replicas = 1
app_max_replicas = 1

# A second, independent instance of the app at grocery.biffkittz.com: the same image, Key Vault secrets and settings,
# its own Container App, database (on the same server) and list. 8444 is its port on the environment's IP, next to the
# main instance's 8443; Cloudflare's Origin Rule sends grocery.biffkittz.com there. After the first apply that adds it,
# run the database bootstrap job (docs/DEPLOYMENT.md, "Add an instance").
additional_app_instances = {
  grocery = {
    hostname      = "grocery.biffkittz.com"
    exposed_port  = 8444
    database_name = "grocery"
  }
}

# Whenever someone registers on either instance, this address gets an email from notifications@biffkittz.com
# ("[grocery app] new registration detected"), sent through Cloudflare Email Service. Cloudflare emails it a
# confirmation link after the first apply that adds it: follow it once (docs/DEPLOYMENT.md, "Registration emails").
registration_notification_recipients = ["benjamin.e.burner@gmail.com"]

# Smallest PostgreSQL size available.
postgresql_sku_name = "B_Standard_B1ms"

# CI/CD (docs/CI_CD.md): you and GitHub Actions both run Terraform, and both need to read the vault's contents back.
# Fill these in from what infra/scripts/setup-github-oidc.sh prints, uncomment them and apply once from your machine
# before CI's first plan. Object IDs aren't secrets. Without them, each plan moves the vault's administrator role to
# whoever runs it.
key_vault_administrator_object_id   = "e01611a8-1e53-4313-9ca1-8c1dd450ff8f"
key_vault_additional_administrators = { github_deploy = "a81d64db-6f59-44c4-be91-a7c50764d9db" }
