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
  labeling.generationPayload("1000", false, { concept: "cloud.aws", company: "acme" }),
  { requestedItems: 1000, allEligible: false, company: "acme", concept: "cloud.aws" });
assert.match(labeling.formatImportSummary({ recordsRead: 4, imported: 2, unchanged: 1, conflicts: 1, rejected: 1 }),
  /4 read.*2 imported.*1 conflicts.*1 rejected/);

console.log("Annotation labeling UI tests passed.");
