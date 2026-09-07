#!/usr/bin/env bash
# Explicit research validation. Never invoked by normal JSM CI/deployment.
set -Eeuo pipefail
cd "$(dirname "$0")/../.."
sha="${1:?usage: research/qwen/validate.sh <full-git-sha>}"
[[ "$sha" =~ ^[0-9a-f]{40}$ ]]
bash scripts/verify-repository-identity.sh "$(pwd)"
[[ "$(git rev-parse HEAD)" == "$sha" ]]
dotnet restore research/qwen/Tests/Jsm.Qwen.Research.Tests.csproj --locked-mode
dotnet run --project research/qwen/Tests/Jsm.Qwen.Research.Tests.csproj -c Release --no-restore
python3 classifier-service/classifier_service.py --self-test
node Tests/llm-holdout-evaluation.tests.js
docker build --build-arg CLASSIFIER_GIT_SHA="$sha" -f classifier-service/Dockerfile -t "jsm-research-adapter:$sha" .
docker build --build-arg JSM_GIT_SHA="$sha" -f Dockerfile.hardware-benchmark -t "jsm-research-benchmark:$sha" .
docker build --build-arg JSM_GIT_SHA="$sha" -t "jsm-research-ollama:$sha" ollama-runtime
for image in "jsm-research-adapter:$sha" "jsm-research-benchmark:$sha" "jsm-research-ollama:$sha"; do
  [[ "$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image")" == "$sha" ]]
  bash scripts/security-scan.sh image "$image"
done
DEEP_ANALYSIS_IMAGE_REFERENCE="jsm-research-adapter:$sha" OLLAMA_IMAGE_REFERENCE="jsm-research-ollama:$sha" \
  BENCHMARK_IMAGE_REFERENCE="jsm-research-benchmark:$sha" BENCHMARK_ROOT=/tmp/jsm-research-config-check \
  docker compose -f research/qwen/compose.yaml config --quiet
echo 'Research contracts, images/security and separate Compose validation passed. No model downloaded or started.'
