#!/usr/bin/env bash
set -Eeuo pipefail

expected_sha="${1:?usage: ci-validate.sh <full-git-sha>}"
security_cache="$(mktemp -d)"
temporary_root=""
container=""
classifier_container=""

cleanup() {
  [[ -z "$container" ]] || docker rm -f "$container" >/dev/null 2>&1 || true
  [[ -z "$classifier_container" ]] || docker rm -f "$classifier_container" >/dev/null 2>&1 || true
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

classifier_image="jsm-classifier-ci:$expected_sha"
classifier_container="jsm-classifier-ci-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}"
docker build \
  --platform linux/amd64 \
  --build-arg "CLASSIFIER_GIT_SHA=$expected_sha" \
  --tag "$classifier_image" \
  classifier-service
classifier_revision="$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$classifier_image")"
[[ "$classifier_revision" == "$expected_sha" ]] || {
  echo "Classifier image revision $classifier_revision does not match $expected_sha." >&2
  exit 1
}
bash scripts/security-scan.sh image "$classifier_image" "$security_cache"
docker run --detach \
  --name "$classifier_container" \
  --user 65532:65532 \
  --read-only \
  --tmpfs /tmp \
  --security-opt no-new-privileges:true \
  --cap-drop ALL \
  --publish 127.0.0.1::8081 \
  "$classifier_image" >/dev/null
for _ in $(seq 1 30); do
  classifier_health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$classifier_container")"
  [[ "$classifier_health" == "healthy" ]] && break
  [[ "$classifier_health" == "unhealthy" ]] && { docker logs "$classifier_container" >&2; exit 1; }
  sleep 1
done
[[ "$classifier_health" == "healthy" ]]
classifier_port="$(docker port "$classifier_container" 8081/tcp | sed -n 's/.*://p' | head -n 1)"
classifier_health_json="$(curl --fail --silent --show-error "http://127.0.0.1:${classifier_port}/healthz")"
classifier_response="$(curl --fail --silent --show-error \
  --header 'Content-Type: application/json' \
  --data '{"jobId":"R180395","title":"Senior Software Developer","description":"phase one"}' \
  "http://127.0.0.1:${classifier_port}/classify")"
malformed_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --header 'Content-Type: application/json' --data '{' \
  "http://127.0.0.1:${classifier_port}/classify")"
missing_id_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"title":"Senior Software Developer","description":"phase one"}' \
  "http://127.0.0.1:${classifier_port}/classify")"
model_unavailable_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"jobId":"fixture","title":"Backend Engineer","description":"Build APIs."}' \
  "http://127.0.0.1:${classifier_port}/classify-embedding")"
[[ "$malformed_status" == "400" && "$missing_id_status" == "400" && "$model_unavailable_status" == "503" ]]
node -e '
const health = JSON.parse(process.argv[1]);
const result = JSON.parse(process.argv[2]);
const sha = process.argv[3];
if (health.status !== "healthy" || health.gpuAvailable !== false || health.revision !== sha) process.exit(1);
if (!result.received || result.jobId !== "R180395" || result.title !== "Senior Software Developer" || result.descriptionLength !== 9) process.exit(1);
if (result.serviceVersion !== "0.3.0" || result.protocolVersion !== "3") process.exit(1);
' "$classifier_health_json" "$classifier_response" "$expected_sha"

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
