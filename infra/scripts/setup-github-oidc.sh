#!/usr/bin/env bash
# One-time Azure setup for GitHub Actions CI/CD (docs/CI_CD.md): three user-assigned managed identities that the
# workflows sign in as with GitHub's OpenID Connect tokens, so no client secret or storage key exists anywhere.
#
#   id-loginapp-github-plan    speculative plans on pull requests ("infra-pr" environment): can read, can't change
#   id-loginapp-github-deploy  the plan and apply on main ("demo-plan" and "demo"): what running Terraform needs
#   id-loginapp-github-push    pushing app images ("registry"): AcrPush on the registry, nothing else
#
# Each identity trusts tokens for one repository and specific GitHub environments only, and the environments decide
# which branches and reviewers may use them. The identities live in the state's resource group, outside the
# Terraform-managed one, so `terraform destroy` can't remove the pipeline that runs it.
#
# Run it once from infra/, signed in with `az login` as an Owner of the subscription, after the platform exists (it
# reads the registry's name from the Terraform outputs). Running it again changes nothing that is already in place.
#
#   ./scripts/setup-github-oidc.sh
#
# Overrides: GITHUB_REPOSITORY (owner/name), AZURE_SUBSCRIPTION_ID, LOCATION, IDENTITY_RESOURCE_GROUP, ACR_NAME.
set -euo pipefail

cd "$(dirname "$0")/.."

