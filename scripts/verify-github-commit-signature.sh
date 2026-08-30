#!/usr/bin/env bash
set -Eeuo pipefail

repository="${1:?usage: verify-github-commit-signature.sh <owner/repository> <full-git-sha>}"
commit_sha="${2:?usage: verify-github-commit-signature.sh <owner/repository> <full-git-sha>}"

[[ "$repository" == "dwcourtney/JobSearchManager" ]] || {
  echo "Refusing signature validation for non-canonical repository $repository." >&2
  exit 1
}
[[ "$commit_sha" =~ ^[0-9a-f]{40}$ ]] || {
  echo "Signature validation requires a lowercase full Git SHA." >&2
  exit 2
}
[[ -n "${GH_TOKEN:-}" ]] || {
  echo "GH_TOKEN is required for GitHub commit verification." >&2
  exit 2
}
command -v gh >/dev/null || {
  echo "GitHub CLI is unavailable on this trusted hosted runner." >&2
  exit 2
}

verification="$(gh api "repos/$repository/commits/$commit_sha" --jq '.commit.verification | [.verified, .reason] | @tsv')"
IFS=$'\t' read -r verified reason <<< "$verification"
if [[ "$verified" != "true" || "$reason" != "valid" ]]; then
  echo "GitHub did not verify commit $commit_sha (verified=$verified, reason=$reason)." >&2
  exit 1
fi

echo "GitHub verified commit signature for $commit_sha."
