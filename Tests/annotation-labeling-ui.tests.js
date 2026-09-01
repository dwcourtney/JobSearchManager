"use strict";

const assert = require("assert");
const labeling = require("../wwwroot/annotation-labeling-ui.js");

assert.strictEqual(labeling.shortcutFor({ key: "1", target: {} }), "correct");
assert.strictEqual(labeling.shortcutFor({ key: "S", target: {} }), "unsure");
assert.strictEqual(labeling.shortcutFor({ key: "f", target: {} }), "toggleFull");
assert.strictEqual(labeling.shortcutFor({ key: "1", ctrlKey: true, target: {} }), null);
assert.strictEqual(labeling.shortcutFor({ key: "1", target: { tagName: "INPUT" } }), null);
assert.strictEqual(labeling.validateDecision("differentLabel", ["cloud.aws"]), "");
assert.match(labeling.validateDecision("differentLabel", []), /exactly one/);
assert.strictEqual(labeling.validateDecision("multipleLabels", ["cloud.aws", "containers.docker"]), "");
assert.match(labeling.validateDecision("multipleLabels", ["cloud.aws"]), /at least two/);
assert.strictEqual(
  labeling.queueUrl({ status: "reviewed", concept: "cloud.aws", company: "acme" }),
  "/api/admin/annotations/queue?status=reviewed&concept=cloud.aws&company=acme");
assert.strictEqual(
  labeling.exportUrl("all", { concept: "cloud.aws", company: "acme" }),
  "/api/admin/annotations/export?mode=all&concept=cloud.aws&company=acme");
assert.deepStrictEqual(
  labeling.generationPayload("100", false, { concept: "cloud.aws", company: "acme" }),
  { requestedItems: 100, allEligible: false, company: "acme", concept: "cloud.aws" });
assert.deepStrictEqual(labeling.generationPayload("100", true, { concept: "", company: "" }),
  { requestedItems: null, allEligible: true, company: null, concept: null });
assert.strictEqual(labeling.generationStatusUrl({ concept: "cloud.aws", company: "acme" }),
  "/api/admin/annotations/generation-status?concept=cloud.aws&company=acme");
assert.strictEqual(labeling.formatGenerationResult({ added: 100, total: 1100, remainingEligible: 25 }),
  "Added 100 new items. Corpus total: 1,100.");
assert.strictEqual(labeling.formatGenerationResult({ added: 63, total: 1063, remainingEligible: 0 }),
  "Added 63 new items. Corpus total: 1,063. No additional eligible items remain.");
assert.strictEqual(labeling.formatGenerationResult({ added: 0, total: 1063, remainingEligible: 0 }),
  "No items were added because no eligible ungenerated items remain. Corpus total: 1,063.");
assert.strictEqual(labeling.formatImportSummary({ recordsRead: 4, imported: 2, unchanged: 1, conflicts: 1, rejected: 1 }),
  "Imported: 2 · Unchanged: 1 · Conflicts: 1 · Rejected: 1");

console.log("Annotation labeling UI tests passed.");
