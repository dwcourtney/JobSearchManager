"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const crypto = require("node:crypto");
const root = path.resolve(__dirname, "..");
const app = fs.readFileSync(path.join(root, "wwwroot/app.js"), "utf8");
class Element {
  constructor(tag) { this.tag = tag; this.children = []; this.attrs = {}; this.handlers = {}; this.classList = { toggle() {} }; }
  setAttribute(k, v) { this.attrs[k] = v; }
  append(...nodes) { this.children.push(...nodes); }
  addEventListener(k, fn) { this.handlers[k] = fn; }
  after() {}
  remove() { this.removed = true; }
}
function extract(name) {
  const start = app.indexOf(`function ${name}(`);
  assert.ok(start >= 0);
  const end = app.indexOf("\n}\n", start);
  return app.slice(start, end + 3);
}
let cheapLoads = 0, jobFitLoads = 0;
const elements = { settingsTab: new Element("button"), settingsView: new Element("section") };
const state = { activeView: "jobs", activeEvaluationTab: "llm" };
const context = {
  document: { createElement: tag => new Element(tag) }, elements, state,
  window: { location: { hash: "", pathname: "/", search: "" } }, history: { replaceState() {} },
  RuleMaintenance: { renderStatus(panel) { assert.equal(panel, elements.adminCheapPanel); cheapLoads++; } },
  loadClassifierStatus() { jobFitLoads++; }, loadEvaluationLedger() {}, showView() {},
  renderCuratedEvaluationCard: () => "curated-job-fit", renderHoldoutEvaluationCard: () => "frozen-job-fit"
};
vm.createContext(context);
vm.runInContext(["synchronizeAdminNavigation", "showAdminSection", "renderEvaluationNavigation"].map(extract).join("\n"), context);
context.synchronizeAdminNavigation(true);
assert.equal(elements.adminClassifierTab.textContent, "Job Fit Rules");
assert.equal(elements.adminEvaluationTab.textContent, "Job Fit Evaluation");
assert.equal(elements.adminCheapTab.textContent, "Cheap Triage");
context.showAdminSection("cheap-triage", true);
assert.equal(cheapLoads, 1); assert.equal(jobFitLoads, 0);
assert.equal(elements.adminCheapPanel.hidden, false); assert.equal(elements.adminClassifierPanel.hidden, true);
assert.equal(elements.adminCheapTab.attrs["aria-selected"], "true");
context.showAdminSection("classifier"); assert.equal(jobFitLoads, 1); assert.equal(elements.adminCheapPanel.hidden, true);
const ledger = context.renderEvaluationNavigation({ llmHoldoutReport: {}, triageReport: {} });
assert.deepEqual(ledger.children, ["curated-job-fit", "frozen-job-fit"]);
const cheapTab = elements.adminCheapTab;
context.synchronizeAdminNavigation(false);
assert.equal(elements.adminCheapTab, null); assert.equal(elements.adminCheapPanel, null);
assert.equal(cheapTab.textContent, "Cheap Triage");
// Audit archive preservation without opening the blinded holdout or running inference.
const inventory = JSON.parse(fs.readFileSync(path.join(root, "docs/cheap-triage-research-inventory.json"), "utf8"));
const archived = inventory.files.filter(f => f.disposition === "commit");
for (const f of archived) assert.equal(crypto.createHash("sha256").update(fs.readFileSync(path.join(root, f.path))).digest("hex"), f.sha256, f.path);
assert.equal(archived.length, 241);
const program = fs.readFileSync(path.join(root, "Program.cs"), "utf8");
assert.match(program, /case "evaluate-triage"/);
assert.match(program, /--llm-benchmark/);
assert.doesNotMatch(program, /AddSingleton<(?:LlmHoldoutEvaluationService|TriageEvaluationService)>/);
for (const file of ["JobCatalog.cs", "ClassifierClient.cs", "JobAnalysis.cs", "RegexCacheReconciler.cs"]) {
  assert.doesNotMatch(fs.readFileSync(path.join(root, file), "utf8"), /CheapRejectRules|CheapTriageStatus/,
    "Observations must not gate classification, reconciliation, visibility, or deletion.");
}
console.log("PASS executable Admin navigation, retired-tab isolation, unchanged downstream boundary, and 241 archived hashes");
