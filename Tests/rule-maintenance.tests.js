"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const api = require("../wwwroot/rule-maintenance.js");
class Element {
  constructor(tag, doc) { this.tag = tag; this.doc = doc; this.children = []; this.handlers = {}; this.disabled = false; }
  setAttribute(k, v) { this[k] = v; }
  addEventListener(k, fn) { this.handlers[k] = fn; }
  append(...children) { for (const child of children) { if(child.parentNode) child.parentNode.children=child.parentNode.children.filter(n=>n!==child); child.parentNode = this; } this.children.push(...children); }
  replaceChildren(...children) { for (const child of this.children) child.parentNode = null; this.children = []; this.append(...children); }
  appendChild(child) { this.append(child); }
  focus() { this.doc.activeElement = this; }
  select() { this.selected = true; }
  showModal() { this.modal = true; }
  close() { this.handlers.close(); }
  remove() { this.removed = true; }
  async click() { if (!this.disabled) await this.handlers.click(); }
}
function setup(fetch) {
  const doc = { createElement(tag) { return new Element(tag, this); }, execCommand() { return true; } };
  doc.body = new Element("body", doc);
  const env = { document: doc, fetch, navigator: { clipboard: { async writeText(text) { env.copied = text; } } } };
  return env;
}
(async () => {
  const full = "Complete\n<script>literal, never HTML</script>\n" + "x".repeat(12000);
  let request;
  const env = setup(async (url, options) => { request = { url, options }; return { ok: true, async json() { return { prompt: full, rulesetVersion: "1.0.0", templateVersion: "1.0.0" }; } }; });
  const opener = api.createButton(env);
  assert.equal(opener.textContent, "Update RegEx Rules");
  await opener.click();
  const dialog = env.document.body.children[0];
  const [, status, textarea, copy, close] = dialog.children;
  for (const button of [opener, copy, close]) assert.match(button.className,/\bprimary-button\b/);
  assert.equal(textarea.className,"text-control");
  assert.equal(dialog.modal, true);
  assert.equal(textarea.value, full);
  assert.equal(textarea.readOnly, true);
  assert.match(status.textContent, /Ruleset 1.0.0/);
  assert.equal(request.url, "/api/admin/cheap-triage/maintenance-prompt");
  assert.equal(request.options.cache, "no-store");
  await copy.click(); assert.equal(env.copied, full);
  env.navigator.clipboard.writeText = async () => { throw new Error("denied"); };
  await copy.click(); assert.equal(textarea.selected, true);
  env.document.execCommand = () => false;
  await copy.click(); assert.match(status.textContent, /manually/);
  await close.click(); assert.equal(dialog.removed, true); assert.equal(opener.disabled, false);
  assert.equal(env.document.activeElement, opener); assert.equal(request.options.signal.aborted, true);
  const failed = setup(async () => ({ ok: false }));
  const bad = await api.open(api.createButton(failed), failed);
  assert.match(bad.children[1].textContent, /Unable/); assert.equal(bad.children[3].disabled, true);
  await bad.children[4].click(); assert.equal(bad.removed, true);
  let complete;
  const pending = setup(() => new Promise(resolve => { complete = resolve; }));
  const pendingButton = api.createButton(pending); const open = api.open(pendingButton, pending);
  const pendingDialog = pending.document.body.children[0]; pendingDialog.close();
  complete({ ok: true, async json() { return { prompt: full }; } }); await open;
  assert.equal(pendingButton.disabled, false); assert.equal(pendingDialog.children[2].value, undefined);
  const reviewedEnv = setup(async (url) => {
    assert.equal(url,"/api/admin/cheap-triage/human-review/maintenance-prompt");
    return {ok:true,json:async()=>({prompt:full,rulesetVersion:"1.0.0",templateVersion:"human-reviewed-1.0.0"})};
  });
  const reviewedButton=api.createButton(reviewedEnv,{title:"Prepare Rule Update",endpoint:"/api/admin/cheap-triage/human-review/maintenance-prompt"});
  await reviewedButton.click();
  const reviewedDialog=reviewedEnv.document.body.children[0];
  assert.equal(reviewedDialog.children[0].textContent,"Prepare Rule Update");
  await reviewedDialog.children[3].click();assert.equal(reviewedEnv.copied,full);
  await reviewedDialog.children[4].click();assert.equal(reviewedButton.disabled,false);
  env.document.execCommand=()=>true; env.navigator.clipboard.writeText=async value=>{env.copied=value;};
  await api.copyPrompt(full,env);assert.equal(env.copied,full);
  env.navigator.clipboard.writeText=async()=>{throw new Error("HTTP clipboard unavailable");};
  await api.copyPrompt(full,env);assert.equal(env.document.body.children.at(-1).selected,true);
  assert.equal(env.document.body.children.at(-1).removed,true);
  const root = path.join(__dirname, "..");
  const program = fs.readFileSync(path.join(root, "Program.cs"), "utf8");
  const route = program.slice(program.indexOf('app.MapGet("/api/admin/cheap-triage/maintenance-prompt"'));
  assert.match(route.slice(0, 600), /RequireAuthorization\(AdminAuthorization.Policy\)/);
  assert.match(route.slice(0, 600), /RequireRateLimiting\("state"\)/);
  assert.match(fs.readFileSync(path.join(root, "wwwroot/index.html"), "utf8"), /rule-maintenance.js\?v=9/);
  assert.match(fs.readFileSync(path.join(root, "wwwroot/app.js"), "utf8"), /RuleMaintenance.renderStatus\(elements.adminCheapPanel,/);
  const service = fs.readFileSync(path.join(root, "classifier-service/classifier_service.py"), "utf8");
  assert.match(service, /opt-in-llm-deep-analysis/);
  assert.doesNotMatch(service, /import transformers|AutoModelForSequenceClassification|deberta|bert-tiny/i);
  assert.match(fs.readFileSync(path.join(root, "ClassifierClient.cs"), "utf8"), /DeepAnalyzeAsync/);
  const baseline = { slices: { combined: { keepRecall: .995757, falseRejects: 9 }, description: { keepRecall: .990153 }, title_only: { keepRecall: 1 } }, cache: { rejectionRate: .13839 } };
  const statusEnv = setup(async (url, options) => {
    assert.equal(url, "/api/admin/cheap-triage/status"); assert.equal(options.cache, "no-store");
    return { ok: true, async json() { return { rulesetVersion: "1.0.0", rulesetFingerprint: "test-hash", metricsCurrent: true, frozenEvaluation: baseline }; } };
  });
  const panel = new Element("section", statusEnv.document);
  await api.renderStatus(panel, statusEnv);
  for (const button of panel.children.filter(n=>n.tag==="button")) assert.match(button.className,/\bprimary-button\b/,button.textContent);
  assert.match(panel.children[2].textContent, /Non-gating.*test-hash.*Off or Shadow/);
  assert.match(panel.children[3].textContent, /99.58%.*99.02%.*100.00%.*9 known false rejects.*13.84%/);
  assert.equal(panel.children.filter(n => n.tag === "button").length, 1, "Diagnostics only; primary actions belong to guided workflow");
  statusEnv.fetch = async () => ({ ok: true, json: async () => ({ metricsCurrent: false, rulesetVersion: "1.0.1", rulesetFingerprint: "new", frozenEvaluation: baseline }) });
  await api.renderStatus(panel, statusEnv);
  for (const button of panel.children.filter(n=>n.tag==="button")) assert.match(button.className,/\bprimary-button\b/,button.textContent);
  assert.match(panel.children[3].textContent, /do not match/);
  assert.doesNotMatch(panel.children[3].textContent, /99.58/);
  await api.renderStatus(panel, failed);
  assert.match(panel.children[2].textContent, /Unable/);
  let finishStatus;
  const delayedStatus = setup(() => new Promise(resolve => { finishStatus = resolve; }));
  const olderStatus = api.renderStatus(panel, delayedStatus);
  await api.renderStatus(panel, statusEnv);
  for (const button of panel.children.filter(n=>n.tag==="button")) assert.match(button.className,/\bprimary-button\b/,button.textContent);
  finishStatus({ ok: true, json: async () => ({ metricsCurrent: true, frozenEvaluation: baseline }) });
  await olderStatus;
  assert.equal(panel.children.length, 6);
  assert.match(panel.children[3].textContent, /do not match/);
  let mounted=false;
  statusEnv.CheapTriageWorkflow={mount(host){mounted=true;host.textContent="Guided maintenance";}};
  await api.renderStatus(panel,statusEnv);
  assert.ok(mounted);assert.equal(panel.children[1].textContent,"Guided maintenance");
  const diagnostics=panel.children[2];assert.equal(diagnostics.tag,"details");assert.ok(!diagnostics.open);
  assert.match(diagnostics.children.find(n=>n.textContent==="Refresh observations").className,/confirmation-secondary-button/);
  assert.ok(!panel.children.some(n=>n.textContent==="Update RegEx Rules"),"No competing primary maintenance action");
  const statusRoute = program.slice(program.indexOf('app.MapGet("/api/admin/cheap-triage/status"'));
  assert.match(statusRoute.slice(0, 650), /no-store/);
  assert.match(statusRoute.slice(0, 650), /RequireAuthorization\(AdminAuthorization.Policy\)/);
  console.log("PASS rule maintenance modal, full clipboard, fallback, close/abort, authorization and retained Job Fit runtime");
})().catch(error => { console.error(error); process.exitCode = 1; });
