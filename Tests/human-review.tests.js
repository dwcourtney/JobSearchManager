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
const root = path.resolve(__dirname,"..");
const queue = JSON.parse(fs.readFileSync(path.join(root,"CheapTriage/review-queues/human-review-v1.json")));
const original = fs.readFileSync(path.join(root,"Tests/cheap_reject_evaluation/human_review_v1/excerpt-provenance.json"));
assert.deepEqual(queue.cases.map(x=>x.stableJobId).sort(),JSON.parse(original).map(x=>x.stableId).sort());
(async () => {
  const reviews = {}; let revision=0, fail=false, conflict=false;
  function report() {
    const counts={KEEP:0,REJECT:0,AMBIGUOUS:0}; Object.values(reviews).forEach(x=>counts[x.decision]++);
    return {...queue, queueFingerprint:"hash", reviews:structuredClone(reviews), reviewed:Object.keys(reviews).length,
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
  const panel=new Node("section"); await mount(panel,env).ready;
  assert.match(text(panel),/1 of 29.*0 reviewed.*29 remaining/);
  assert.match(text(panel),/QUESTIONABLE.*Description: backed/);
  assert.equal(button(panel,"Save review").disabled,true);
  button(panel,"KEEP").click(); assert.equal(button(panel,"KEEP").attrs["aria-pressed"],"true");
  assert.equal(button(panel,"Next").disabled,true,"Draft cannot be silently lost through navigation");
  const note=all(panel).find(x=>x.tag==="textarea");note.value="<img src=x onerror=alert(1)>";note.handlers.input();
  fail=true;await button(panel,"Save review").click();assert.match(text(panel),/Save failed/);assert.equal(note.value,"<img src=x onerror=alert(1)>");
  fail=false;await button(panel,"Save review").click();assert.match(text(panel),/1 reviewed.*28 remaining/);
  button(panel,"Next").click();button(panel,"Previous").click();
  assert.equal(button(panel,"KEEP").attrs["aria-pressed"],"true");
  button(panel,"AMBIGUOUS").click();await button(panel,"Save review").click();
  assert.equal(Object.values(reviews)[0].decision,"AMBIGUOUS");
  await mount(panel,env).ready;assert.equal(button(panel,"AMBIGUOUS").attrs["aria-pressed"],"true","Reopening reloads saved choice");
  button(panel,"REJECT").click();conflict=true;await button(panel,"Save review").click();assert.match(text(panel),/changed.*reload/);conflict=false;
  await button(panel,"Discard draft / Reload").click();
  for(let i=1;i<29;i++){ button(panel,"Next").click();button(panel,"REJECT").click();await button(panel,"Save review").click(); }
  assert.match(text(panel),/29 reviewed.*0 remaining.*Review Complete/);
  assert.match(text(panel),/KEEP 0.*REJECT 28.*AMBIGUOUS 1.*Rules have NOT been changed/);
  assert.equal(button(panel,"Next").disabled,true);
  assert.equal(all(panel).find(x=>x.tag==="a").href,"/api/admin/cheap-triage/human-review/export");
  const program=fs.readFileSync(path.join(root,"Program.cs"),"utf8");
  assert.match(program,/MapGroup\("\/api\/admin\/cheap-triage\/human-review"\)\s*\.RequireAuthorization\(AdminAuthorization.Policy\)\.RequireRateLimiting\("state"\)/);
  assert.match(program,/RequireRateLimiting\("state"\)\.DisableCookieRedirect\(\)/);
  assert.match(program,/humanReviewApi.MapGet\("\/export"/);
  assert.match(program,/humanReviewApi.MapPost/);
  assert.match(program,/RequestSecurity.HasSameOrigin/);
  const backend=fs.readFileSync(path.join(root,"CheapTriageHumanReview.cs"),"utf8");
  assert.doesNotMatch(backend,/JobCatalog|SaveJobHistory|CheapRejectRules|DeepAnalyze|FetchJob/);
  const index=fs.readFileSync(path.join(root,"wwwroot/index.html"),"utf8");
  assert.ok(index.indexOf("human-review.js?v=1")<index.indexOf("rule-maintenance.js?v=4"));
  console.log("PASS Human Review 29 IDs, save/navigation/revision/errors/completion/export, safe rendering and Admin route boundaries");
})().catch(e=>{console.error(e);process.exitCode=1;});
