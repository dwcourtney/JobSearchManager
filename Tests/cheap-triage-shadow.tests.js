"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const api = require("../wwwroot/cheap-triage.js");
class Element {
  constructor(tag) { this.tag = tag; this.children = []; this.handlers = {}; }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children = nodes; }
  addEventListener(name, action) { this.handlers[name] = action; }
  click() { this.handlers.click?.(); }
}
const flatten = node => [node.textContent || "", ...node.children.map(flatten)].join(" ");
const observation = { decision: "REJECT", analyzedAtUtc: "2026-09-06T00:00:00Z", descriptionAvailable: true,
  result: { category: "clinical", reason: "Nursing duties", rulesetVersion: "1.0.0", rulesetFingerprint: "hash",
    postingFingerprint: "input", ruleIds: ["reject-clinical"], evidence: [{ predicateId: "care", scope: "body", text: "<script>patient care</script>" }] } };
(async () => {
  let calls = 0, exported;
  const env = { document: { createElement: tag => new Element(tag) },
    fetch: async (url, options) => { calls++; assert.equal(options.cache, "no-store"); assert.match(url, /^\/api\/jobs\/cheap-triage\?stableId=/);
      return { ok: true, json: async () => ({ mode: "Shadow", current: true, observation }) }; },
    Blob: class { constructor(parts) { exported = parts.join(""); } }, URL: { createObjectURL: () => "blob:review", revokeObjectURL() {} } };
  const panel = new Element("section"), job = { stableId: "leidos:one" };
  await api.renderDetail(panel, job, env);
  assert.match(flatten(panel), /Shadow.*REJECT.*does not change Job Fit/);
  assert.match(flatten(panel), /reject-clinical.*hash.*input.*Title \+ description/);
  assert.match(flatten(panel), /<script>patient care<\/script>/, "Evidence remains literal text");
  await api.renderDetail(panel, job, env); assert.equal(calls, 1, "Repeated render reuses the observation request");
  env.fetch = async () => ({ ok: true, json: async () => ({ mode: "Off", current: false, observation: null }) });
  await api.renderDetail(panel, { stableId: "off" }, env);
  assert.match(flatten(panel), /Off.*Not analyzed.*disabled/);
  env.fetch = async () => ({ ok: true, json: async () => ({ mode: "Shadow", current: false, observation }) });
  await api.renderDetail(panel, { stableId: "stale" }, env);
  assert.match(flatten(panel), /REJECT \(stale\).*Refresh observation/);
  assert.match(panel.children[0].children.at(-1).className,/\bprimary-button\b/);
  let resolve;
  env.fetch = () => new Promise(done => { resolve = done; });
  const old = api.renderDetail(panel, { stableId: "old" }, env);
  await api.renderDetail(panel, null, env);
  resolve({ ok: true, json: async () => ({ mode: "Shadow", current: true, observation }) }); await old;
  assert.equal(panel.children.length, 0, "Late response cannot display another job's observation");
  env.fetch = async () => ({ ok: false }); await api.renderDetail(panel, job, env);
  assert.match(flatten(panel), /unavailable; Job Fit is unaffected/);
  const live = { mode: "Shadow", scope: "Selected source", companyId: "leidos", rulesetVersion: "1.0.0", rulesetFingerprint: "hash",
    analyzedJobs: 3, totalJobs: 5, keep: 1, reject: 1, undetermined: 1, titleOnly: 1, descriptionBacked: 2,
    rejectionPercentage: 100 / 3, requiringReconciliation: 2, stalePreviousRuleset: 1, running: true,
    latestAnalysisUtc: observation.analyzedAtUtc, rejectedJobs: [{ employer: "Example", job, workflowState: "saved", current: true,
      sourceAvailable: true, observation }] };
  const rendered = api.renderLive(live, env, () => 7);
  assert.match(flatten(rendered), /Live Shadow observation.*KEEP 1.*REJECT 1.*UNDETERMINED 1.*33.33%/);
  assert.match(flatten(rendered), /2 missing\/stale.*1 under previous rules/);
  assert.match(flatten(rendered), /Job Fit 7\/10.*saved/);
  const review = rendered.children.at(-1); assert.match(review.children[2].className,/\bprimary-button\b/); review.children[2].click();
  const data = JSON.parse(exported); assert.equal(data.jobs[0].jobFitScore, 7);
  assert.equal(data.jobs[0].observation.result.ruleIds[0], "reject-clinical");
  assert.equal(data.provisionalReviewOnly, true);
  assert.doesNotMatch(flatten(rendered), /Activate|saved LLM calls/);
  const root = path.resolve(__dirname, "..");
  const source = fs.readFileSync(path.join(root, "JobCatalog.CheapTriage.cs"), "utf8");
  assert.doesNotMatch(source, /FetchJobDetail|FetchAllJobs|SaveJobHistory|DeepAnalyze|ClassifyAsync|DeleteAsync/);
  const index = fs.readFileSync(path.join(root, "wwwroot/index.html"), "utf8");
  assert.ok(index.indexOf('src="/cheap-triage.js?v=2" defer') < index.indexOf('src="/app.js?v=56"'));
  assert.equal(JSON.parse(fs.readFileSync(path.join(root, "appsettings.json"))).CheapTriage.Mode, "Off");
  console.log("PASS Shadow detail states/races, live metrics/review/export, text safety and non-gating boundaries");
})().catch(error => { console.error(error); process.exitCode = 1; });
