"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const production = fs.readFileSync(path.join(root, "deploy", "compose.curiosity.yaml"), "utf8");
const program = fs.readFileSync(path.join(root, "Program.cs"), "utf8");
const scoring = fs.readFileSync(path.join(root, "wwwroot", "job-fit.js"), "utf8");
const classifier = fs.readFileSync(path.join(root, "classifier-service", "classifier_service.py"), "utf8");
const ollamaRuntime = fs.readFileSync(path.join(root, "ollama-runtime", "Dockerfile"), "utf8");
const client = fs.readFileSync(path.join(root, "ClassifierClient.cs"), "utf8");
const evaluation = fs.readFileSync(path.join(root, "LlmEvaluation.cs"), "utf8");
const phase2 = fs.readFileSync(path.join(root, "docs", "classifier-phase-2.md"), "utf8");
const phase2b = fs.readFileSync(path.join(root, "docs", "classifier-phase-2b.md"), "utf8");
const phase2c = fs.readFileSync(path.join(root, "docs", "classifier-phase-2c.md"), "utf8");

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
assert.match(program, /MapPost\("\/api\/admin\/classifier-diagnostic"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.match(program, /MapPost\("\/api\/admin\/detector-evaluation\/llm"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.match(program, /--detector-evaluation-diagnostic/,
  "the fixed regex report must be exportable without browser or production data access");
assert.doesNotMatch(scoring, /classifier/i, "normal Job Fit scoring must remain regex-only");

assert.match(classifier, /MODEL_TAG = "llama3\.1:8b-instruct-q4_K_M"/);
assert.match(classifier, /MODEL_DIGEST = "sha256:46e0c10c039e019119339687c3c1757cc81b9da49709a3b3924863ba87ca666e"/);
assert.match(classifier, /"format": OUTPUT_SCHEMA/);
assert.match(classifier, /"temperature": TEMPERATURE, "seed": SEED/);
assert.match(classifier, /CONTEXT_LENGTH, MAX_OUTPUT_TOKENS, SEED, TEMPERATURE = 8192, 384, 42, 0/);
assert.match(classifier, /set\(value\) != set\(CONCEPT_IDS\)/, "output must have exactly eight keys");
assert.match(classifier, /type\(value\[key\]\) is not bool/, "output values must be strict booleans");
assert.match(classifier, /model_digest_matches\(item\.get\("digest"\)\)/,
  "Ollama's unprefixed tags digest must be checked against the pinned sha256 identity");
assert.match(classifier, /actual candidate responsibilities/);
assert.match(classifier, /PROMPT_VERSION = "phase3-zero-shot-v1"/);
assert.match(classifier, /result = classify\("Backend API Engineer"[\s\S]*?\{\*\*identity\(\), \*\*result\}/,
  "the model diagnostic must report runtime state after loading the model");
assert.match(ollamaRuntime, /f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a/,
  "the Ollama 0.33.2 source commit must be immutable");
assert.match(ollamaRuntime, /golang:1\.26\.6-trixie@sha256:23fdfd3a/);
assert.match(ollamaRuntime, /github\.com\/buger\/jsonparser@v1\.1\.2/);
assert.match(ollamaRuntime, /golang\.org\/x\/crypto@v0\.55\.0/);
assert.match(ollamaRuntime, /golang\.org\/x\/image@v0\.45\.0/);
assert.match(ollamaRuntime, /golang\.org\/x\/net@v0\.57\.0/);
assert.match(ollamaRuntime, /golang\.org\/x\/text@v0\.41\.0/);
assert.match(ollamaRuntime, /-buildvcs=false/);
assert.match(ollamaRuntime, /USER 65532:65532/,
  "the patched runtime image must be non-root even outside Compose");
assert.match(ollamaRuntime, /HEALTHCHECK[\s\S]*?ollama[\s\S]*?list/);
assert.match(client, /classify-llm/);
assert.match(evaluation, /cases\.Count, cases\.Sum\(item => item\.Labels\.Count\)/);
assert.match(phase2, /cross-encoder\/nli-distilroberta-base/);
assert.match(phase2b, /cross-encoder\/nli-deberta-v3-base/);
assert.match(phase2c, /BAAI\/bge-base-en-v1\.5/);

console.log("Local LLM classifier isolation, schema, prompt, history, and scoring architecture tests: PASS");
