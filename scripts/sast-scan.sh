#!/usr/bin/env bash
set -Eeuo pipefail

readonly semgrep_image="semgrep/semgrep:1.175.0@sha256:1623685c0f6388b0bc8d577a712bf92b88252aaa09d6d7e38943dafa10ed978c"
readonly rules_commit="40b8c63f75dc7c22c8a77482d73bfb864b146f7e"

mode="${1:?usage: sast-scan.sh <source|policy-test> [repository-root]}"
repository_root="${2:-$(pwd)}"
repository_root="$(readlink -f -- "$repository_root")"
rules_root="$repository_root/security/semgrep-rules"
temporary_root=""

cleanup() {
  [[ -z "$temporary_root" ]] || rm -rf -- "$temporary_root"
}
trap cleanup EXIT

verify_rules() {
  [[ -f "$rules_root/csharp/lang/security/sqli/csharp-sqli.yaml" ]] || {
    echo "Pinned Semgrep rules submodule is not initialized." >&2
    exit 2
  }
  [[ "$(git -C "$rules_root" rev-parse HEAD)" == "$rules_commit" ]] || {
    echo "Semgrep rules are not at the approved commit $rules_commit." >&2
    exit 2
  }
  git -C "$rules_root" diff --quiet --exit-code || {
    echo "Semgrep rules submodule contains local changes." >&2
    exit 2
  }
}

run_semgrep() {
  local scan_root="$1"
  local report_directory="$2"
  shift 2
  docker run --rm \
    --platform linux/amd64 \
    --user "$(id -u):$(id -g)" \
    --read-only \
    --network none \
    --tmpfs /tmp:rw,nosuid,nodev \
    --security-opt no-new-privileges:true \
    --cap-drop ALL \
    --env HOME=/tmp \
    --env XDG_CACHE_HOME=/tmp/cache \
    --env SEMGREP_SEND_METRICS=off \
    --env SEMGREP_ENABLE_VERSION_CHECK=0 \
    --volume "$repository_root:/repo:ro" \
    --volume "$scan_root:/scan:ro" \
    --volume "$report_directory:/reports" \
    --workdir /scan \
    "$semgrep_image" \
    semgrep scan \
      --oss-only \
      --metrics off \
      --disable-version-check \
      --strict \
      --timeout 30 \
      --timeout-threshold 1 \
      "$@"
}

evaluate_report() {
  local report_directory="$1"
  docker run --rm \
    --platform linux/amd64 \
    --user "$(id -u):$(id -g)" \
    --read-only \
    --network none \
    --tmpfs /tmp:rw,nosuid,nodev \
    --security-opt no-new-privileges:true \
    --cap-drop ALL \
    --volume "$repository_root:/repo:ro" \
    --volume "$report_directory:/reports:ro" \
    "$semgrep_image" \
    python /repo/scripts/evaluate-semgrep.py /reports/semgrep.json
}

scan_source() {
  verify_rules
  temporary_root="$(mktemp -d)"
  mkdir "$temporary_root/reports"

  run_semgrep "$repository_root" "$temporary_root/reports" \
    --config /repo/security/semgrep-rules/csharp/dotnet/security \
    --config /repo/security/semgrep-rules/csharp/lang/security \
    --config /repo/security/semgrep-rules/csharp/razor/security \
    --config /repo/security/semgrep-jsm-rules \
    --exclude-rule security.semgrep-rules.csharp.dotnet.security.audit.xpath-injection \
    --include '*.cs' \
    --exclude bin \
    --exclude obj \
    --exclude Tests \
    --exclude security/semgrep-rules \
    --json-output /reports/semgrep.json \
    .
  evaluate_report "$temporary_root/reports"
}

expect_failure() {
  local expected_status="$1"
  local description="$2"
  shift 2
  set +e
  "$@"
  local status=$?
  set -e
  if [[ "$status" -ne "$expected_status" ]]; then
    echo "Semgrep policy self-test returned $status for $description; expected $expected_status." >&2
    exit 1
  fi
}

expect_nonzero() {
  local description="$1"
  shift
  set +e
  "$@"
  local status=$?
  set -e
  if [[ "$status" -eq 0 ]]; then
    echo "Semgrep policy self-test unexpectedly passed $description." >&2
    exit 1
  fi
}

test_policy() {
  verify_rules
  temporary_root="$(mktemp -d)"
  mkdir "$temporary_root/safe" "$temporary_root/unsafe" "$temporary_root/invalid" \
    "$temporary_root/safe-report" "$temporary_root/unsafe-report" "$temporary_root/invalid-report"

  printf '%s\n' \
    'using System.Security.Cryptography;' \
    'public static class SafeFixture' \
    '{' \
    '    public static byte[] CreateKey()' \
    '    {' \
    '        byte[] key = new byte[32];' \
    '        RandomNumberGenerator.Fill(key);' \
    '        return key;' \
    '    }' \
    '}' > "$temporary_root/safe/SafeFixture.cs"
  printf '%s\n' \
    'using System.Security.Cryptography;' \
    'public static class UnsafeFixture' \
    '{' \
    '    public static SymmetricAlgorithm CreateCipher()' \
    '    {' \
    '        var random = new System.Random();' \
    '        byte[] key = new byte[16];' \
    '        random.NextBytes(key);' \
    '        SymmetricAlgorithm cipher = Aes.Create();' \
    '        cipher.Key = key;' \
    '        return cipher;' \
    '    }' \
    '}' > "$temporary_root/unsafe/UnsafeFixture.cs"
  printf 'rules:\n  - id:\n' > "$temporary_root/invalid/invalid.yaml"

  local fixture_rule="/repo/security/semgrep-rules/csharp/dotnet/security/use_weak_rng_for_keygeneration.yaml"
  run_semgrep "$temporary_root/safe" "$temporary_root/safe-report" \
    --config "$fixture_rule" --include '*.cs' --json-output /reports/semgrep.json .
  evaluate_report "$temporary_root/safe-report"

  run_semgrep "$temporary_root/unsafe" "$temporary_root/unsafe-report" \
    --config "$fixture_rule" --include '*.cs' --json-output /reports/semgrep.json .
  expect_failure 42 "the unsafe C# fixture" evaluate_report "$temporary_root/unsafe-report"

  expect_nonzero "an invalid rule configuration" \
    run_semgrep "$temporary_root/invalid" "$temporary_root/invalid-report" \
      --config /scan/invalid.yaml --include '*.cs' \
      --json-output /reports/semgrep.json .
  echo "Semgrep policy self-test confirmed safe C#, blocking C#, and scanner failure behavior."
}

case "$mode" in
  source)
    scan_source
    ;;
  policy-test)
    test_policy
    ;;
  *)
    echo "Unknown SAST scan mode: $mode" >&2
    exit 2
    ;;
esac
