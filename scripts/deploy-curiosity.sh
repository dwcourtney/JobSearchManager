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
previous_classifier_reference=""
classifier_was_running=false
previous_ollama_reference=""
ollama_was_running=false
ollama_image="jsm-ollama:$target_sha"
ollama_source_revision="f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a"
model_tag="qwen3:4b-instruct-2507-q4_K_M"
model_digest="0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0"
provision_container="jsm-ollama-provision-${target_sha:0:12}"

cleanup_provision() {
  docker rm -f "$provision_container" >/dev/null 2>&1 || true
}
trap cleanup_provision EXIT

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
model_root="$lab_root/models/ollama"
mkdir -p "$model_root"
sudo -n chown -R 65532:65532 "$model_root"
deberta_model_root="$lab_root/models/nli-deberta-v3-base"
mkdir -p "$deberta_model_root"
sudo -n chown -R 65532:65532 "$deberta_model_root"

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
current_classifier="$(docker ps -q \
  --filter label=com.docker.compose.project=jsm-lab \
  --filter label=com.docker.compose.service=job-classifier | head -n 1)"
if [[ -n "$current_classifier" ]]; then
  classifier_was_running=true
  previous_classifier_reference="$(docker inspect --format '{{.Image}}' "$current_classifier")"
fi
current_ollama="$(docker ps -q \
  --filter label=com.docker.compose.project=jsm-lab \
  --filter label=com.docker.compose.service=ollama | head -n 1)"
if [[ -n "$current_ollama" ]]; then
  ollama_was_running=true
  previous_ollama_reference="$(docker inspect --format '{{.Image}}' "$current_ollama")"
fi
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

docker build \
  --platform linux/amd64 \
  --build-arg "JSM_GIT_SHA=$target_sha" \
  --tag "jsm:$target_sha" \
  "$repository_root"

docker build \
  --platform linux/amd64 \
  --build-arg "CLASSIFIER_GIT_SHA=$target_sha" \
  --tag "jsm-classifier:$target_sha" \
  --file "$repository_root/classifier-service/Dockerfile" \
  "$repository_root"

docker build \
  --platform linux/amd64 \
  --build-arg "JSM_GIT_SHA=$target_sha" \
  --build-arg "OLLAMA_SOURCE_REVISION=$ollama_source_revision" \
  --tag "$ollama_image" \
  "$repository_root/ollama-runtime"

image_revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "jsm:$target_sha")"
[[ "$image_revision" == "$target_sha" ]] || {
  echo "Image revision $image_revision does not match deployment SHA $target_sha." >&2
  exit 1
}
classifier_revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "jsm-classifier:$target_sha")"
[[ "$classifier_revision" == "$target_sha" ]] || {
  echo "Classifier image revision $classifier_revision does not match deployment SHA $target_sha." >&2
  exit 1
}
ollama_revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$ollama_image")"
ollama_source="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.source-revision" }}' "$ollama_image")"
[[ "$ollama_revision" == "$target_sha" && "$ollama_source" == "$ollama_source_revision" ]] || {
  echo "Ollama runtime identity does not match the deployment inputs." >&2
  exit 1
}

# Scan the exact locally built artifact before any manifest or running container changes.
bash "$repository_root/scripts/security-scan.sh" image "jsm:$target_sha" "$security_cache"
bash "$repository_root/scripts/security-scan.sh" image "jsm-classifier:$target_sha" "$security_cache"
bash "$repository_root/scripts/security-scan.sh" image "$ollama_image" "$security_cache"

# Restore/validate the exact immutable DeBERTa snapshot retained by the Phase 2B contract.
# Only this bounded provisioning container has registry egress; production remains internal-only.
docker run --rm --user 65532:65532 --env HOME=/tmp --tmpfs /tmp --env CLASSIFIER_MODEL_ROOT=/models/nli-deberta-v3-base --volume "$deberta_model_root:/models/nli-deberta-v3-base" "jsm-classifier:$target_sha" --download-model

# Phase 0 host inventory and a non-mutating GPU-container preflight happen before replacement.
echo "Docker $(docker version --format '{{.Server.Version}}'); Compose $(docker compose version --short)."
nvidia-smi --query-gpu=name,driver_version,memory.total,memory.used --format=csv,noheader
command -v nvidia-ctk >/dev/null || {
  echo "NVIDIA Container Toolkit is required but nvidia-ctk is absent. No host configuration was changed." >&2
  exit 1
}
docker info --format 'Docker runtimes: {{json .Runtimes}}'
docker run --rm --gpus all --entrypoint nvidia-smi "$ollama_image" \
  --query-gpu=name --format=csv,noheader

