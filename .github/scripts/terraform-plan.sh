#!/usr/bin/env bash
# Runs `terraform plan` for the infra workflow (.github/workflows/infra.yml) and publishes a summary that is safe for
# a public repository. Run from the repository root after .github/actions/terraform-init.
#
#   PLAN_MODE=pr      Speculative plan for a pull request: no refresh and no state lock, because the pull request
#                     identity can only read. Nothing is saved.
#   PLAN_MODE=deploy  The plan the apply job will run: refreshed and locked, then saved and encrypted with
#                     TF_PLAN_ENCRYPTION_KEY, because a plan file holds every secret in clear text.
#   PLAN_MODE=drift   Scheduled check: refreshed; any change means the infrastructure drifted from main.
#
# The repository is public, and so are its logs. Terraform's own plan output shows attribute values, so it never
# reaches the log; only terraform-plan-summary.py's summary does. Secrets arrive as environment variables from the
# job's GitHub environment and are passed on to Terraform as TF_VAR_* for this step only.
#
# Step outputs: has_changes (true/false) and summary (path of the Markdown summary).
set -euo pipefail

mode="${PLAN_MODE:?PLAN_MODE must be pr, deploy or drift}"
case "$mode" in
  pr | deploy | drift) ;;
  *) echo "::error::Unknown PLAN_MODE '$mode'." && exit 1 ;;
esac

out="${RUNNER_TEMP:?}/terraform"
umask 077
mkdir -p "$out"
cd "${GITHUB_WORKSPACE:?}/infra"

# The origin certificate. Terraform reads the file while planning and again while applying, so the apply job writes
# the same secret to the same path.
if [[ -z "${TLS_CERTIFICATE_PFX_BASE64:-}" ]]; then
  echo "::error::The TLS_CERTIFICATE_PFX_BASE64 secret isn't set in this environment (docs/CI_CD.md)."
  exit 1
fi
printf '%s' "$TLS_CERTIFICATE_PFX_BASE64" | base64 --decode > "$RUNNER_TEMP/origin.pfx"
export TF_VAR_tls_certificate_pfx_path="$RUNNER_TEMP/origin.pfx"
if [[ -n "${TLS_CERTIFICATE_PFX_PASSWORD:-}" ]]; then
  export TF_VAR_tls_certificate_pfx_password="$TLS_CERTIFICATE_PFX_PASSWORD"
fi

# Without the key, Terraform plans to delete it from Key Vault, and the app loses AI pictures and cost estimates.
# That shows in the plan; this makes sure nobody misses why.
if [[ -n "${GEMINI_API_KEY:-}" ]]; then
  export TF_VAR_gemini_api_key="$GEMINI_API_KEY"
else
  echo "::warning::GEMINI_API_KEY isn't set in this environment, so the plan removes the Gemini key (no AI pictures or cost estimates)."
fi

# Grafana's GitHub OAuth App (observability_enabled). Terraform's validation names it if it is needed and missing.
if [[ -n "${GRAFANA_GITHUB_CLIENT_SECRET:-}" ]]; then
  export TF_VAR_grafana_github_client_secret="$GRAFANA_GITHUB_CLIENT_SECRET"
fi
unset TLS_CERTIFICATE_PFX_BASE64 TLS_CERTIFICATE_PFX_PASSWORD GEMINI_API_KEY GRAFANA_GITHUB_CLIENT_SECRET

args=(-input=false -no-color -detailed-exitcode -var-file=environments/demo.tfvars -out="$out/tfplan")
if [[ "$mode" == "pr" ]]; then
  args+=(-refresh=false -lock=false)
fi

set +e
terraform plan "${args[@]}" > "$out/plan.stdout" 2> "$out/plan.stderr"
status=$?
set -e

# Errors and warnings go to stderr. Terraform redacts sensitive values in them, unlike the plan on stdout.
if [[ -s "$out/plan.stderr" ]]; then
  echo "::group::terraform plan messages"
  cat "$out/plan.stderr"
  echo "::endgroup::"
fi
rm -f "$out/plan.stdout"
if ((status == 1)); then
  echo "::error::terraform plan failed; its messages are above."
  exit 1
fi

has_changes=false
((status == 2)) && has_changes=true

# The JSON form holds every value in clear text: it's read once and removed.
terraform show -json "$out/tfplan" > "$out/plan.json"
python3 "$GITHUB_WORKSPACE/.github/scripts/terraform-plan-summary.py" --mode "$mode" "$out/plan.json" > "$out/summary.md"
rm -f "$out/plan.json"
cat "$out/summary.md" >> "${GITHUB_STEP_SUMMARY:?}"

if [[ "$mode" == "deploy" && "$has_changes" == "true" ]]; then
  if [[ -z "${TF_PLAN_ENCRYPTION_KEY:-}" ]]; then
    echo "::error::The TF_PLAN_ENCRYPTION_KEY secret isn't set in this environment (docs/CI_CD.md)."
    exit 1
  fi
  # AES-256 with an integrity check (OpenPGP's modification detection), so a damaged or swapped file fails to decrypt
  # rather than being applied. The key goes through a file descriptor, never the command line.
  gpg --batch --yes --quiet --pinentry-mode loopback --passphrase-fd 3 \
    --symmetric --cipher-algo AES256 --output "$out/tfplan.gpg" "$out/tfplan" 3<<< "$TF_PLAN_ENCRYPTION_KEY"
fi
rm -f "$out/tfplan"

{
  echo "has_changes=$has_changes"
  echo "summary=$out/summary.md"
} >> "${GITHUB_OUTPUT:?}"

if [[ "$mode" == "drift" && "$has_changes" == "true" ]]; then
  echo "::error::The infrastructure has drifted from main; the job summary lists what differs."
  exit 1
fi
