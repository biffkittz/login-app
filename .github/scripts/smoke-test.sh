#!/usr/bin/env bash
# After an apply, checks every instance of the app from the outside, through Cloudflare, the way users reach it:
# /health/ready must answer "Healthy" (the app is up and reaches its database) and / must serve the SPA. Retries for
# up to ten minutes, because a new revision takes a minute or two to start and migrate. Run from the repository root
# after an apply, with Terraform initialized in infra/.
set -euo pipefail

cd "${GITHUB_WORKSPACE:?}/infra"

# Only the public URLs are printed: the other outputs include the origin's IP address.
mapfile -t urls < <(terraform output -json app_instances | python3 -c '
import json, sys
for instance in json.load(sys.stdin).values():
    print(instance["url"])')
if ((${#urls[@]} == 0)); then
  echo "Nothing to check: the Container Apps don't exist yet (app_image_tag isn't set)."
  exit 0
fi

user_agent="login-app-ci-smoke-test (github.com/${GITHUB_REPOSITORY:-biffkittz/login-app})"
deadline=$((SECONDS + 600))
failed=0
for url in "${urls[@]}"; do
  until body="$(curl -fsS --max-time 10 --user-agent "$user_agent" "$url/health/ready" 2> /dev/null)" && [[ "$body" == "Healthy" ]]; do
    if ((SECONDS >= deadline)); then
      echo "::error::$url/health/ready didn't answer Healthy within 10 minutes."
      failed=1
      continue 2
    fi
    sleep 15
  done
  status="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 --user-agent "$user_agent" "$url/")"
  if [[ "$status" != "200" ]]; then
    echo "::error::$url/ answered $status instead of 200."
    failed=1
    continue
  fi
  echo "$url is healthy and serves the app."
  echo "- $url: healthy" >> "${GITHUB_STEP_SUMMARY:?}"
done
exit "$failed"
