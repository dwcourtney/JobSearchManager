#!/usr/bin/env bash
set -Eeuo pipefail

expected_sha="${1:?usage: ci-validate.sh <full-git-sha>}"
security_cache="$(mktemp -d)"
temporary_root=""
container=""
deep_analysis_container=""

cleanup() {
  [[ -z "$container" ]] || docker rm -f "$container" >/dev/null 2>&1 || true
  [[ -z "$deep_analysis_container" ]] || docker rm -f "$deep_analysis_container" >/dev/null 2>&1 || true
  [[ -z "$temporary_root" ]] || rm -rf -- "$temporary_root"
  rm -rf -- "$security_cache"
}
trap cleanup EXIT

if [[ ! "$expected_sha" =~ ^[0-9a-f]{40}$ ]]; then
  echo "Expected a lowercase full Git SHA." >&2
  exit 2
fi

actual_sha="$(git rev-parse HEAD)"
if [[ "$actual_sha" != "$expected_sha" ]]; then
  echo "Checked-out SHA $actual_sha does not match expected SHA $expected_sha." >&2
  exit 1
fi

bash scripts/verify-repository-identity.sh "$(pwd)"
bash scripts/test-repository-identity.sh

dotnet restore JobSearchManager.csproj --locked-mode
dotnet restore Tests/JobSearchManager.Tests.csproj --locked-mode
dotnet build JobSearchManager.csproj --configuration Release --no-restore
dotnet run --project Tests/JobSearchManager.Tests.csproj --configuration Release --no-restore
python3 classifier-service/classifier_service.py --self-test
pwsh -NoLogo -NoProfile -File scripts/validate-source.ps1
pwsh -NoLogo -NoProfile -File scripts/audit-repository.ps1
bash scripts/security-scan.sh source "$(pwd)" "$security_cache"
bash scripts/security-scan.sh policy-test unused "$security_cache"
git diff --check

if [[ -n "$(git status --porcelain --untracked-files=all)" ]]; then
  echo "Validation changed the Git worktree:" >&2
  git status --short >&2
  exit 1
fi

image="jsm-ci:$expected_sha"
container="jsm-ci-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}"
temporary_root="$(mktemp -d)"
mkdir -p "$temporary_root/app" "$temporary_root/dataprotection"
chmod 0777 "$temporary_root/app" "$temporary_root/dataprotection"

docker build \
  --platform linux/amd64 \
  --build-arg "JSM_GIT_SHA=$expected_sha" \
  --tag "$image" \
  .

image_revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$image")"
if [[ "$image_revision" != "$expected_sha" ]]; then
  echo "Image revision $image_revision does not match $expected_sha." >&2
  exit 1
fi

bash scripts/security-scan.sh image "$image" "$security_cache"

deep_analysis_image="jsm-deep-analysis-ci:$expected_sha"
deep_analysis_container="jsm-deep-analysis-ci-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}"
docker build \
  --platform linux/amd64 \
  --build-arg "CLASSIFIER_GIT_SHA=$expected_sha" \
  --tag "$deep_analysis_image" \
  --file classifier-service/Dockerfile \
  .
deep_analysis_revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$deep_analysis_image")"
[[ "$deep_analysis_revision" == "$expected_sha" ]] || {
  echo "Deep-analysis image revision $deep_analysis_revision does not match $expected_sha." >&2
  exit 1
}
bash scripts/security-scan.sh image "$deep_analysis_image" "$security_cache"
ollama_image="jsm-ollama-ci:$expected_sha"
docker build \
  --platform linux/amd64 \
  --build-arg "JSM_GIT_SHA=$expected_sha" \
  --build-arg "OLLAMA_SOURCE_REVISION=f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a" \
  --tag "$ollama_image" \
  ollama-runtime
ollama_labels="$(docker image inspect --format '{{json .Config.Labels}}' "$ollama_image")"
[[ "$ollama_labels" == *"$expected_sha"* && \
   "$ollama_labels" == *"f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a"* ]] || {
  echo "Ollama runtime labels do not match the candidate/source revisions." >&2
  exit 1
}
bash scripts/security-scan.sh image "$ollama_image" "$security_cache"
docker run --detach \
  --name "$deep_analysis_container" \
  --user 65532:65532 \
  --read-only \
  --tmpfs /tmp \
  --security-opt no-new-privileges:true \
  --cap-drop ALL \
  --publish 127.0.0.1::8081 \
  "$deep_analysis_image" >/dev/null
