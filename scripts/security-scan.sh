#!/usr/bin/env bash
set -Eeuo pipefail

readonly trivy_image="ghcr.io/aquasecurity/trivy:0.74.0@sha256:ee940acbf1f58ebadb42d01434ce4609530bf1b52536afbd1eee66cd7123c5c9"
readonly all_severities="UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL"

mode="${1:?usage: security-scan.sh <source|image|policy-test> <target> [cache-directory]}"
target="${2:-}"
cache_directory="${3:-}"
temporary_cache=""
temporary_scan=""

cleanup() {
  [[ -z "$temporary_scan" ]] || rm -rf -- "$temporary_scan"
  [[ -z "$temporary_cache" ]] || rm -rf -- "$temporary_cache"
}
trap cleanup EXIT

if [[ -z "$cache_directory" ]]; then
  temporary_cache="$(mktemp -d)"
  cache_directory="$temporary_cache"
else
  mkdir -p -- "$cache_directory"
fi
cache_directory="$(readlink -f -- "$cache_directory")"

trivy() {
  local scan_mount="$1"
  shift
  docker run --rm \
    --platform linux/amd64 \
    --user "$(id -u):$(id -g)" \
    --read-only \
    --tmpfs /tmp \
    --security-opt no-new-privileges:true \
    --cap-drop ALL \
    --volume "$cache_directory:/cache" \
    --volume "$scan_mount" \
    "$trivy_image" \
    --cache-dir /cache \
    --disable-telemetry \
    --skip-version-check \
    "$@"
}

scan_source() {
  local source_directory
  source_directory="$(readlink -f -- "$target")"
  [[ -d "$source_directory" ]] || {
    echo "Source scan target is not a directory: $target" >&2
    exit 2
  }

  echo "Security report: tracked source dependencies and secrets (all severities; fixed vulnerabilities only)."
  trivy "$source_directory:/workspace:ro" \
    fs --scanners vuln,secret --severity "$all_severities" --ignore-unfixed --exit-code 0 /workspace

  echo "Security report: source configuration (all severities)."
  trivy "$source_directory:/workspace:ro" \
    config --severity "$all_severities" --exit-code 0 /workspace

  echo "Security gate: fixed High/Critical source dependency vulnerabilities."
  trivy "$source_directory:/workspace:ro" \
    fs --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 /workspace

  echo "Security gate: source secrets at any severity."
  trivy "$source_directory:/workspace:ro" \
    fs --scanners secret --severity "$all_severities" --exit-code 1 /workspace

  echo "Security gate: High/Critical source configuration findings."
  trivy "$source_directory:/workspace:ro" \
    config --severity HIGH,CRITICAL --exit-code 1 /workspace
}

scan_image() {
  [[ -n "$target" ]] || {
    echo "Image scan requires an image reference." >&2
    exit 2
  }
  docker image inspect "$target" >/dev/null
  temporary_scan="$(mktemp -d)"
  docker save --output "$temporary_scan/image.tar" "$target"

  echo "Security report: exact image $target (all severities; fixed vulnerabilities only)."
  trivy "$temporary_scan:/scan:ro" \
    image --input /scan/image.tar --scanners vuln,secret --severity "$all_severities" \
    --ignore-unfixed --exit-code 0

  echo "Security gate: fixed High/Critical vulnerabilities in exact image $target."
  trivy "$temporary_scan:/scan:ro" \
    image --input /scan/image.tar --scanners vuln --severity HIGH,CRITICAL \
    --ignore-unfixed --exit-code 1

  echo "Security gate: secrets in exact image $target at any severity."
  trivy "$temporary_scan:/scan:ro" \
    image --input /scan/image.tar --scanners secret --severity "$all_severities" --exit-code 1
}

expect_policy_failure() {
  local description="$1"
  shift
  set +e
  trivy "$@" >/dev/null
  local status=$?
  set -e
  if [[ "$status" -ne 37 ]]; then
    echo "Security policy self-test did not reject $description (exit $status, expected 37)." >&2
    exit 1
  fi
}

test_policy() {
  temporary_scan="$(mktemp -d)"
  mkdir -p "$temporary_scan/fixture"

  # Runtime-only synthetic fixtures prove the independent scanner fails closed.
  # They are assembled here so no secret-like value or vulnerable package is tracked.
  printf 'FROM scratch\n' > "$temporary_scan/fixture/Dockerfile"
  printf 'token=%s%s\n' 'ghp_ABCDEFGH' 'IJKLMNOPQRSTUVWXYZabcdefghij' \
    > "$temporary_scan/fixture/synthetic.env"

  expect_policy_failure "a High/Critical configuration finding" \
    "$temporary_scan/fixture:/fixture:ro" \
    config --severity HIGH,CRITICAL --exit-code 37 /fixture
  expect_policy_failure "a detected secret" \
    "$temporary_scan/fixture:/fixture:ro" \
    fs --scanners secret --severity "$all_severities" --exit-code 37 /fixture
  echo "Security policy self-test confirmed configuration and secret findings fail the gate."
}

case "$mode" in
  source)
    scan_source
    ;;
  image)
    scan_image
    ;;
  policy-test)
    test_policy
    ;;
  *)
    echo "Unknown security scan mode: $mode" >&2
    exit 2
    ;;
esac
