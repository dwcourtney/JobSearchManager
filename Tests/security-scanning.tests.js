"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const repo = path.resolve(__dirname, "..");
const scan = fs.readFileSync(path.join(repo, "scripts", "security-scan.sh"), "utf8");
const ci = fs.readFileSync(path.join(repo, "scripts", "ci-validate.sh"), "utf8");
const deploy = fs.readFileSync(path.join(repo, "scripts", "deploy-curiosity.sh"), "utf8");
const benchmarkDockerfile = fs.readFileSync(path.join(repo, "Dockerfile.hardware-benchmark"), "utf8");
const benchmarkCompose = fs.readFileSync(path.join(repo, "deploy", "compose.tinker-benchmark.yaml"), "utf8");

const trivyReference = "ghcr.io/aquasecurity/trivy:0.74.0@sha256:ee940acbf1f58ebadb42d01434ce4609530bf1b52536afbd1eee66cd7123c5c9";
assert.ok(scan.includes(trivyReference), "Trivy must be pinned by version and immutable digest.");
assert.ok(!scan.includes("/var/run/docker.sock"), "The scanner must not receive the Docker socket.");
assert.match(scan, /docker save --output .*image\.tar/, "Image scans must use an exported exact image.");
assert.match(scan, /image --input \/scan\/image\.tar/, "Trivy must scan the exported image archive.");
assert.match(scan, /--scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1/);
assert.match(scan, /--scanners secret --severity .* --exit-code 1/);
assert.match(scan, /config --severity HIGH,CRITICAL --exit-code 1/);
assert.match(scan, /--read-only/);
assert.match(scan, /--security-opt no-new-privileges:true/);
assert.match(scan, /--cap-drop ALL/);
assert.match(scan, /--disable-telemetry/);

const sourceScan = ci.indexOf("security-scan.sh source");
const imageBuild = ci.indexOf("docker build");
const imageScan = ci.indexOf("security-scan.sh image");
const candidateRun = ci.indexOf("docker run --detach");
assert.ok(sourceScan >= 0 && sourceScan < imageBuild, "CI must scan source before building.");
assert.ok(imageScan > imageBuild && imageScan < candidateRun, "CI must scan the exact image before executing it.");
assert.ok(ci.includes("security-scan.sh policy-test"), "CI must prove scanner findings fail closed.");
assert.match(ci, /Dockerfile\.hardware-benchmark[\s\S]*?security-scan\.sh image "\$benchmark_image"/,
  "CI must scan the exact RegEx-data-free hardware benchmark image.");
assert.match(benchmarkDockerfile, /rm -f[\s\S]*?LegacyJobConceptRules\.json[\s\S]*?RegexValidationCorpus\.json/);
assert.doesNotMatch(benchmarkCompose, /ports:|network_mode:\s*host/,
  "The tinker benchmark must not publish a model or application endpoint.");
assert.match(benchmarkCompose, /internal:\s*true/,
  "The tinker classifier network must be private.");
assert.doesNotMatch(benchmarkCompose, /mailpit|jsm-lab\/data|restart:\s*unless-stopped/,
  "The benchmark must not become a production service or access production/Mailpit data.");

const deployBuild = deploy.indexOf("docker build");
const deployScan = deploy.indexOf("security-scan.sh\" image");
const manifestMutation = deploy.indexOf('if [[ -f "$active_manifest" ]]');
const replacement = deploy.indexOf("replacement_started=true");
assert.ok(deployBuild >= 0 && deployScan > deployBuild, "Deployment must scan the locally built image.");
assert.ok(deployScan < manifestMutation && deployScan < replacement,
  "Deployment must pass scanning before manifest or running-container changes.");

for (const protectedPath of ["/home/codex/jsm-lab/data/app", "/home/codex/jsm-lab/data/dataprotection"]) {
  assert.ok(!scan.includes(protectedPath), `Scanner must not access persistent path ${protectedPath}.`);
}

console.log("Security scanning integration tests passed.");
