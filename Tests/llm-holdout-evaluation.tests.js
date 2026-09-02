"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), "utf8");
const source = read("LlmHoldoutEvaluation.cs");
const hardware = read("LlmHardwareBenchmark.cs");
const program = read("Program.cs");
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
assert.match(program, /MapPost\("\/api\/admin\/evaluations\/llm-holdout"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)[\s\S]*?RequireRateLimiting\("state"\)/);
assert.match(app, /Evaluation classifiers[\s\S]*?RegEx[\s\S]*?LLM/);
assert.match(app, /Run LLM Holdout Evaluation/);
assert.match(app, /LLM evaluation hardware[\s\S]*?GTX 1070[\s\S]*?RTX 5080/);
assert.match(app, /Semantic agreement[\s\S]*?runtime reduction/);
assert.match(program, /rtx5080Status = evaluation\.GetRtx5080Status\(\)/);
assert.match(program, /--llm-benchmark[\s\S]*?requireStablePredictions: false/);
assert.match(read("LlmTechnicalPreflight.cs"), /passed-with-observed-semantic-variation/);
assert.match(read("scripts", "monitor-llm-hardware-benchmark.py"),
  /sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0/);
assert.match(app, /RegEx P[\s\S]*?LLM P[\s\S]*?F1 Δ/);
for (const marker of [/total_duration/, /eval_count/, /size_vram/]) assert.match(adapter, marker);
assert.doesNotMatch(program, /MapPost\("\/api\/jobs\/deep-analysis/,
  "Normal Jobs must not expose an LLM deep-analysis endpoint.");
console.log("Prediction-blinded LLM holdout architecture tests: PASS");
