"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), "utf8");
const source = read("LlmHoldoutEvaluation.cs");
const hardware = read("LlmHardwareBenchmark.cs");
const program = read("research", "qwen", "Program.cs");
const app = read("wwwroot", "app.js");
const adapter = read("classifier-service", "classifier_service.py");

const freeze = source.indexOf("WriteAtomicallyAsync(frozenPath, frozen");
const referenceRead = source.indexOf("ReadRequiredAsync<AiReferenceDataset>(ReferenceName", freeze);
assert.ok(freeze > 0 && referenceRead > freeze,
  "Frozen references must not be opened until the complete LLM prediction set is frozen.");
assert.match(source, /PredictionDatasetFingerprint != PredictionFingerprint\(value\)/);
assert.match(source, /item\.LabelProvenance\.Contains\("qwen"/);
assert.match(source, /SaveLlmEvaluationAsync/);
assert.match(source, /First valid prediction per posting is retained/);
assert.match(source, /DateTimeOffset\? StartedUtc/,
  "Durable LLM status must expose a run start time so elapsed progress survives browser refresh.");
assert.doesNotMatch(source, /RegexSemanticClassifier|LegacyJobConceptRules|_classifier\.Classify/,
  "The LLM prediction runner must not load or inspect RegEx implementation details.");
assert.ok(program.indexOf('args[0] == "--llm-benchmark"') <
  program.indexOf('args[0] == "--regex-maintenance"'),
  "The hardware prediction entry point must run before RegEx database initialization.");
assert.doesNotMatch(hardware, /SqliteSemanticRuleStore|RegexSemanticClassifier|LegacyJobConceptRules/,
  "The hardware benchmark runner must not depend on the RegEx store or classifier.");
assert.match(hardware, /Scoring artifacts must not be present on the prediction-blinded benchmark node/);
for (const forbidden of [/Contains\("reference"/, /Contains\("regex"/, /Contains\("gtx"/])
  assert.match(hardware, forbidden);
assert.match(hardware, /ExpectedHoldoutFileSha256[\s\S]*?5be7fa382048eee4cd104d901c33367b522c5a4fb1a9962f63521c069bcde88b/);
assert.match(hardware, /First valid prediction per posting is retained; no retry, tuning, or alternate prompt is allowed/);
assert.ok(hardware.indexOf("WriteAtomicallyAsync(frozenPath, frozen") <
  hardware.indexOf("public static async Task<LlmHardwareComparisonReport> ScoreAsync"),
  "RTX predictions must be frozen in the prediction path before the separate scorer can open references.");
assert.match(program, /--llm-benchmark[\s\S]*?requireStablePredictions: false/);
assert.match(read("LlmTechnicalPreflight.cs"), /passed-with-observed-semantic-variation/);
assert.match(read("scripts", "monitor-llm-hardware-benchmark.py"),
  /sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0/);
for (const marker of [/total_duration/, /eval_count/, /size_vram/]) assert.match(adapter, marker);
assert.doesNotMatch(program, /MapPost\("\/api\/jobs\/deep-analysis/,
  "Normal Jobs must not expose an LLM deep-analysis endpoint.");
console.log("Prediction-blinded LLM holdout architecture tests: PASS");

assert.doesNotMatch(app, /renderLlmEvaluationCard|Run LLM Holdout Evaluation/);

const repo = root;
const benchmarkDockerfile=read("Dockerfile.hardware-benchmark");
const benchmarkCompose=read("research","qwen","compose.yaml");
assert.match(benchmarkDockerfile,/Jsm.Qwen.Research.csproj/);
assert.match(benchmarkDockerfile,/rm -f[\s\S]*?LegacyJobConceptRules\.json[\s\S]*?RegexValidationCorpus\.json/);
assert.doesNotMatch(benchmarkCompose,/ports:|network_mode:\s*host|mailpit|jsm-lab\/data/);
assert.match(benchmarkCompose,/internal:\s*true/);
const deepAnalysisDockerfile = fs.readFileSync(path.join(repo, "classifier-service", "Dockerfile"), "utf8");
assert.match(deepAnalysisDockerfile, /FROM python:3\.12\.12-alpine3\.23@sha256:[a-f0-9]{64}/,
  "Deep-analysis base must remain pinned to an immutable digest.");
assert.match(deepAnalysisDockerfile, /apk add --no-cache --upgrade[\s\S]*?libuuid=2\.41\.6-r1(?:\s|$)/,
  "Python's inherited libuuid runtime dependency must receive the pinned Alpine security fix.");
