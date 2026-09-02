"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), "utf8");
const source = read("LlmHoldoutEvaluation.cs");
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
assert.doesNotMatch(source, /RegexSemanticClassifier|LegacyJobConceptRules|_classifier\.Classify/,
  "The LLM prediction runner must not load or inspect RegEx implementation details.");
assert.match(program, /MapPost\("\/api\/admin\/evaluations\/llm-holdout"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)[\s\S]*?RequireRateLimiting\("state"\)/);
assert.match(app, /Evaluation classifiers[\s\S]*?RegEx[\s\S]*?LLM/);
assert.match(app, /Run LLM Holdout Evaluation/);
assert.match(app, /RegEx P[\s\S]*?LLM P[\s\S]*?F1 Δ/);
for (const marker of [/total_duration/, /eval_count/, /size_vram/]) assert.match(adapter, marker);
assert.doesNotMatch(program, /MapPost\("\/api\/jobs\/deep-analysis/,
  "Normal Jobs must not expose an LLM deep-analysis endpoint.");
console.log("Prediction-blinded LLM holdout architecture tests: PASS");
