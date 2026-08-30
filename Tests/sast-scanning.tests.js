"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const repo = path.resolve(__dirname, "..");
const scan = fs.readFileSync(path.join(repo, "scripts", "sast-scan.sh"), "utf8");
const evaluator = fs.readFileSync(path.join(repo, "scripts", "evaluate-semgrep.py"), "utf8");
const ci = fs.readFileSync(path.join(repo, "scripts", "ci-validate.sh"), "utf8");
const trivy = fs.readFileSync(path.join(repo, "scripts", "security-scan.sh"), "utf8");
const deploy = fs.readFileSync(path.join(repo, ".github", "workflows", "deploy-curiosity.yaml"), "utf8");
const workflow = fs.readFileSync(path.join(repo, ".github", "workflows", "ci.yaml"), "utf8");
const modules = fs.readFileSync(path.join(repo, ".gitmodules"), "utf8");
const dockerignore = fs.readFileSync(path.join(repo, ".dockerignore"), "utf8");
const project = fs.readFileSync(path.join(repo, "JobSearchManager.csproj"), "utf8");
const xpathRule = fs.readFileSync(
  path.join(repo, "security", "semgrep-jsm-rules", "csharp-xpath-injection.yaml"), "utf8");

const image = "semgrep/semgrep:1.175.0@sha256:1623685c0f6388b0bc8d577a712bf92b88252aaa09d6d7e38943dafa10ed978c";
const rulesCommit = "40b8c63f75dc7c22c8a77482d73bfb864b146f7e";
assert.ok(scan.includes(image), "Semgrep must be pinned by version and linux/amd64 digest.");
assert.ok(scan.includes(rulesCommit), "Community rules must be pinned to an immutable commit.");
assert.match(modules, /url = https:\/\/github\.com\/semgrep\/semgrep-rules\.git/);
assert.match(scan, /--network none/);
assert.match(scan, /--read-only/);
assert.match(scan, /--cap-drop ALL/);
assert.match(scan, /--security-opt no-new-privileges:true/);
assert.match(scan, /--oss-only/);
assert.match(scan, /--metrics off/);
assert.match(scan, /--strict/);
assert.match(scan, /--config \/repo\/security\/semgrep-jsm-rules/);
assert.match(scan, /--exclude-rule security\.semgrep-rules\.csharp\.dotnet\.security\.audit\.xpath-injection/);
assert.match(scan, /--include '\*\.cs'/);
for (const excluded of ["bin", "obj", "Tests", "security/semgrep-rules"]) {
  assert.ok(scan.includes(`--exclude ${excluded}`), `SAST must exclude ${excluded}.`);
}
assert.match(dockerignore, /^security\/semgrep-rules$/m,
  "The rules submodule must not enter the application image context.");
assert.match(project, /<Compile Remove="security\\semgrep-rules\\\*\*\\\*\.cs" \/>/,
  "The rules repository fixtures must not be compiled into the application.");
assert.ok(!scan.includes("/var/run/docker.sock"));
assert.ok(!scan.includes("SEMGREP_APP_TOKEN"));
assert.ok(!workflow.includes("security-events: write"));
assert.match(workflow, /permissions:\s*\r?\n\s+contents: read/);

const trivySource = ci.indexOf("security-scan.sh source");
const submoduleInit = ci.indexOf("git submodule update");
const sastSource = ci.indexOf("sast-scan.sh source");
const imageBuild = ci.indexOf("docker build");
assert.ok(trivySource >= 0 && submoduleInit > trivySource,
  "Trivy must scan application source before third-party rules are initialized.");
assert.ok(sastSource > submoduleInit && sastSource < imageBuild,
  "SAST must analyze the exact checkout before the image build.");
assert.ok(ci.includes("sast-scan.sh policy-test"));
assert.ok(trivy.includes("security-scan.sh") || ci.includes("security-scan.sh source"));
assert.match(deploy, /RUN_CONCLUSION/);
assert.match(deploy, /\$RUN_CONCLUSION.*success/);

assert.match(scan, /SafeFixture\.cs/);
assert.match(scan, /UnsafeFixture\.cs/);
assert.match(scan, /expect_failure 42 "the unsafe C# fixture"/);
assert.match(scan, /expect_nonzero "an invalid rule configuration"/);
assert.match(evaluator, /confidence == "HIGH"/);
assert.match(evaluator, /confidence == "MEDIUM" and severity == "ERROR"/);
assert.match(evaluator, /return 42 if blocking else 0/);
assert.match(evaluator, /fixpoint_timeouts/);
assert.match(evaluator, /skipped_rules/);
assert.match(xpathRule, /id: jsm-csharp-xpath-injection/);
assert.match(xpathRule, /confidence: MEDIUM/);

for (const protectedPath of [
  "/home/codex/jsm-lab/data/app",
  "/home/codex/jsm-lab/data/dataprotection",
  "mailpit",
  "ai801"
]) {
  assert.ok(!scan.includes(protectedPath), `SAST must not access ${protectedPath}.`);
}

console.log("Semgrep SAST integration tests passed.");
