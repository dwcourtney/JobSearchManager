"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const repo = path.resolve(__dirname, "..");
const workflow = fs.readFileSync(
  path.join(repo, ".github", "workflows", "ci.yaml"), "utf8");
const deploy = fs.readFileSync(
  path.join(repo, ".github", "workflows", "deploy-curiosity.yaml"), "utf8");
const ci = fs.readFileSync(path.join(repo, "scripts", "ci-validate.sh"), "utf8");
const trivy = fs.readFileSync(path.join(repo, "scripts", "security-scan.sh"), "utf8");
const curiosityDeploy = fs.readFileSync(path.join(repo, "scripts", "deploy-curiosity.sh"), "utf8");
const dockerignore = fs.readFileSync(path.join(repo, ".dockerignore"), "utf8");
const project = fs.readFileSync(path.join(repo, "JobSearchManager.csproj"), "utf8");
const securityDocs = fs.readFileSync(path.join(repo, "docs", "curiosity-cicd.md"), "utf8");

const checkoutPin = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1";
const dotnetPin = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68";
const codeqlPin = "github/codeql-action/";
const codeqlSha = "cdf488f595d80d6e07e03d4674febd5ab45fa938";

assert.ok(workflow.includes(checkoutPin), "Checkout must use the approved immutable v7.0.1 pin.");
assert.ok(workflow.includes(dotnetPin), "setup-dotnet must use the approved immutable v6.0.0 pin.");
for (const action of ["init", "analyze"]) {
  const uses = `${codeqlPin}${action}@${codeqlSha}`;
  assert.ok(workflow.includes(uses), `${action} must use the immutable CodeQL v4.37.9 pin.`);
  assert.ok(!workflow.includes(`${codeqlPin}${action}@v4`), `${action} must not use a floating tag.`);
}

assert.match(workflow, /name: Initialize C# CodeQL[\s\S]*languages: csharp[\s\S]*build-mode: manual[\s\S]*queries: security-extended/);
assert.match(workflow, /name: Initialize JavaScript CodeQL[\s\S]*languages: javascript-typescript[\s\S]*build-mode: none[\s\S]*queries: security-extended/);
assert.match(workflow, /permissions: \{\}/, "Workflow permissions must default to none.");
assert.equal((workflow.match(/security-events: write/g) || []).length, 2,
  "Only the two CodeQL jobs may upload code-scanning results.");
assert.equal((workflow.match(/contents: read/g) || []).length, 2,
  "Only the two checkout-and-analysis jobs need repository contents.");

const csharpInit = workflow.indexOf("name: Initialize C# CodeQL");
const validation = workflow.indexOf("name: Validate source, tests, image, health, and version identity");
const csharpAnalyze = workflow.indexOf("name: Analyze C# with CodeQL");
assert.ok(csharpInit >= 0 && validation > csharpInit && csharpAnalyze > validation,
  "C# CodeQL must observe the existing deterministic validation build before analysis.");

assert.match(workflow, /name: Validate exact commit[\s\S]*if: \$\{\{ always\(\) \}\}[\s\S]*- validate[\s\S]*- javascript-codeql/);
assert.match(workflow, /CSHARP_RESULT:[\s\S]*JAVASCRIPT_RESULT:[\s\S]*'success'/,
  "The protected exact-commit check must fail unless both analysis jobs succeed.");
assert.match(deploy, /workflows: \[CI\]/);
assert.match(deploy, /RUN_CONCLUSION/);
assert.match(deploy, /\$RUN_CONCLUSION.*success/,
  "Automatic deployment must still require the complete CI workflow to succeed.");

assert.match(ci, /security-scan\.sh source/);
assert.match(ci, /security-scan\.sh policy-test/);
assert.ok(trivy.includes("ghcr.io/aquasecurity/trivy:"), "Trivy must remain pinned and active.");
assert.match(curiosityDeploy, /security-scan\.sh" image/,
  "Curiosity must still scan the exact deployable image with Trivy.");

const retiredScanner = ["sem", "grep"].join("");
for (const retiredPath of [
  path.join(repo, ".gitmodules"),
  path.join(repo, "scripts", `${retiredScanner === "" ? "" : "sast-scan"}.sh`),
  path.join(repo, "scripts", `evaluate-${retiredScanner}.py`),
  path.join(repo, "security", `${retiredScanner}-rules`),
  path.join(repo, "security", `${retiredScanner}-jsm-rules`)
]) {
  assert.ok(!fs.existsSync(retiredPath), `Retired SAST artifact remains: ${retiredPath}`);
}
for (const [name, source] of Object.entries({ ci, dockerignore, project, securityDocs })) {
  assert.ok(!source.toLowerCase().includes(retiredScanner),
    `${name} retains an active dependency on the retired scanner.`);
}

console.log("CodeQL integration tests passed.");
