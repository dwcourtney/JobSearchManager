"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), "utf8");
const production = read("deploy", "compose.curiosity.yaml");
const deployment = read("scripts", "deploy-curiosity.sh");
const program = read("Program.cs");
const adapter = read("classifier-service", "classifier_service.py");
const adapterDockerfile = read("classifier-service", "Dockerfile");
const ollamaRuntime = read("ollama-runtime", "Dockerfile");
const client = read("ClassifierClient.cs");
const catalogService = read("JobCatalog.cs");
const models = read("JobModels.cs");
const store = read("SqliteSemanticRuleStore.cs");
const regex = read("RegexSemanticClassifier.cs");
const taxonomy = JSON.parse(read("JobConceptCatalog.json"));

const deepAnalysis = production.match(/^  deep-analysis:\n[\s\S]*?(?=^  ollama:)/m)?.[0];
const ollama = production.match(/^  ollama:\n[\s\S]*?(?=^networks:)/m)?.[0];
assert.ok(deepAnalysis && ollama, "production Compose must define opt-in deep analysis and Ollama");
for (const block of [deepAnalysis, ollama]) {
  assert.doesNotMatch(block, /^\s*ports:/m, "LLM components must not publish a host/LAN port");
  assert.doesNotMatch(block, /docker\.sock|\/app\/data|dataprotection|mailpit|ai801/i);
  assert.match(block, /read_only: true/);
  assert.match(block, /no-new-privileges:true/);
  assert.match(block, /cap_drop:\s*\n\s*- ALL/);
}
assert.doesNotMatch(deepAnalysis, /gpus: all|nli-deberta|model.*volume/i);
assert.match(deepAnalysis, /cpus: 1\.0/);
assert.match(deepAnalysis, /mem_limit: 256m/);
assert.match(ollama, /gpus: all/);
assert.match(ollama, /models\/ollama:\/models/);
assert.match(production, /classifier:\s*\n\s*internal: true/);
assert.match(production, /DeepAnalysis__BaseUrl: http:\/\/deep-analysis:8081\//);

assert.equal(taxonomy.concepts.length, 85);
assert.equal(new Set(taxonomy.concepts.map(item => item.id)).size, 85);
assert.ok(taxonomy.concepts.every(item => item.definition?.trim()));
assert.match(store, /Microsoft\.Data\.Sqlite/);
assert.match(store, /CREATE TABLE IF NOT EXISTS SemanticRules/);
assert.match(store, /CREATE TABLE IF NOT EXISTS RuleRelationships/);
assert.match(store, /CREATE TABLE IF NOT EXISTS EvaluationRuns/);
assert.match(regex, /Interlocked\.Exchange\(ref _current/);
assert.match(regex, /NonBacktracking/);
assert.match(regex, /_pendingUsage/);
assert.match(client, /"deterministic-regex"/);
assert.match(client, /"sqlite-regex-v1"/);
assert.doesNotMatch(client, /\/classify|DeBERTa|deberta/i);

assert.match(adapter, /purpose": "opt-in-llm-deep-analysis"/);
assert.match(adapter, /SERVICE_VERSION, PROTOCOL_VERSION = "3\.0\.0", "7"/);
assert.match(adapter, /QWEN_PROMPT_VERSION = "job-fit-85-deep-analysis-v1"/);
assert.match(adapter, /QWEN_OUTPUT_SCHEMA/);
assert.match(adapter, /qwen_classification_fingerprint/);
assert.doesNotMatch(adapter, /torch|transformers|deberta|\/classify/i);
assert.match(adapterDockerfile, /python:3\.12\.12-alpine3\.23@sha256:/);
assert.match(adapterDockerfile, /COPY JobConceptCatalog\.json/);

assert.doesNotMatch(program, /MapPost\("\/api\/jobs\/deep-analysis/,
  "Normal production HTTP routes must not expose manual LLM execution.");
assert.match(program, /\/api\/admin\/regex-rules/);
assert.match(models, /LlmDeepAnalysisRequestState/);
assert.match(models, /Queued[\s\S]*?Running[\s\S]*?Completed[\s\S]*?Failed/);
assert.match(models, /QwenDeepAnalysis\? QwenDeepAnalysis/);
assert.match(client, /_deepAnalysisInFlight/);
assert.match(catalogService, /PersistDeepAnalysisStateAsync/);
assert.match(catalogService, /Task\.Run\(RunSemanticClassificationAsync\)/);
assert.match(deployment, /jsm-deep-analysis:\$target_sha/);
assert.doesNotMatch(deployment, /--download-model|--classifier-diagnostic|--model-diagnostic/);

assert.match(ollamaRuntime, /f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a/);
assert.match(ollamaRuntime, /USER 65532:65532/);
assert.match(ollamaRuntime, /HEALTHCHECK[\s\S]*?ollama[\s\S]*?list/);

console.log("SQLite RegEx authority with dormant Qwen infrastructure tests: PASS");