# The Windows Azure CLI, reached through WSL interop, ends every line with a carriage return, which corrupts the IDs
# and scopes below. Prefer the Linux CLI (/usr/bin/az; docs/DEVELOPMENT.md), but strip the returns so either works.
if [[ "$(command -v az)" == /mnt/* ]]; then
  echo "Note: this is the Windows Azure CLI ($(command -v az)); the Linux one (/usr/bin/az) is faster and safer." >&2
fi
az() { command az "$@" | tr -d '\r'; }

tfvars="environments/demo.tfvars"
repository="${GITHUB_REPOSITORY:-biffkittz/login-app}"
subscription_id="${AZURE_SUBSCRIPTION_ID:-$(sed -nE 's/^subscription_id[[:space:]]*=[[:space:]]*"([^"]+)".*/\1/p' "$tfvars")}"
location="${LOCATION:-$(sed -nE 's/^location[[:space:]]*=[[:space:]]*"([^"]+)".*/\1/p' "$tfvars")}"
identity_group="${IDENTITY_RESOURCE_GROUP:-rg-terraform-state}"

# The remote state (backend.tf).
state_group="rg-terraform-state"
state_account="demonstration"
state_container="terraform"

if [[ ! "$repository" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
  echo "GITHUB_REPOSITORY must be owner/name, not '$repository'." >&2
  exit 1
fi

az account set --subscription "$subscription_id"
tenant_id="$(az account show --query tenantId -o tsv)"
registry="${ACR_NAME:-$(terraform output -raw container_registry_name)}"

subscription_scope="/subscriptions/$subscription_id"
state_scope="$(az storage account show --name "$state_account" --resource-group "$state_group" --query id -o tsv)/blobServices/default/containers/$state_container"
registry_scope="$(az acr show --name "$registry" --query id -o tsv)"

echo "Repository:   $repository"
echo "Subscription: $subscription_id (tenant $tenant_id)"
echo "Identities:   $identity_group ($location)"
echo "State:        $state_scope"
echo "Registry:     $registry"
echo

# Creates the identity unless it exists; prints its principal (object) ID.
ensure_identity() {
  local name="$1"
  if ! az identity show --resource-group "$identity_group" --name "$name" --output none 2> /dev/null; then
    az identity create --resource-group "$identity_group" --name "$name" --location "$location" \
      --tags project=loginapp managed_by=setup-github-oidc.sh --output none
    echo "+ identity $name" >&2
  fi
  az identity show --resource-group "$identity_group" --name "$name" --query principalId -o tsv
}

# Trusts GitHub's tokens for one environment of this repository (create-or-update, so it is safe to repeat).
ensure_federation() {
  local identity="$1" environment="$2"
  az identity federated-credential create --resource-group "$identity_group" --identity-name "$identity" \
    --name "github-$environment" \
    --issuer "https://token.actions.githubusercontent.com" \
    --subject "repo:$repository:environment:$environment" \
    --audiences "api://AzureADTokenExchange" \
    --output none
  echo "  trusts repo:$repository:environment:$environment"
}

# One attempt at a role assignment: skips it if the same one exists, and prints what it did. An optional condition
# (ABAC) constrains what the role allows.
assign_role() {
  local principal="$1" role="$2" scope="$3" condition="${4:-}"
  local existing
  existing="$(az role assignment list --assignee "$principal" --role "$role" --scope "$scope" --query '[0].id' -o tsv)" || return 1
  if [[ -n "$existing" ]]; then
    echo "  = $role on ${scope##*/}"
    return 0
  fi
  local args=(--assignee-object-id "$principal" --assignee-principal-type ServicePrincipal --role "$role" --scope "$scope")
  if [[ -n "$condition" ]]; then
    args+=(--condition "$condition" --condition-version "2.0")
  fi
  az role assignment create "${args[@]}" --output none || return 1
  echo "  + $role on ${scope##*/}"
}

# A new managed identity takes a minute or two to replicate through Microsoft Entra ID, and until then role
# assignments to it fail with "make sure the corresponding service principal is created". Retries only that error.
ensure_role() {
  local attempt output
  for attempt in $(seq 1 24); do
    if output="$(assign_role "$@" 2>&1)"; then
      echo "$output"
      return 0
    fi
    if [[ "$output" != *"service principal"* && "$output" != *"PrincipalNotFound"* && "$output" != *"does not exist in the directory"* ]]; then
      break
    fi
    echo "  waiting for the new identity to appear in Entra ID ($attempt/24)..." >&2
    sleep 15
  done
  echo "$output" >&2
  return 1
}

role_id() {
  az role definition list --name "$1" --query '[0].name' -o tsv
}

# Role Based Access Control Administrator may only grant or remove the four roles Terraform assigns, so the deploy
# identity can't make itself, or anyone else, an Owner.
assignable="$(role_id "AcrPull"), $(role_id "Key Vault Secrets User"), $(role_id "Key Vault Secrets Officer"), $(role_id "Key Vault Certificates Officer")"
constrained="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$assignable})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$assignable}))"

echo "id-loginapp-github-plan"
plan_principal="$(ensure_identity id-loginapp-github-plan)"
ensure_federation id-loginapp-github-plan infra-pr
# Pull request plans don't refresh, so reading the configuration of the subscription and the state is all they need.
ensure_role "$plan_principal" "Reader" "$subscription_scope"
ensure_role "$plan_principal" "Storage Blob Data Reader" "$state_scope"

echo "id-loginapp-github-deploy"
deploy_principal="$(ensure_identity id-loginapp-github-deploy)"
ensure_federation id-loginapp-github-deploy demo-plan
ensure_federation id-loginapp-github-deploy demo
ensure_role "$deploy_principal" "Contributor" "$subscription_scope"
ensure_role "$deploy_principal" "Role Based Access Control Administrator" "$subscription_scope" "$constrained"
ensure_role "$deploy_principal" "Storage Blob Data Contributor" "$state_scope"

echo "id-loginapp-github-push"
push_principal="$(ensure_identity id-loginapp-github-push)"
ensure_federation id-loginapp-github-push registry
ensure_role "$push_principal" "AcrPush" "$registry_scope"
# Read access to the registry resource itself lets `az acr login --name` find it; it reveals no credentials.
ensure_role "$push_principal" "Reader" "$registry_scope"

client_id() {
  az identity show --resource-group "$identity_group" --name "$1" --query clientId -o tsv
}

your_object_id="$(az ad signed-in-user show --query id -o tsv 2> /dev/null || echo "<your object ID>")"

cat << EOF

Done. Configure GitHub (Settings; details in docs/CI_CD.md):

  Repository variables
    AZURE_TENANT_ID       = $tenant_id
    AZURE_SUBSCRIPTION_ID = $subscription_id
    ACR_NAME              = $registry

  Environment variable AZURE_CLIENT_ID
    infra-pr   = $(client_id id-loginapp-github-plan)
    demo-plan  = $(client_id id-loginapp-github-deploy)
    demo       = $(client_id id-loginapp-github-deploy)
    registry   = $(client_id id-loginapp-github-push)

Then let the deploy identity manage Key Vault's contents next to you: add these two lines to $tfvars and apply once
from your machine (CI's first plan reads the vault's secrets, which it can't before this apply):

  key_vault_administrator_object_id   = "$your_object_id"
  key_vault_additional_administrators = { github_deploy = "$deploy_principal" }

Role assignments can take a few minutes to take effect.
EOF
