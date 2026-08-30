#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
guard="$script_dir/verify-repository-identity.sh"
temporary_root="$(mktemp -d)"

cleanup() {
  rm -rf -- "$temporary_root"
}
trap cleanup EXIT

fixture="$temporary_root/repository"
git init --quiet "$fixture"
git -C "$fixture" remote add origin https://github.com/dwcourtney/JobSearchManager

GITHUB_REPOSITORY=dwcourtney/JobSearchManager bash "$guard" "$fixture" >/dev/null

git -C "$fixture" remote set-url origin https://github.com/dwc5703/JobSearchManager.git
if GITHUB_REPOSITORY=dwcourtney/JobSearchManager bash "$guard" "$fixture" >/dev/null 2>&1; then
  echo "Repository guard accepted the wrong owner." >&2
  exit 1
fi

git -C "$fixture" remote set-url origin https://github.com/dwcourtney/JobSearchManager.git
if GITHUB_REPOSITORY=dwc5703/JobSearchManager bash "$guard" "$fixture" >/dev/null 2>&1; then
  echo "Repository guard accepted the wrong GitHub event repository." >&2
  exit 1
fi

echo "Repository identity guard tests passed."
