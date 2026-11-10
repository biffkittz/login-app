#!/usr/bin/env bash
# Applies the plan the infra workflow's plan job saved and encrypted (terraform-plan.sh, PLAN_MODE=deploy): exactly
# what the reviewer approved, never a fresh plan. Run from the repository root after .github/actions/terraform-init,
# with the encrypted plan downloaded to $RUNNER_TEMP/terraform/tfplan.gpg.
#
# Terraform refuses a saved plan whose state has changed since it was made ("Saved plan is stale"), so an apply can't
# act on an outdated picture of the infrastructure; plan again instead.
set -euo pipefail

out="${RUNNER_TEMP:?}/terraform"
umask 077
cd "${GITHUB_WORKSPACE:?}/infra"

for name in TF_PLAN_ENCRYPTION_KEY TLS_CERTIFICATE_PFX_BASE64; do
  if [[ -z "${!name:-}" ]]; then
    echo "::error::The $name secret isn't set in this environment (docs/CI_CD.md)."
    exit 1
  fi
done

# Terraform reads the certificate file again while applying; it must be the file the plan read, at the same path.
printf '%s' "$TLS_CERTIFICATE_PFX_BASE64" | base64 --decode > "$RUNNER_TEMP/origin.pfx"
unset TLS_CERTIFICATE_PFX_BASE64

gpg --batch --yes --quiet --pinentry-mode loopback --passphrase-fd 3 \
  --decrypt --output "$out/tfplan" "$out/tfplan.gpg" 3<<< "$TF_PLAN_ENCRYPTION_KEY"
unset TF_PLAN_ENCRYPTION_KEY

# The log is public. Progress lines name resources and their IDs, which is fine; the outputs at the end include the
# origin's IP address and the registration email recipients, so the log stops before them. (awk keeps reading to
# the end, so Terraform never writes into a closed pipe.)
set +e
terraform apply -input=false -no-color "$out/tfplan" 2>&1 | awk '/^Outputs:$/ { hide = 1 } !hide { print; fflush() }'
status=${PIPESTATUS[0]}
set -e
rm -f "$out/tfplan" "$RUNNER_TEMP/origin.pfx"
exit "$status"
