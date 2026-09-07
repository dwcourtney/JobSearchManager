"use strict";
const assert = require("node:assert/strict"), fs = require("node:fs"), path = require("node:path");
const { mount } = require("../wwwroot/human-review.js");
class Node {
  constructor(tag) { this.tag = tag; this.children = []; this.handlers = {}; this.attrs = {}; }
  append(...items) { this.children.push(...items); }
  replaceChildren(...items) { this.children = items; }
  setAttribute(k,v) { this.attrs[k] = v; }
  addEventListener(k,v) { this.handlers[k] = v; }
  click() { if (!this.disabled) return this.handlers.click?.(); }
}
const all = n => [n,...n.children.flatMap(all)];
const text = n => all(n).map(x=>x.textContent||"").join(" ");
const button = (n,label) => all(n).find(x=>x.tag==="button"&&x.textContent===label);
const assertThemedControls = panel => {
  const controls = all(panel).filter(n => ["button", "input", "select", "textarea", "a"].includes(n.tag));
  assert.ok(controls.length > 0);
  for (const control of controls) {
    const classes = (control.className || "").split(/\s+/);
    const required = control.tag === "button" ? "primary-button" : control.tag === "a" ? "source-posting-button" : "text-control";
    assert.ok(classes.includes(required), `${control.tag} ${control.textContent || ""} must use the shared theme style`);
  }
};
const root = path.resolve(__dirname,"..");
const queue = JSON.parse(fs.readFileSync(path.join(root,"CheapTriage/review-queues/human-review-v1.json")));
const original = fs.readFileSync(path.join(root,"Tests/cheap_reject_evaluation/human_review_v1/excerpt-provenance.json"));
assert.deepEqual(queue.cases.map(x=>x.stableJobId).sort(),JSON.parse(original).map(x=>x.stableId).sort());
(async () => {
  const reviews = {}; let revision=0, fail=false, conflict=false;
  function report() {
    const counts={KEEP:0,REJECT:0,AMBIGUOUS:0}; Object.values(reviews).forEach(x=>counts[x.decision]++);
    return {...queue, categoryName:"Technology", queueFingerprint:"hash", reviews:structuredClone(reviews), reviewed:Object.keys(reviews).length,
      remaining:29-Object.keys(reviews).length, counts, complete:Object.keys(reviews).length===29};
  }
  const env={document:{createElement:t=>new Node(t)},fetch:async (url,options)=>{
    assert.equal(url,"/api/admin/cheap-triage/human-review"); assert.equal(options.cache,"no-store");
    if(options.method==="POST") {
      if(fail)return {ok:false,status:500}; if(conflict)return {ok:false,status:409};
      const r=JSON.parse(options.body); assert.equal(r.queueFingerprint,"hash");
      reviews[r.stableJobId]={...r,revision:++revision,reviewer:"admin",reviewedAtUtc:"2026-09-06T00:00:00Z"};
    }
    return {ok:true,json:async()=>report()};
  }};
  const panel=new Node("section"); await mount(panel,env).ready; assertThemedControls(panel);
  assert.match(text(panel),/1 of 29.*0 reviewed.*29 remaining/);
  assert.match(text(panel),/QUESTIONABLE.*Description: backed/);
  const visible = n => n.tag === "details" ? [] : [n,...n.children.flatMap(visible)];
  const visibleText = n => visible(n).map(x=>x.textContent||"").join(" ");
  assert.match(visibleText(panel),/Does this job belong to Technology\?/);
  for(const value of [queue.cases[0].title, queue.cases[0].employer, queue.cases[0].dutyExcerpt1]) assert.ok(visibleText(panel).includes(value));
  for(const value of [queue.cases[0].stableJobId, queue.sourceManifestHash, queue.cases[0].matchedRuleIds, "Workflow snapshot", "QUESTIONABLE"])
    assert.ok(!visibleText(panel).includes(value), `${value} belongs inside collapsed diagnostics`);
  const details = all(panel).find(n=>n.tag==="details");
  assert.ok(!details.open && !Object.hasOwn(details.attrs,"open"));
  assert.equal(details.children[0].textContent,"Show technical details");
  const question = all(panel).find(n=>n.className==="human-review-question");
  const parent = all(panel).find(n=>n.children.includes(question));
  assert.equal(parent.children[parent.children.indexOf(question)+1].className,"human-review-controls human-review-decisions");
  const help = all(panel).find(n=>n.className==="human-review-help");
  assert.equal(parent.children[parent.children.indexOf(question)+2],help);
  const legend = help.children[0]; assert.equal(legend.tag,"dl");
  assert.deepEqual(legend.children.map(n=>n.children.map(c=>[c.tag,c.textContent])),[
    [["dt","KEEP"],["dd","— belongs to Technology"]],[["dt","REJECT"],["dd","— does not belong to Technology"]],
    [["dt","AMBIGUOUS"],["dd","— genuinely unclear"]]]);
  assert.match(text(help),/Judge the job's primary duties, not whether you would personally apply/);
  const audit = all(panel).find(n=>n.className==="human-review-audit");
  assert.deepEqual(audit.children.map(n=>n.tag),["details","a"],"Details and export have separate ordered grid rows");
  const alternate = new Node("section");
  await mount(alternate,{...env,fetch:async()=>({ok:true,json:async()=>({...report(),categoryName:"Healthcare"})})}).ready;
  assert.match(visibleText(alternate),/Does this job belong to Healthcare\?/);
  assert.equal(Object.keys(reviews).length,0,"Rendering must not save decisions");
  assert.equal(button(panel,"Save and Next").disabled,true);
  assert.equal(button(panel,"Prepare Rule Update"),undefined,"No action before completion");
  button(panel,"KEEP").click(); assert.equal(button(panel,"KEEP").attrs["aria-pressed"],"true");
  assert.equal(button(panel,"Next").disabled,true,"Draft cannot be silently lost through navigation");
  const note=all(panel).find(x=>x.tag==="textarea");note.value="<img src=x onerror=alert(1)>";note.handlers.input();
  fail=true;const saving=button(panel,"Save and Next").click();
  assertThemedControls(panel); assert.equal(note.disabled,true);
  for(const control of all(panel).filter(n=>n.tag==="button")) assert.equal(control.disabled,true,"Controls remain disabled while saving");
  await saving; assert.equal(note.disabled,false); assert.match(text(panel),/Save failed/);assert.equal(note.value,"<img src=x onerror=alert(1)>");
  fail=false;await button(panel,"Save and Next").click();assert.match(text(panel),/1 reviewed.*28 remaining/);
  assert.match(text(panel),/2 of 29/);button(panel,"Previous").click();
  assert.equal(button(panel,"KEEP").attrs["aria-pressed"],"true");
  button(panel,"AMBIGUOUS").click();await button(panel,"Save and Next").click();
  assert.equal(Object.values(reviews)[0].decision,"AMBIGUOUS");
  await mount(panel,env).ready;assert.equal(button(panel,"AMBIGUOUS").attrs["aria-pressed"],"true","Reopening reloads saved choice");
  button(panel,"REJECT").click();conflict=true;await button(panel,"Save and Next").click();assert.match(text(panel),/changed.*reload/);conflict=false;
  await button(panel,"Discard draft / Reload").click();
  button(panel,"Next").click();
  for(let i=1;i<29;i++){ button(panel,"REJECT").click();await button(panel,i===28 ? "Save review" : "Save and Next").click(); }
  assert.match(text(panel),/29 reviewed.*0 remaining.*Review Complete/);
  assert.match(text(panel),/KEEP 0.*REJECT 28.*AMBIGUOUS 1.*Rules have NOT been changed/);
  assert.equal(button(panel,"Next").disabled,true); assertThemedControls(panel);
  assert.ok(button(panel,"Prepare Rule Update"));
  const completion=all(panel).find(n=>(n.className||"").includes("human-review-complete"));
  assert.match(text(completion),/Human Review Complete.*29 reviewed.*KEEP 0.*REJECT 28.*AMBIGUOUS 1.*human_review_v1.*hash/);
  assert.equal(all(panel).find(x=>x.tag==="a").href,"/api/admin/cheap-triage/human-review/export");
  const unavailable = new Node("section");
  await mount(unavailable, {...env, fetch: async () => ({ok:false})}).ready;
  assert.equal(button(unavailable,"Retry").disabled, undefined);
  assertThemedControls(unavailable);
  const styles=fs.readFileSync(path.join(root,"wwwroot/styles.css"),"utf8");
  assert.match(styles,/\.human-review-question\s*\{[^}]*font-size: var\(--font-size-page-heading\)/);
  assert.match(styles,/\.human-review-decisions button\s*\{[^}]*font-size: var\(--font-size-detail-heading\)/);
  const scopedRules=[...styles.matchAll(/([^{}]+)\{([^{}]*)\}/g)].filter(m=>m[1].includes(".human-review"));
  for(const [,selector,body] of scopedRules) {
    assert.doesNotMatch(body,/position:\s*(absolute|fixed)|(?:margin|top|left|right|bottom)[^:]*:\s*-|(?:^|;)\s*transform:/,selector);
  }
  assert.match(styles,/\.human-review-audit\s*\{[^}]*display: grid;[^}]*gap: 1rem/);
  assert.match(styles,/\.human-review-legend\s*\{[^}]*flex-wrap: wrap/);
  assert.match(styles,/\.human-review-controls\s*\{[^}]*flex-wrap: wrap/);
  assert.match(styles,/\.settings-section \.human-review-question\s*\{[^}]*font-size: var\(--font-size-page-heading\)/);
  const settings=JSON.parse(fs.readFileSync(path.join(root,"appsettings.json"),"utf8"));
  assert.equal(settings.HumanReview.CategoryName,"Technology");
  const program=fs.readFileSync(path.join(root,"Program.cs"),"utf8");
  assert.match(program,/MapGroup\("\/api\/admin\/cheap-triage\/human-review"\)\s*\.RequireAuthorization\(AdminAuthorization.Policy\)\.RequireRateLimiting\("state"\)/);
  assert.match(program,/RequireRateLimiting\("state"\)\.DisableCookieRedirect\(\)/);
  assert.match(program,/humanReviewApi.MapGet\("\/export"/);
  assert.match(program,/humanReviewApi.MapPost/);
  assert.match(program,/RequestSecurity.HasSameOrigin/);
  const backend=fs.readFileSync(path.join(root,"CheapTriageHumanReview.cs"),"utf8");
  assert.doesNotMatch(backend,/JobCatalog|SaveJobHistory|CheapRejectRules|DeepAnalyze|FetchJob/);
  const index=fs.readFileSync(path.join(root,"wwwroot/index.html"),"utf8");
  assert.ok(index.indexOf("human-review.js?v=7")<index.indexOf("rule-maintenance.js?v=10"));
  console.log("PASS Human Review 29 IDs, save/navigation/revision/errors/completion/export, safe rendering and Admin route boundaries");
})().catch(e=>{console.error(e);process.exitCode=1;});
