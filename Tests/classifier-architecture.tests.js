"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), "utf8");
const production = read("deploy", "compose.curiosity.yaml");
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
assert.doesNotMatch(adapter, /gpus:|\/models/, "the adapter must have neither GPU nor model access");
assert.match(ollama, /gpus: all/);
assert.match(ollama, /models\/ollama:\/models/);
assert.match(production, /classifier:\s*\n\s*internal: true/, "classifier network must be private");

assert.equal(taxonomy.concepts.length, 85, "the canonical Job Fit taxonomy must retain 85 concepts");
assert.equal(new Set(taxonomy.concepts.map(item => item.id)).size, 85,
  "canonical concept IDs must remain unique");
assert.ok(taxonomy.concepts.every(item => item.definition?.trim()),
  "every semantic concept must have a canonical definition");
assert.match(classifier, /MODEL_TAG = "qwen3:4b-instruct-2507-q4_K_M"/);
assert.match(classifier, /MODEL_DIGEST = "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0"/);
assert.match(classifier, /PROMPT_VERSION = "job-fit-85-zero-shot-v1"/);
assert.match(classifier, /len\(CONCEPTS\) == len\(set\(CONCEPT_IDS\)\) == 85/);
assert.match(classifier, /"format": OUTPUT_SCHEMA/);
assert.match(classifier, /set\(value\) != set\(CONCEPT_IDS\)/,
  "output must have exactly the canonical 85 keys");
assert.match(classifier, /type\(value\[key\]\) is not bool/,
  "output values must be strict booleans");
assert.match(classifier, /classificationFingerprint/);
assert.match(classifier, /taxonomyFingerprint/);
assert.match(classifier, /postingContentHash/);
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
assert.match(models, /SemanticJobClassification\? SemanticClassification/,
  "semantic results and provenance must persist with cached jobs");

assert.match(ollamaRuntime, /f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a/,
  "the Ollama 0.33.2 source commit must be immutable");
assert.match(ollamaRuntime, /USER 65532:65532/,
  "the patched runtime image must be non-root even outside Compose");
assert.match(ollamaRuntime, /HEALTHCHECK[\s\S]*?ollama[\s\S]*?list/);

console.log("85-concept semantic classifier isolation, persistence, and scheduling architecture tests: PASS");
