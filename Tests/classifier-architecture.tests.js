"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const production = fs.readFileSync(path.join(root, "deploy", "compose.curiosity.yaml"), "utf8");
const program = fs.readFileSync(path.join(root, "Program.cs"), "utf8");
const scoring = fs.readFileSync(path.join(root, "wwwroot", "job-fit.js"), "utf8");
const classifier = fs.readFileSync(path.join(root, "classifier-service", "classifier_service.py"), "utf8");
const client = fs.readFileSync(path.join(root, "ClassifierClient.cs"), "utf8");
const evaluation = fs.readFileSync(path.join(root, "EmbeddingEvaluation.cs"), "utf8");
const benchmark = fs.readFileSync(path.join(root, "scripts", "benchmark-embedding.py"), "utf8");
const phase2 = fs.readFileSync(path.join(root, "docs", "classifier-phase-2.md"), "utf8");
const phase2b = fs.readFileSync(path.join(root, "docs", "classifier-phase-2b.md"), "utf8");

const classifierBlock = production.split("  job-classifier:")[1].split("\nnetworks:")[0];
assert.ok(classifierBlock, "production Compose must define job-classifier");
assert.doesNotMatch(classifierBlock, /^\s*ports:/m, "classifier must not publish a host/LAN port");
assert.doesNotMatch(classifierBlock, /docker\.sock|\/app\/data|dataprotection|mailpit|ai801/i,
  "classifier must not mount or reference production data, Docker, Mailpit, or ai801");
assert.match(classifierBlock, /read_only: true/);
assert.match(classifierBlock, /no-new-privileges:true/);
assert.match(classifierBlock, /cap_drop:\s*\n\s*- ALL/);
assert.match(classifierBlock, /gpus: all/);
assert.match(production, /classifier:\s*\n\s*internal: true/,
  "classifier network must be private");

assert.match(program,
  /MapPost\("\/api\/admin\/classifier-diagnostic"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/,
  "diagnostic endpoint must enforce the server-side admin policy");
assert.match(program,
  /MapPost\("\/api\/admin\/detector-evaluation\/embedding"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/,
  "embedding evaluation must preserve the server-side admin policy");
assert.doesNotMatch(scoring, /classifier/i,
  "normal Job Fit scoring must remain independent of the experimental classifier");

assert.match(classifier, /MODEL_ID = "BAAI\/bge-base-en-v1\.5"/);
assert.match(classifier, /MODEL_REVISION = "a5beb1e3e68b9ab74eb54cfd186867f64f240e1a"/);
assert.match(classifier, /EMBEDDING_DIMENSION, DEFAULT_THRESHOLD = 768, \.50/);
assert.match(classifier, /AutoModel\.from_pretrained/);
assert.doesNotMatch(classifier, /AutoModelForSequenceClassification|softmax|entailment/i);
assert.match(classifier, /last_hidden_state\[:, 0\]/, "BGE must use documented CLS pooling");
assert.match(classifier, /functional\.normalize\(vectors, p=2, dim=1\)/,
  "embedding vectors must be L2 normalized");
assert.match(classifier, /torch\.allclose\(norms, torch\.ones_like\(norms\), atol=1e-5\)/,
  "concept-vector normalization must be verified at runtime");
assert.match(classifier, /posting_embeddings @ self\.concept_embeddings\.T/,
  "normalized dot product must calculate cosine similarity");
assert.match(classifier, /aggregate_similarities\(rows\)/);
assert.match(classifier, /self\.concept_embeddings = self\.embed/,
  "concept embeddings must initialize once with the loaded runtime");
assert.match(classifier, /conceptEmbeddingMemoryBytes/);
assert.match(classifier, /"modelType": MODEL_TYPE/);
assert.match(classifier, /"similarity": similarity/);
assert.doesNotMatch(classifier, /probability/i, "cosine similarity must not be called probability");
assert.match(client, /classify-embedding/);
assert.match(client, /Similarity is >= -1 and <= 1/);
assert.match(evaluation, /ThresholdValues = \[\.50, \.55, \.60, \.65, \.70, \.75, \.80, \.85, \.90\]/);
assert.match(benchmark, /Expected 40 scoped fixtures/);
assert.match(benchmark, /"labelCount": len\(cases\) \* len\(CONCEPTS\)/);
assert.match(benchmark, /value\["macro"\]\["f1"\] is not None else -1\.0/,
  "threshold selection must tolerate undefined macro F1 at strict thresholds");
assert.match(benchmark, /"distilRoBERTa"/);
assert.match(benchmark, /"deBERTa"/);
assert.match(phase2, /cross-encoder\/nli-distilroberta-base/,
  "historical DistilRoBERTa evidence must remain");
assert.match(phase2b, /cross-encoder\/nli-deberta-v3-base/,
  "historical DeBERTa evidence must remain");
assert.match(classifierBlock, /bge-base-en-v1\.5/,
  "only the isolated embedding cache may be mounted into the classifier");

console.log("Embedding classifier isolation, contract, cache, history, and scoring architecture tests: PASS");
