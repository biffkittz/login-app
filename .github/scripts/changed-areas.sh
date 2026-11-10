#!/usr/bin/env bash
# Tells a workflow whether a pull request touches its part of the repository, as a step output ("<name>=true" or
# "false"). Workflows therefore run on every pull request, so the checks a branch ruleset requires always report,
# while the jobs that would have nothing to do are skipped (a skipped job counts as passed).
#
#   changed-areas.sh <output name> <extended regular expression matched against changed paths>
#
# Every other event (push, workflow_dispatch, schedule) is filtered by the workflow's own `paths` or runs on
# purpose, so the answer is always "true". Needs a checkout with fetch-depth 2: a pull request's merge commit has the
# base branch as its first parent, so diffing against HEAD^1 gives exactly the pull request's changes.
set -euo pipefail

name="${1:?usage: changed-areas.sh <output name> <path pattern>}"
pattern="${2:?usage: changed-areas.sh <output name> <path pattern>}"

if [[ "${GITHUB_EVENT_NAME:?}" != "pull_request" ]]; then
  result=true
else
  # Captured first rather than piped: with pipefail, grep -q closing the pipe early would fail the pipeline.
  files="$(git diff --name-only HEAD^1 HEAD)"
  if grep -Eq "$pattern" <<< "$files"; then result=true; else result=false; fi
fi

echo "$name=$result" >> "${GITHUB_OUTPUT:?}"
echo "Changes that concern $name: $result"
