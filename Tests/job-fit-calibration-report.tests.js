"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const directory = fs.mkdtempSync(path.join(os.tmpdir(), "jsm-job-fit-calibration-"));
try {
  const cachePath = path.join(directory, "cache.json");
  const settingsPath = path.join(directory, "settings.json");
  const detectionsPath = path.join(directory, "detections.json");
  const reportPath = path.join(directory, "report.json");
  fs.writeFileSync(cachePath, JSON.stringify({
    schemaVersion: 6,
    query: { companyId: "leidos" },
    jobs: [{
      requisitionId: "REQ-CALIBRATION",
      title: "Systems Engineer",
      descriptionHtml: "<p>Serve as the primary interface with the customer.</p>"
    }]
  }));
  fs.writeFileSync(settingsPath, JSON.stringify({
    jobFit: { enabled: true, signals: [{ conceptId: "role.systems-engineering", preference: "positive" }] }
  }));
  fs.writeFileSync(detectionsPath, JSON.stringify({
    jobConceptCatalogVersion: 4,
    jobs: [{
      requisitionId: "REQ-CALIBRATION",
      detectedConcepts: [{ conceptId: "role.systems-engineering", evidence: "Systems Engineer" }]
    }]
  }));
  const result = spawnSync(process.execPath, [
    path.join(__dirname, "..", "scripts", "job-fit-calibration-report.mjs"),
    cachePath, settingsPath, reportPath, detectionsPath
  ], { encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr || result.stdout);
  const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));
  assert.equal(report.input.companyId, "leidos");
  assert.equal(report.input.jobCount, 1);
  assert.equal(report.input.jobConceptCatalogVersion, 4);
  assert.equal(report.jobs[0].score, 6);
  assert.equal(report.jobs[0].detectedConcepts[0].configuredPreference, "positive");
  assert.equal(report.jobs[0].auditMisses[0].conceptId, "responsibility.customer-facing");
  assert.equal(report.scoreDistribution[6], 1);
} finally {
  fs.rmSync(directory, { recursive: true, force: true });
}

console.log("Job Fit calibration report test passed.");
