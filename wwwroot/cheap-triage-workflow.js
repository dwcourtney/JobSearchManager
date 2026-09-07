"use strict";
(function(root,factory){const api=factory();if(typeof module==="object"&&module.exports)module.exports=api;else root.CheapTriageWorkflow=api;})(globalThis,()=>{
  const endpoint="/api/admin/cheap-triage/human-review/workflow";
  const stages={"review-required":["REVIEW_REQUIRED","Human Review Required","Start Human Review"],"review-progress":["REVIEW_IN_PROGRESS","Human Review In Progress","Continue Human Review"],"review-complete":["REVIEW_COMPLETE","Human Review Complete","Prepare Rule Update"],"prompt-ready":["CODEX_ANALYSIS_REQUIRED","Codex Analysis Required","Copy Codex Prompt"],candidate:["CANDIDATE_READY","Candidate Ready","Review Candidate"],approved:["CANDIDATE_APPROVED","Candidate Approved","Prepare Release Request"],rejected:["CANDIDATE_REJECTED","Candidate Rejected","Prepare Revision Request"],"release-request-ready":["RELEASE_REQUEST_READY","Release Request Ready","Copy Release Prompt"],released:["RELEASED","Release Complete",null]};
  function currentStep(stage){return stage.startsWith("review-")?(stage==="review-complete"?2:1):stage==="prompt-ready"?3:4;}
  function mount(panel,env=globalThis){
    const doc=env.document;let state,busy=false,reviewOpen=false,candidateOpen=false,changing=false;
    const make=(tag,value)=>{const n=doc.createElement(tag);n.textContent=value;return n;};
    const card=make("section","");card.className="triage-workflow admin-evaluation-card";
    const status=make("p","");status.setAttribute("role","status");panel.replaceChildren(card,status);
    const button=(label,action,primary=false)=>{const b=make("button",label);b.type="button";b.className="primary-button admin-evaluation-action"+(primary?"":" confirmation-secondary-button");if(primary)b.setAttribute("data-workflow-primary","true");b.disabled=busy;b.addEventListener("click",action);return b;};
    const details=(label)=>{const d=make("details","");d.append(make("summary",label));return d;};
    async function load(){try{const r=await env.fetch(endpoint,{cache:"no-store"});if(!r.ok)throw Error("Unable to load workflow.");state=await r.json();draw();status.textContent="";}catch(e){status.textContent=e.message;card.replaceChildren(button("Retry workflow",load,true));}}
    async function act(path,body={}){if(busy)return;busy=true;draw();try{const r=await env.fetch(endpoint+path,{method:"POST",cache:"no-store",headers:{"Content-Type":"application/json"},body:JSON.stringify({key:state.key,revision:state.revision,...body})});const result=await r.json();if(!r.ok)throw Error(result.error||"Request failed. Reload and retry.");state=result;candidateOpen=false;changing=false;status.textContent="Saved. Running rules have not changed.";}catch(e){status.textContent=e.message;}finally{busy=false;draw();}}
    async function copy(prepared){try{await env.RuleMaintenance.copyPrompt(prepared.prompt,env);status.textContent="Copied. Paste into Codex to continue. Nothing ran automatically.";}catch{status.textContent="Clipboard unavailable. Open View Prompt to select the text manually.";}}
    function draw(){
      const [code,title,next]=stages[state.stage]||["UNKNOWN","Workflow unavailable",null],r=state.review,c=state.candidate;
      card.replaceChildren(make("h3","Cheap Triage Rule Maintenance"));card.setAttribute("data-workflow-state",code);card.append(make("h4",title));
      const technical=details("Technical Details");technical.append(make("p",`Queue ${r.queueVersion} · ${r.queueFingerprint} · Workflow revision ${state.revision}.`),button("Reload Workflow",load));
      const phase=currentStep(state.stage);const steps=make("ol","");steps.className="triage-workflow-steps";
      ["Human review","Candidate analysis","Candidate decision","Release"].forEach((label,i)=>{const active=state.stage==="released"?4:phase===1?0:phase===2||phase===3?1:state.disposition?3:2;const li=make("li",`${label} — ${i<active?"Complete":i===active?"Current":"Upcoming"}`);li.setAttribute("data-state",i<active?"complete":i===active?"current":"upcoming");if(i===active)li.setAttribute("aria-current","step");steps.append(li);});card.append(steps);
      const summary=()=>{
        card.append(make("p",`${c.humanCorrections} reviewed false rejects corrected.`));
        // Counts are explicitly reported evidence; do not silently turn missing evidence into a guarantee.
        const changedReviewedRejects=c.changedDecisions.filter(x=>x.human?.decision==="REJECT"&&x.after.decision!=="REJECT");
        card.append(make("p",`${r.counts.REJECT-changedReviewedRejects.length} reviewed rejects preserved (reported evaluation).`),make("p","Running rules have NOT changed."));
      };
      const openCandidate=(edit=false)=>{candidateOpen=!candidateOpen||edit;changing=edit;draw();};
      const candidateDetails=()=>{
        const section=make("section","");section.className="admin-evaluation-card";section.setAttribute("aria-label","Candidate review");section.append(make("h4","Review the proposed change"),make("p",`${c.humanCorrections} reviewed false rejects corrected; ${r.counts.REJECT} human REJECT decisions in the review set. See changed decisions and safety evidence below.`));
        const table=make("table","");table.className="admin-compact-table";const head=make("thead","");const h=make("tr","");["Metric","Before","After"].forEach(t=>h.append(make("th",t)));head.append(h);table.append(head);const body=make("tbody","");
        for(const [key,label] of [["keepRecall","KEEP recall"],["describedKeepRecall","Described KEEP recall"],["titleOnlyKeepRecall","Title-only KEEP recall"],["rejectionRate","Rejection rate"],["oldCacheRejectionRate","Old-cache rejection rate"],["falseRejectCount","False rejects"]]){const row=make("tr","");row.append(make("th",label),make("td",key==="falseRejectCount"?c.before[key]:`${(100*c.before[key]).toFixed(2)}%`),make("td",key==="falseRejectCount"?c.after[key]:`${(100*c.after[key]).toFixed(2)}%`));body.append(row);}table.append(body);section.append(table);
        const newRejects=c.changedDecisions.filter(x=>x.before.decision==="KEEP"&&x.after.decision==="REJECT");section.append(make("p",`New KEEP → REJECT decisions: ${newRejects.length}. Validation: ${c.validationStatus}.`));
        for(const w of c.warnings)section.append(make("p",`Warning: ${w}`));for(const f of c.safetyFailures)section.append(make("p",`Safety failure: ${f}`));
        const changes=details(`Every changed decision (${c.changedDecisions.length})`);for(const x of c.changedDecisions){const item=details(`${x.title||x.id}: ${x.before.decision} → ${x.after.decision}`);item.append(make("pre",JSON.stringify(x,null,2)));changes.append(item);}section.append(changes);
        if(!state.disposition||changing){section.append(make("p","Choose whether to accept this candidate. Neither decision changes or deploys running rules."));const controls=make("div","");controls.className="human-review-controls";controls.append(button("Approve Candidate",()=>act("/decision",{candidateHash:c.candidateHash,decision:"approved"})),button("Reject Candidate",()=>act("/decision",{candidateHash:c.candidateHash,decision:"rejected"})));section.append(controls);}
        section.append(button("Close Candidate Details",()=>{candidateOpen=false;changing=false;draw();}));card.append(section);
      };
      if(state.stage==="review-required"||state.stage==="review-progress"){
        card.append(make("p",r.reviewed?`${r.reviewed} of ${r.cases.length} reviewed; ${r.remaining} remaining.`:`${r.cases.length} potentially unsafe decisions need your review before rules can be updated.`));
        if(!reviewOpen)card.append(button(next,()=>{reviewOpen=true;draw();},true));
        else{const host=make("section","");card.append(host);env.HumanReview?.mount(host,env,{guided:true,onSaved:report=>{state.review=report;if(report.complete){reviewOpen=false;load();}}});card.append(button("Back to Workflow",()=>{reviewOpen=false;draw();}));}
      }else if(state.stage==="review-complete"){
        card.append(make("p",`${r.reviewed} of ${r.cases.length} reviewed. KEEP ${r.counts.KEEP} · REJECT ${r.counts.REJECT} · AMBIGUOUS ${r.counts.AMBIGUOUS}.`),make("p","Next, prepare instructions for Codex to build and evaluate a candidate using your decisions. Preparing the request does not run Codex or change rules."),button(next,()=>act("/prepare"),true));
      }else if(state.stage==="prompt-ready"){
        card.append(make("p","Paste this prompt into Codex, let the analysis finish, then return with candidate-result.json. JSM cannot detect when Codex finishes. When you return, open Technical Details → Codex finished? Supply candidate result. Nothing changes automatically."),button(next,()=>copy(state.prompt),true));
        const handoff=details("Codex finished? Supply candidate result");handoff.append(make("p","Choose the result file to import it, or import the result Codex synced to curiosity."));
        const input=make("input","");input.type="file";input.accept=".json,application/json";input.hidden=true;input.tabIndex=-1;input.setAttribute("aria-label","Candidate result file");
        const filename=make("span","No file selected");filename.id="candidate-selected-file";filename.setAttribute("aria-live","polite");
        const picker=button("Choose File",()=>{input.value="";input.click();});picker.setAttribute("aria-describedby","candidate-selected-file");const controls=make("div","");controls.className="human-review-controls";controls.append(picker,filename,input);handoff.append(controls,button("Import Synced Result",()=>act("/import-synced")));
        input.addEventListener("change",async()=>{try{const f=input.files?.[0];if(!f)return;filename.textContent=f.name;if(f.size>2000000)throw Error("Candidate file must be below 2 MB.");await act("/candidate",{candidate:JSON.parse(await f.text())});}catch(e){status.textContent=e.message;}});technical.append(handoff);
        card.append(button("View Prompt",e=>env.RuleMaintenance.open(e?.target,env,{title:"Codex Analysis Request",prepared:state.prompt})));
      }else if(state.stage==="candidate"){
        card.append(make("p","Candidate imported successfully. Review its corrections, metrics and safety warnings before accepting or rejecting it. Running rules have not changed."),button(next,()=>openCandidate(),true));
      }else if(state.stage==="approved"||state.stage==="rejected"){
        if(state.stage==="approved")summary();else card.append(make("p","Your rejection is saved. Prepare a revision request for Codex; running rules remain unchanged."));
        card.append(make("p",state.stage==="approved"?"Prepare the release prompt to hand to Codex. Preparing it does not commit, push or deploy anything.":"The new request uses your completed human review. Codex must evaluate the revision before you decide again."),button(next,()=>act(state.stage==="approved"?"/prepare-release":"/prepare"),true),button("View Candidate Details",()=>openCandidate()),button("Change Decision",()=>openCandidate(true)));
      }else if(state.stage==="release-request-ready"){
        card.append(make("p","Copy this release prompt into Codex. Codex will finalize, commit, push, validate CI and deploy after successful checks. Cheap Triage remains Shadow unless separately changed. Copying alone runs nothing."),button(next,()=>copy(state.releasePrompt),true),button("View Prompt",e=>env.RuleMaintenance.open(e?.target,env,{title:"Release Request",prepared:state.releasePrompt})),button("View Candidate Details",()=>openCandidate()),button("Change Decision",()=>openCandidate(true)));
        card.append(make("p","Release remains pending until a release receipt matches the running committed build and ruleset. JSM does not detect external Codex progress."));
      }else if(state.stage==="released"){
        card.append(make("p",`Active ruleset ${state.release.rulesetVersion} · ${state.release.candidateHash}`),make("p",`Deployment identity: ${state.release.deploymentIdentity}`),make("p",`Validation: ${state.release.validationStatus}. No maintenance action is needed until a new review cycle.`));
      }
      if(c){technical.append(make("p",`Candidate ${c.candidateVersion} · SHA-256 ${c.candidateHash}`),make("p",`Baseline ${c.baselineVersion} · SHA-256 ${c.baselineHash}`),make("p","Metrics are imported evaluation evidence. JSM validates structure and identity, not the external evaluation itself."));if(candidateOpen)candidateDetails();}
      card.append(technical);
    }
    return {ready:load()};
  }
  return {mount,currentStep,stages};
});