# Provision the pinned model while this temporary, unpublished container has registry egress.
# The long-running runtime is then confined to the private internal network.
docker rm -f "$provision_container" >/dev/null 2>&1 || true
docker run --detach --name "$provision_container" --gpus all --user 65532:65532 \
  --read-only --tmpfs /tmp --security-opt no-new-privileges:true --cap-drop ALL \
  --env HOME=/tmp --env OLLAMA_HOST=127.0.0.1:11434 --env OLLAMA_MODELS=/models \
  --volume "$model_root:/models" "$ollama_image" serve >/dev/null
for _ in $(seq 1 30); do
  docker exec "$provision_container" ollama list >/dev/null 2>&1 && break
  sleep 1
done
docker exec "$provision_container" ollama pull "$model_tag"
manifest_path="$model_root/manifests/registry.ollama.ai/library/qwen3/4b-instruct-2507-q4_K_M"
[[ -f "$manifest_path" ]] || { echo "Pinned Ollama model manifest is absent." >&2; exit 1; }
[[ "$(sha256sum "$manifest_path" | awk '{print $1}')" == "$model_digest" ]] || {
  echo "Pinned Ollama model manifest digest validation failed." >&2; exit 1;
}
docker rm -f "$provision_container" >/dev/null

if [[ -f "$active_manifest" ]]; then
  cp -- "$active_manifest" "$previous_manifest.tmp"
  mv -f -- "$previous_manifest.tmp" "$previous_manifest"
else
  cp -- "$manifest_source" "$previous_manifest.tmp"
  mv -f -- "$previous_manifest.tmp" "$previous_manifest"
fi
cp -- "$manifest_source" "$active_manifest.tmp"
mv -f -- "$active_manifest.tmp" "$active_manifest"

