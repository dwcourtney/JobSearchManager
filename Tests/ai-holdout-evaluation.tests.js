"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const source = fs.readFileSync(path.join(root, "AiHoldoutEvaluation.cs"), "utf8");
const program = fs.readFileSync(path.join(root, "Program.cs"), "utf8");
const docs = fs.readFileSync(path.join(root, "docs", "regex-evaluation-methodology.md"), "utf8");

assert.match(source, /LabelingPassA[\s\S]*LabelingPassB[\s\S]*ComparingLabels[\s\S]*Adjudicating[\s\S]*Freezing[\s\S]*Scoring[\s\S]*Calculating[\s\S]*Complete/);
const freeze = source.indexOf("await WriteAtomicallyAsync(referencePath, references, token)");
const scoring = source.indexOf("_classifier.Classify(example.Title", freeze);
assert.ok(freeze > 0 && scoring > freeze,
  "RegEx must not run until the AI reference file is frozen.");
assert.match(source, /detector output is computed or revealed/);
assert.match(source, /productionUsage: false/);
assert.match(source, /Reference judgments changed after freeze[\s\S]*existing frozen references were not modified/);
assert.match(source, /DetectorOutputExposedDuringLabeling[\s\S]*UsedForRuleDevelopment[\s\S]*ContaminatedUtc[\s\S]*PredictionScore/);
assert.match(source, /LabelerAJudgment[\s\S]*LabelerBJudgment[\s\S]*AdjudicatedJudgment[\s\S]*FinalReferenceJudgment/);
assert.match(source, /ValidatePromptAsync[\s\S]*PromptFingerprint/);
assert.match(source, /UnresolvedExcludedDecisions/);
assert.match(source, /binary RegEx output yields one precision\/recall operating point/);
assert.doesNotMatch(source, /Qwen|DeepAnalysis|ClassifierClient/,
  "The reference-label pipeline must not depend on the dormant LLM stack.");
assert.match(source, /AI-ADJUDICATED PRODUCTION HOLDOUT/);
assert.match(source, /They are not human-ground-truth labels/);
assert.match(program, /AddSingleton<AiHoldoutEvaluationService>/);
assert.match(program, /api\/admin\/evaluations\/ai-holdout\/status/);
assert.match(docs, /share model-family biases/i);
assert.match(docs, /agreement measures consistency rather than truth/i);
assert.match(docs, /holdout failure must not immediately become a new rule/i);

console.log("AI-adjudicated holdout architecture and scientific-boundary tests passed.");
