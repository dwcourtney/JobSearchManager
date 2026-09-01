"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), "utf8");
const production = read("deploy", "compose.curiosity.yaml");
const deployment = read("scripts", "deploy-curiosity.sh");
const program = read("Program.cs");
const classifier = read("classifier-service", "classifier_service.py");
const classifierDockerfile = read("classifier-service", "Dockerfile");
const ollamaRuntime = read("ollama-runtime", "Dockerfile");
const client = read("ClassifierClient.cs");
const catalogService = read("JobCatalog.cs");
const models = read("JobModels.cs");
const taxonomy = JSON.parse(read("JobConceptCatalog.json"));

const adapter = production.match(/^  job-classifier:\n[\s\S]*?(?=^  ollama:)/m)?.[0];
const ollama = production.match(/^  ollama:\n[\s\S]*?(?=^networks:)/m)?.[0];
assert.ok(adapter && ollama, "production Compose must define adapter and Ollama services");
for (const block of [adapter, ollama]) {
  assert.doesNotMatch(block, /^\s*ports:/m, "classifier components must not publish a host/LAN port");
  assert.doesNotMatch(block, /docker\.sock|\/app\/data|dataprotection|mailpit|ai801/i,
    "classifier components must not mount or reference production data, Docker, Mailpit, or ai801");
  assert.match(block, /read_only: true/);
  assert.match(block, /no-new-privileges:true/);
  assert.match(block, /cap_drop:\s*\n\s*- ALL/);
}
assert.match(adapter, /gpus: all/, "the default DeBERTa classifier must have GPU access");
assert.match(adapter, /models\/nli-deberta-v3-base:\/models\/nli-deberta-v3-base:ro/);
assert.match(ollama, /gpus: all/);
assert.match(ollama, /models\/ollama:\/models/);
assert.match(production, /classifier:\s*\n\s*internal: true/, "classifier network must be private");

assert.equal(taxonomy.concepts.length, 85, "the canonical Job Fit taxonomy must retain 85 concepts");
assert.equal(new Set(taxonomy.concepts.map(item => item.id)).size, 85,
  "canonical concept IDs must remain unique");
assert.ok(taxonomy.concepts.every(item => item.definition?.trim()),
  "every semantic concept must have a canonical definition");
assert.match(classifier, /MODEL_ID = "cross-encoder\/nli-deberta-v3-base"/);
assert.match(classifier, /MODEL_REVISION = "6c749ce3425cd33b46d187e45b92bbf96ee12ec7"/);
assert.match(classifier, /MODEL_SHA256 = "d8148c6d49e0a7925134294c56326c71fe0ab1dc390e37355e00c7efbb488afa"/);
assert.match(classifier, /CONFIGURATION_VERSION = "deberta-85-nli-v1"/);
assert.match(classifier, /CHUNK_TOKENS, CHUNK_OVERLAP, MAX_LENGTH, CONCEPT_BATCH_SIZE, THRESHOLD = 384, 64, 512, 8, 0\.5/);
assert.match(classifier, /len\(CONCEPTS\) == len\(set\(CONCEPT_IDS\)\) == 85/);
assert.match(classifier, /"predictions": predictions/);
assert.match(classifier, /classificationFingerprint/);
assert.match(classifier, /taxonomyFingerprint/);
assert.match(classifier, /postingContentHash/);
assert.match(classifier, /qwen_deep_analysis/);
assert.match(classifier, /QWEN_PROMPT_VERSION = "job-fit-85-deep-analysis-v1"/);
assert.match(classifier, /QWEN_OUTPUT_SCHEMA/);
assert.match(classifier, /qwen_classification_fingerprint/);
assert.match(program, /\/api\/jobs\/deep-analysis/);
assert.match(models, /QwenDeepAnalysis\? QwenDeepAnalysis/);
assert.match(models, /QwenDeepAnalysis\([\s\S]*?TaxonomyFingerprint[\s\S]*?PromptHash[\s\S]*?Predictions/,
  "opt-in LLM predictions and their provenance must persist separately");
assert.match(classifierDockerfile, /COPY JobConceptCatalog\.json/,
  "the classifier image must receive the canonical taxonomy artifact");

assert.match(client, /PostingContentHash/);
assert.match(client, /ClassificationFingerprint/);
assert.match(client, /TaxonomyFingerprint/);
assert.match(client, /_inferenceGate = new\(1, 1\)/, "semantic inference must be bounded");
assert.match(catalogService, /Task\.Run\(RunSemanticClassificationAsync\)/,
  "ingestion must schedule semantic classification asynchronously");
assert.match(catalogService, /ThenByDescending\(job => job\.StartDate/,
  "newer postings must have classification priority");
assert.match(catalogService, /SemanticClassificationStates\.Unavailable/,
  "classifier failure must be represented without failing ingestion");
assert.match(program, /\/api\/admin\/classifier\/backfill/,
  "an explicit existing-cache backfill mechanism must remain available");
assert.match(program, /Timeout = TimeSpan\.FromSeconds\(120\)/,
  "the exact-SHA deployment probe must allow one bounded 85-concept GPU inference");
assert.match(deployment, /if \[\[ "\$allow_legacy" != "true" \]\]; then[\s\S]*?--classifier-diagnostic/,
  "rollback verification must not apply the new semantic contract to a legacy image");
assert.match(models, /SemanticJobClassification\? SemanticClassification/,
  "semantic results and provenance must persist with cached jobs");

assert.match(ollamaRuntime, /f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a/,
  "the Ollama 0.33.2 source commit must be immutable");
assert.match(ollamaRuntime, /USER 65532:65532/,
  "the patched runtime image must be non-root even outside Compose");
assert.match(ollamaRuntime, /HEALTHCHECK[\s\S]*?ollama[\s\S]*?list/);

console.log("85-concept DeBERTa default and opt-in Qwen architecture tests: PASS");
