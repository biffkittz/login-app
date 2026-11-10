#!/usr/bin/env bash
# Proposes deploying a freshly pushed app image: opens a pull request that sets app_image_tag in
# infra/environments/demo.tfvars to the new tag. The deployed version is therefore always what main says, every
# release goes through the same plan-and-approve path as any other infrastructure change, and rolling back is
# reverting the pull request. Called by .github/workflows/app.yml; idempotent, so a re-run reuses the branch and pull
# request it already opened.
#
#   IMAGE_TAG=<tag> IMAGE_DIGEST=<sha256:...> GH_TOKEN=<token> propose-deploy.sh
#
# The branch and the commit are made through GitHub's API, so GitHub signs the commit and no token is used for a git
# push from the runner. Pull requests opened with the workflow's GITHUB_TOKEN don't start pull_request workflows
# (GitHub's rule against recursive runs), so the checks are started with workflow_dispatch, which that token may do.
set -euo pipefail

tag="${IMAGE_TAG:?}"
digest="${IMAGE_DIGEST:-unknown}"
repo="${GITHUB_REPOSITORY:?}"
tfvars="infra/environments/demo.tfvars"
branch="deploy/login-app-$tag"
pattern='^app_image_tag[[:space:]]*='

[[ "$tag" =~ ^[0-9a-f]{12}$ ]] || { echo "::error::Unexpected image tag '$tag'."; exit 1; }

count="$(grep -cE "$pattern" "$tfvars" || true)"
if [[ "$count" != "1" ]]; then
  echo "::error::$tfvars must set app_image_tag on exactly one line (found $count)."
  exit 1
fi
current="$(sed -nE 's/^app_image_tag[[:space:]]*=[[:space:]]*"([^"]*)".*/\1/p' "$tfvars")"
if [[ "$current" == "$tag" ]]; then
  echo "main already deploys login-app:$tag; nothing to propose."
  exit 0
fi

if ! gh api "repos/$repo/git/ref/heads/$branch" --silent 2> /dev/null; then
  gh api --method POST "repos/$repo/git/refs" -f ref="refs/heads/$branch" -f sha="${GITHUB_SHA:?}" --silent
  echo "Created branch $branch."
fi

branch_file="$(gh api "repos/$repo/contents/$tfvars?ref=$branch")"
blob_sha="$(jq -r .sha <<< "$branch_file")"
if jq -r .content <<< "$branch_file" | base64 --decode | grep -qE "^app_image_tag[[:space:]]*=[[:space:]]*\"$tag\""; then
  echo "$branch already sets app_image_tag to $tag."
else
  updated="$(sed -E "s/^(app_image_tag[[:space:]]*=[[:space:]]*\")[^\"]*(\".*)$/\1$tag\2/" "$tfvars")"
  gh api --method PUT "repos/$repo/contents/$tfvars" --silent \
    -f branch="$branch" \
    -f sha="$blob_sha" \
    -f message="Deploy login-app:$tag" \
    -f content="$(printf '%s\n' "$updated" | base64 --wrap=0)"
  echo "Committed app_image_tag = \"$tag\" to $branch."
fi

pr="$(gh pr list --repo "$repo" --head "$branch" --state open --json number --jq '.[0].number // empty')"
if [[ -z "$pr" ]]; then
  run_url="${GITHUB_SERVER_URL:?}/$repo/actions/runs/${GITHUB_RUN_ID:?}"
  body="$(
    cat << EOF
Deploys \`login-app:$tag\` (built from $GITHUB_SHA, digest \`$digest\`) to every instance of the app, by setting \`app_image_tag\` in \`$tfvars\` (was \`$current\`).

- The image was built, scanned and pushed by [this run]($run_url).
- The **infra** checks and a speculative plan run on this pull request; the plan is commented below.
- **Merging deploys it:** the infra workflow plans again on main and waits for approval in the \`demo\` environment before it applies.
- To roll back later, revert this pull request.

Opened by the app workflow ([docs/CI_CD.md](../blob/main/docs/CI_CD.md)).
EOF
  )"
  pr_url="$(gh pr create --repo "$repo" --base main --head "$branch" --title "Deploy login-app:$tag" --body "$body")"
  echo "Opened $pr_url."
  echo "- Deployment proposed: $pr_url" >> "${GITHUB_STEP_SUMMARY:?}"
else
  echo "Pull request #$pr already proposes this deployment."
fi

# Report the checks that branch rules require, on the branch's latest commit.
gh workflow run infra.yml --repo "$repo" --ref "$branch"
gh workflow run app.yml --repo "$repo" --ref "$branch"
echo "Started the infra and app checks on $branch."
