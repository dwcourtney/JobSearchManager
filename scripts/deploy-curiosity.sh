#!/usr/bin/env bash
set -Eeuo pipefail

target_sha="${1:?usage: deploy-curiosity.sh <full-git-sha> [repository-root]}"
repository_root="${2:-$(pwd)}"
lab_root="/home/codex/jsm-lab"
state_root="/home/codex/jsm-cicd"
security_cache="$state_root/trivy-cache"
manifest_source="$repository_root/deploy/compose.curiosity.yaml"
active_manifest="$state_root/compose.curiosity.yaml"
previous_manifest="$state_root/compose.curiosity.previous.yaml"
history_file="$state_root/successful-images"
deployed_sha_file="$state_root/deployed-sha"
replacement_started=false
previous_reference=""
previous_sha=""


if [[ ! "$target_sha" =~ ^[0-9a-f]{40}$ ]]; then
  echo "Deployment requires a lowercase full Git SHA." >&2
  exit 2
fi

bash "$repository_root/scripts/verify-repository-identity.sh" "$repository_root"

actual_sha="$(git -C "$repository_root" rev-parse HEAD)"
[[ "$actual_sha" == "$target_sha" ]] || {
  echo "Checked-out SHA $actual_sha does not match deployment SHA $target_sha." >&2
  exit 1
}
[[ -f "$manifest_source" ]] || {
  echo "Deployment manifest is missing: $manifest_source" >&2
  exit 1
}
[[ -d "$lab_root/data/app" && -d "$lab_root/data/dataprotection" && -d "$lab_root/backups" ]] || {
  echo "Required persistent JSM directories are missing; refusing deployment." >&2
  exit 1
}
mkdir -p "$state_root"
mkdir -p "$security_cache"
exec 9>"$state_root/deploy.lock"
flock -n 9 || {
  echo "Another JSM deployment is active." >&2
  exit 1
}

current_container="$(docker ps -q \
  --filter label=com.docker.compose.project=jsm-lab \
  --filter label=com.docker.compose.service=jsm | head -n 1)"
[[ -n "$current_container" ]] || {
  echo "No running JSM Compose container was found; refusing a non-replacement deployment." >&2
  exit 1
}
previous_reference="$(docker inspect --format '{{.Image}}' "$current_container")"
previous_sha="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$previous_reference" 2>/dev/null || true)"
[[ "$previous_sha" =~ ^[0-9a-f]{40}$ ]] || previous_sha=""
if [[ -f "$deployed_sha_file" ]]; then
  read -r recorded_sha < "$deployed_sha_file"
  if [[ "$recorded_sha" =~ ^[0-9a-f]{40}$ ]]; then
    if [[ -n "$previous_sha" && "$previous_sha" != "$recorded_sha" ]]; then
      echo "Running image revision and recorded deployed SHA disagree; refusing deployment." >&2
      exit 1
    fi
    previous_sha="$recorded_sha"
  fi
fi

# This unused historical file is copied byte-for-byte solely for rollback preservation.
# A live/nonempty WAL belongs to legacy tooling; never checkpoint or modify it here.
if [[ -f "$lab_root/data/app/regex-rules.db" ]]; then
  [[ ! -s "$lab_root/data/app/regex-rules.db-wal" ]] || {
    echo "Historical database has a nonempty WAL; seal it with isolated archive tooling before deployment." >&2
    exit 1
  }
  backup_name="regex-rules-predeploy-${previous_sha:-unknown}-$(date -u +%Y%m%dT%H%M%SZ).db"
  cp --preserve=mode,timestamps -- "$lab_root/data/app/regex-rules.db" "$lab_root/backups/$backup_name"
  cmp --silent -- "$lab_root/data/app/regex-rules.db" "$lab_root/backups/$backup_name"
fi

docker build \
  --platform linux/amd64 \
  --build-arg "JSM_GIT_SHA=$target_sha" \
  --tag "jsm:$target_sha" \
  "$repository_root"

image_revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "jsm:$target_sha")"
[[ "$image_revision" == "$target_sha" ]] || {
  echo "Image revision $image_revision does not match deployment SHA $target_sha." >&2
  exit 1
}
bash "$repository_root/scripts/security-scan.sh" image "jsm:$target_sha" "$security_cache"

# Validate the exact JSON-authoritative matcher against immutable curated confusion matrices
# and every cached posting, with production caches mounted read-only and no database/provider access.
cache_benchmark_report="$state_root/json-concept-audit-$target_sha.json"
docker run --rm --user 1001:1001 --read-only --tmpfs /tmp \
  --volume "$lab_root/data/app/workspaces:/production/workspaces:ro" \
  "jsm:$target_sha" --json-concept-audit /production/workspaces > "$cache_benchmark_report"
python3 - "$cache_benchmark_report" <<'PY'
import json, sys
report = json.load(open(sys.argv[1], encoding="utf-8"))
if not report["curatedExact"] or report["curatedPostings"] != 148 or report["classifiedJobs"] < 1:
    raise SystemExit("JSON concept candidate regression or empty cache benchmark.")
