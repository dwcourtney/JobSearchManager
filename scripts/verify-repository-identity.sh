#!/usr/bin/env bash
set -Eeuo pipefail

repository_root="${1:-$(git rev-parse --show-toplevel)}"
expected_repository="dwc5703/JobSearchManager"
expected_origin="https://github.com/${expected_repository}"

[[ -d "$repository_root" ]] || {
  echo "Repository root does not exist: $repository_root" >&2
  exit 2
}

requested_root="$(cd -- "$repository_root" && pwd -P)"
resolved_root="$(git -C "$repository_root" rev-parse --show-toplevel)"
actual_root="$(cd -- "$resolved_root" && pwd -P)"
[[ "$actual_root" == "$requested_root" ]] || {
  echo "Expected repository root $requested_root, but Git resolved $actual_root." >&2
  exit 1
}
repository_root="$actual_root"

mapfile -t fetch_urls < <(git -C "$repository_root" remote get-url --all origin)
mapfile -t push_urls < <(git -C "$repository_root" remote get-url --push --all origin)

is_canonical_origin() {
  [[ "$1" == "$expected_origin" || "$1" == "${expected_origin}.git" ]]
}

if (( ${#fetch_urls[@]} != 1 )) || ! is_canonical_origin "${fetch_urls[0]:-}"; then
  echo "origin must have exactly one canonical fetch URL for $expected_repository." >&2
  exit 1
fi
if (( ${#push_urls[@]} != 1 )) || ! is_canonical_origin "${push_urls[0]:-}"; then
  echo "origin must have exactly one canonical push URL for $expected_repository." >&2
  exit 1
fi

if [[ -n "${GITHUB_REPOSITORY:-}" && "$GITHUB_REPOSITORY" != "$expected_repository" ]]; then
  echo "GitHub event repository $GITHUB_REPOSITORY is not $expected_repository." >&2
  exit 1
fi

echo "Verified canonical repository identity: $expected_repository."
