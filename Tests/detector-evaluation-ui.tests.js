"use strict";

const assert = require("node:assert/strict");
const DetectorEvaluationUi = require("../wwwroot/detector-evaluation-ui.js");

const metrics = [
  { concept: "Artificial Intelligence", conceptId: "technical.artificial-intelligence", category: "Technical Domain", tier: "Tier 1 — Target Technical", evaluated: true, falsePositive: 1, falseNegative: 0 },
  { concept: "Deployment", conceptId: "work.deployment", category: "Work Arrangement", tier: "Tier 2 — Strong Negative", evaluated: true, falsePositive: 0, falseNegative: 0 },
  { concept: "Remote Work", conceptId: "work.remote", category: "Work Arrangement", tier: "Tier 3 — Other", evaluated: false, falsePositive: 0, falseNegative: 0 }
];

assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "all", "").map(item => item.concept),
  ["Artificial Intelligence", "Deployment", "Remote Work"]);
assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "tier1", "").map(item => item.concept),
  ["Artificial Intelligence"]);
assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "tier2", "").map(item => item.concept),
  ["Deployment"]);
assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "tier3", "").map(item => item.concept),
  ["Remote Work"]);
assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "evaluated", "").map(item => item.concept),
  ["Artificial Intelligence", "Deployment"]);
assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "not-evaluated", "").map(item => item.concept),
  ["Remote Work"]);
assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "errors", "").map(item => item.concept),
  ["Artificial Intelligence"]);
assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "all", "artificial").map(item => item.concept),
  ["Artificial Intelligence"]);
assert.deepEqual(DetectorEvaluationUi.filterMetrics(metrics, "tier1", "work"), [],
  "Search and tier filters must combine rather than overwrite each other.");

console.log("Detector Evaluation search, tier, maturity, and error filters passed.");