if report["authority"]["authority"] != "json-regex-v1" or report["cacheWritten"] or report["providersCalled"]:
    raise SystemExit("Invalid JSON concept audit boundary.")
print("JSON concept candidate confusion matrices and production-cache benchmark passed:", report)
PY

if [[ -f "$active_manifest" ]]; then
  cp -- "$active_manifest" "$previous_manifest.tmp"
  mv -f -- "$previous_manifest.tmp" "$previous_manifest"
else
  cp -- "$manifest_source" "$previous_manifest.tmp"
  mv -f -- "$previous_manifest.tmp" "$previous_manifest"
fi
docker compose --project-name jsm-lab --file "$previous_manifest" config --no-interpolate --no-consistency --no-env-resolution --format json |
  python3 "$repository_root/scripts/jsm-rollback-manifest.py" > "$state_root/rollback-jsm.json"
cp -- "$manifest_source" "$active_manifest.tmp"
mv -f -- "$active_manifest.tmp" "$active_manifest"

verify_deployment() {
  local expected_sha="$1"
  local health=""
  for _ in $(seq 1 60); do
    health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
      "$(docker ps -aq --filter label=com.docker.compose.project=jsm-lab --filter label=com.docker.compose.service=jsm | head -n 1)")"
    [[ "$health" == healthy ]] && break
    [[ "$health" != unhealthy ]] || return 1
    sleep 2
  done
  [[ "$health" == healthy ]] || return 1
  [[ "$(curl --fail --silent --show-error http://192.168.1.20:8080/healthz)" == Healthy ]] || return 1
  if [[ -n "$expected_sha" ]]; then
    local version_json
    version_json="$(curl --fail --silent --show-error http://192.168.1.20:8080/version)" || return 1
    [[ "$version_json" =~ \"commit\"[[:space:]]*:[[:space:]]*\"$expected_sha\" ]] || return 1
  fi
}

rollback() {
  failure_status=$?
  trap - ERR
  if [[ "$replacement_started" == true && -n "$previous_reference" ]]; then
    echo "Deployment verification failed; restoring previous JSM image and service settings." >&2
    JSM_IMAGE_REFERENCE="$previous_reference" JSM_LAB_ROOT="$lab_root" \
      docker compose --project-name jsm-lab --file "$state_root/rollback-jsm.json" \
      up --detach --no-deps --pull never --force-recreate jsm
    cp -- "$state_root/rollback-jsm.json" "$active_manifest"
    if verify_deployment "$previous_sha"; then
      echo "Rollback health/version verification succeeded." >&2
    else
      echo "Rollback did not pass verification; JSM requires operator attention." >&2
    fi
  fi
  exit "$failure_status"
}
trap rollback ERR

replacement_started=true
JSM_IMAGE_REFERENCE="jsm:$target_sha" JSM_LAB_ROOT="$lab_root" \
  docker compose --project-name jsm-lab --file "$active_manifest" \
  up --detach --no-deps --pull never --force-recreate jsm
verify_deployment "$target_sha"
replacement_started=false
trap - ERR

printf '%s\n' "$target_sha" > "$deployed_sha_file.tmp"
mv -f -- "$deployed_sha_file.tmp" "$deployed_sha_file"

touch "$history_file"
: > "$history_file.tmp"
while IFS= read -r recorded_image; do
  [[ "$recorded_image" =~ ^[0-9a-f]{40}$ ]] || continue
  [[ "$recorded_image" == "$target_sha" ]] && continue
  docker image inspect "jsm:$recorded_image" >/dev/null 2>&1 || continue
  grep -qxF "$recorded_image" "$history_file.tmp" || printf '%s\n' "$recorded_image" >> "$history_file.tmp"
done < "$history_file"
while IFS= read -r discovered_image; do
  [[ "$discovered_image" =~ ^[0-9a-f]{40}$ ]] || continue
  [[ "$discovered_image" == "$target_sha" ]] && continue
  grep -qxF "$discovered_image" "$history_file.tmp" || printf '%s\n' "$discovered_image" >> "$history_file.tmp"
done < <(docker image ls --format '{{.Repository}} {{.Tag}}' | awk '$1 == "jsm" { print $2 }')
printf '%s\n' "$target_sha" >> "$history_file.tmp"
mv -f -- "$history_file.tmp" "$history_file"

mapfile -t successful_images < "$history_file"
if (( ${#successful_images[@]} > 5 )); then
  remove_count=$(( ${#successful_images[@]} - 5 ))
  for (( index=0; index<remove_count; index++ )); do
    old_sha="${successful_images[$index]}"
    [[ "$old_sha" =~ ^[0-9a-f]{40}$ ]] || continue
    if [[ -n "$(docker ps --all --quiet --filter "ancestor=jsm:$old_sha")" ]]; then
      echo "Retaining jsm:$old_sha because a stopped or running container still references it."
      continue
    fi
    docker image rm "jsm:$old_sha"
  done
  printf '%s\n' "${successful_images[@]:remove_count}" > "$history_file.tmp"
  mv -f -- "$history_file.tmp" "$history_file"
fi

echo "JSM deterministic deployment succeeded at $target_sha. Unrelated services and model data were not operated."