for _ in $(seq 1 30); do
  deep_analysis_health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$deep_analysis_container")"
  [[ "$deep_analysis_health" == "healthy" ]] && break
  [[ "$deep_analysis_health" == "unhealthy" ]] && { docker logs "$deep_analysis_container" >&2; exit 1; }
  sleep 1
done
[[ "$deep_analysis_health" == "healthy" ]]
deep_analysis_port="$(docker port "$deep_analysis_container" 8081/tcp | sed -n 's/.*://p' | head -n 1)"
deep_analysis_health_json="$(curl --fail --silent --show-error "http://127.0.0.1:${deep_analysis_port}/healthz")"
removed_classifier_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"jobId":"R180395","title":"Senior Software Developer","description":"phase one"}' \
  "http://127.0.0.1:${deep_analysis_port}/classify")"
malformed_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --header 'Content-Type: application/json' --data '{' \
  "http://127.0.0.1:${deep_analysis_port}/deep-analyze")"
missing_id_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"title":"Senior Software Developer","description":"phase one"}' \
  "http://127.0.0.1:${deep_analysis_port}/deep-analyze")"
[[ "$malformed_status" == "400" && "$missing_id_status" == "400" && \
   "$removed_classifier_status" == "404" ]]
node -e '
const health = JSON.parse(process.argv[1]);
const sha = process.argv[2];
const require = (condition, message) => {
  if (!condition) {
    console.error(`Deep-analysis identity validation failed: ${message}`);
    process.exit(1);
  }
};
require(health.status === "healthy" && health.revision === sha, "health or revision mismatch");
require(health.serviceVersion === "3.2.0" && health.protocolVersion === "9", "service protocol mismatch");
require(health.outputContractVersion === "compact-85-boolean-map-v2" &&
  health.outputSchemaHash === "15e934183d07749e0db4c3cb4f3bef51a5507285fad23025196ae4f184ad2ef8",
  "structured output contract mismatch");
require(health.purpose === "opt-in-llm-deep-analysis" && health.conceptCount === 85, "purpose or taxonomy size mismatch");
require(/^[0-9a-f]{64}$/.test(health.taxonomyFingerprint) && /^[0-9a-f]{64}$/.test(health.promptHash), "invalid taxonomy or prompt fingerprint");
' "$deep_analysis_health_json" "$expected_sha"

docker run --detach \
  --name "$container" \
  --user 1001:1001 \
  --read-only \
  --tmpfs /tmp \
  --security-opt no-new-privileges:true \
  --cap-drop ALL \
  --env JOBSEARCHMANAGER_HOSTING_MODE=Container \
  --env JOBSEARCHMANAGER_DATA_PROTECTION_PATH=/var/lib/jsm/dataprotection \
  --env ASPNETCORE_URLS=http://0.0.0.0:8080 \
  --publish 127.0.0.1::8080 \
  --volume "$temporary_root/app:/app/data" \
  --volume "$temporary_root/dataprotection:/var/lib/jsm/dataprotection" \
  "$image" >/dev/null

for _ in $(seq 1 45); do
  health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container")"
  [[ "$health" == "healthy" ]] && break
  [[ "$health" == "unhealthy" ]] && {
    docker logs "$container" >&2
    exit 1
  }
  sleep 2
done

if [[ "$(docker inspect --format '{{.State.Health.Status}}' "$container")" != "healthy" ]]; then
  docker logs "$container" >&2
  echo "Candidate container did not become healthy." >&2
  exit 1
fi

host_port="$(docker port "$container" 8080/tcp | sed -n 's/.*://p' | head -n 1)"
health_body="$(curl --fail --silent --show-error "http://127.0.0.1:${host_port}/healthz")"
[[ "$health_body" == "Healthy" ]] || {
  echo "Unexpected /healthz response: $health_body" >&2
  exit 1
}

version_json="$(curl --fail --silent --show-error "http://127.0.0.1:${host_port}/version")"
node -e '
const payload = JSON.parse(process.argv[1]);
const expected = process.argv[2];
const keys = Object.keys(payload).sort().join(",");
if (payload.commit !== expected || !payload.version || payload.hostingMode !== "Container") process.exit(1);
if (keys !== "commit,hostingMode,version") process.exit(1);
' "$version_json" "$expected_sha"

echo "Validated checkout, image label, health, and /version at $expected_sha."