verify_deployment() {
  local expected_sha="$1"
  local allow_legacy="$2"
  local health=""
  for _ in $(seq 1 60); do
    health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
      "$(docker ps -aq --filter label=com.docker.compose.project=jsm-lab --filter label=com.docker.compose.service=jsm | head -n 1)")"
    [[ "$health" == "healthy" ]] && break
    [[ "$health" == "unhealthy" ]] && return 1
    sleep 2
  done
  [[ "$health" == "healthy" ]] || return 1
  [[ "$(curl --fail --silent --show-error http://192.168.1.20:8080/healthz)" == "Healthy" ]] || return 1

  local classifier_container=""
  classifier_container="$(docker ps -q --filter label=com.docker.compose.project=jsm-lab \
    --filter label=com.docker.compose.service=job-classifier | head -n 1)"
  [[ -n "$classifier_container" ]] || return 1
  local classifier_health=""
  for _ in $(seq 1 30); do
    classifier_health="$(docker inspect --format '{{.State.Health.Status}}' "$classifier_container")"
    [[ "$classifier_health" == "healthy" ]] && break
    [[ "$classifier_health" == "unhealthy" ]] && return 1
    sleep 1
  done
  [[ "$classifier_health" == "healthy" ]] || return 1
  if [[ "$allow_legacy" != "true" ]]; then
    local ollama_container=""
    ollama_container="$(docker ps -q --filter label=com.docker.compose.project=jsm-lab \
      --filter label=com.docker.compose.service=ollama | head -n 1)"
    [[ -n "$ollama_container" ]] || return 1
    [[ "$(docker inspect --format '{{.State.Health.Status}}' "$ollama_container")" == "healthy" ]] || return 1
    [[ "$(docker exec "$ollama_container" nvidia-smi --query-gpu=name --format=csv,noheader | tr -d '\r')" \
      == "NVIDIA GeForce GTX 1070" ]] || return 1
    docker exec "$ollama_container" ollama list | grep -F "$model_tag" >/dev/null || return 1
    docker exec "$classifier_container" python3 /app/classifier_service.py --model-diagnostic || return 1
  fi

  if [[ "$allow_legacy" != "true" ]]; then
    local jsm_container=""
    jsm_container="$(docker ps -q --filter label=com.docker.compose.project=jsm-lab \
      --filter label=com.docker.compose.service=jsm | head -n 1)"
    local classifier_round_trip=false
    for _ in $(seq 1 3); do
      if docker exec "$jsm_container" dotnet JobSearchManager.dll --classifier-diagnostic; then
        classifier_round_trip=true
        break
      fi
      sleep 2
    done
    [[ "$classifier_round_trip" == "true" ]] || return 1
  fi

  if [[ -n "$expected_sha" ]]; then
    version_json="$(curl --fail --silent --show-error http://192.168.1.20:8080/version)" || return 1
    [[ "$version_json" =~ \"commit\"[[:space:]]*:[[:space:]]*\"$expected_sha\" ]] || return 1
  elif [[ "$allow_legacy" != "true" ]]; then
    return 1
  fi
}

rollback() {
  failure_status=$?
  trap - ERR
  if [[ "$replacement_started" == true && -n "$previous_reference" ]]; then
    echo "Deployment verification failed; restoring the previous JSM/classifier state." >&2
    JSM_IMAGE_REFERENCE="jsm:$target_sha" CLASSIFIER_IMAGE_REFERENCE="jsm-classifier:$target_sha" \
      OLLAMA_IMAGE_REFERENCE="$ollama_image" JSM_LAB_ROOT="$lab_root" \
      docker compose --project-name jsm-lab --file "$active_manifest" \
      rm --stop --force ollama job-classifier || true
    if [[ "$classifier_was_running" != true ]]; then
      JSM_IMAGE_REFERENCE="jsm:$target_sha" CLASSIFIER_IMAGE_REFERENCE="jsm-classifier:$target_sha" \
        OLLAMA_IMAGE_REFERENCE="$ollama_image" JSM_LAB_ROOT="$lab_root" docker compose --project-name jsm-lab --file "$active_manifest" \
        rm --stop --force job-classifier || true
    fi
    mv -f -- "$previous_manifest" "$active_manifest"
    if [[ "$classifier_was_running" == true ]]; then
      JSM_IMAGE_REFERENCE="$previous_reference" \
        CLASSIFIER_IMAGE_REFERENCE="$previous_classifier_reference" \
        OLLAMA_IMAGE_REFERENCE="${previous_ollama_reference:-$ollama_image}" JSM_LAB_ROOT="$lab_root" \
        docker compose --project-name jsm-lab --file "$active_manifest" \
        up --detach --force-recreate jsm job-classifier
    else
      JSM_IMAGE_REFERENCE="$previous_reference" JSM_LAB_ROOT="$lab_root" \
        docker compose --project-name jsm-lab --file "$active_manifest" \
        up --detach --no-deps --force-recreate jsm
    fi
    if [[ "$classifier_was_running" == true ]] && verify_deployment "$previous_sha" true; then
      echo "Rollback health verification succeeded${previous_sha:+ at $previous_sha}." >&2
    elif [[ "$classifier_was_running" != true ]] && \
      [[ "$(curl --fail --silent --show-error http://192.168.1.20:8080/healthz)" == "Healthy" ]]; then
      echo "Legacy JSM-only rollback health verification succeeded." >&2
    else
      echo "Rollback did not pass verification; JSM requires operator attention." >&2
    fi
    docker image rm "jsm:$target_sha" >/dev/null 2>&1 || true
    docker image rm "jsm-classifier:$target_sha" >/dev/null 2>&1 || true
  fi
  exit "$failure_status"
}
trap rollback ERR

replacement_started=true
JSM_IMAGE_REFERENCE="jsm:$target_sha" CLASSIFIER_IMAGE_REFERENCE="jsm-classifier:$target_sha" \
  OLLAMA_IMAGE_REFERENCE="$ollama_image" \
  JSM_LAB_ROOT="$lab_root" \
  docker compose --project-name jsm-lab --file "$active_manifest" \
  up --detach --no-deps ollama
JSM_IMAGE_REFERENCE="jsm:$target_sha" CLASSIFIER_IMAGE_REFERENCE="jsm-classifier:$target_sha" \
  OLLAMA_IMAGE_REFERENCE="$ollama_image" \
  JSM_LAB_ROOT="$lab_root" \
  docker compose --project-name jsm-lab --file "$active_manifest" \
  up --detach --no-deps job-classifier
JSM_IMAGE_REFERENCE="jsm:$target_sha" CLASSIFIER_IMAGE_REFERENCE="jsm-classifier:$target_sha" \
  OLLAMA_IMAGE_REFERENCE="$ollama_image" \
  JSM_LAB_ROOT="$lab_root" \
  docker compose --project-name jsm-lab --file "$active_manifest" \
  up --detach --no-deps --force-recreate jsm
verify_deployment "$target_sha" false
docker stats --no-stream --format '{{.Name}} cpu={{.CPUPerc}} memory={{.MemUsage}}' \
  "$(docker ps -q --filter label=com.docker.compose.project=jsm-lab --filter label=com.docker.compose.service=job-classifier)"
nvidia-smi --query-gpu=name,memory.total,memory.used --format=csv,noheader
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
    docker image rm "jsm:$old_sha"
  done
  printf '%s\n' "${successful_images[@]:remove_count}" > "$history_file.tmp"
  mv -f -- "$history_file.tmp" "$history_file"
fi

echo "JSM/classifier deployment succeeded at $target_sha. Mailpit and unrelated Docker resources were not operated."
