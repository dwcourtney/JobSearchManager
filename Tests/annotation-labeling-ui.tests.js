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

console.log("Annotation labeling UI tests passed.");
