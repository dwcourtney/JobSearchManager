#!/usr/bin/env bash
set -Eeuo pipefail
model_root="${RESEARCH_MODEL_ROOT:?Set a research-owned model directory}"
ollama_image="${OLLAMA_IMAGE_REFERENCE:?Set an exact research Ollama image}"
model_tag="qwen3:4b-instruct-2507-q4_K_M"
model_digest="0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0"
provision_container="jsm-research-provision-$$"
[[ -d "$model_root" && "$model_root" != / ]]
trap 'docker rm -f "$provision_container" >/dev/null 2>&1 || true' EXIT
# Provision the pinned model while this temporary, unpublished container has registry egress.
# The long-running runtime is then confined to the private internal network.
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
