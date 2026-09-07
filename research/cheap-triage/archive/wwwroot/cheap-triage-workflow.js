"use strict";
(function(root,factory){const api=factory();if(typeof module==="object"&&module.exports)module.exports=api;else root.CheapTriageWorkflow=api;})(globalThis,()=>{
  const endpoint="/api/admin/cheap-triage/human-review/workflow";
  const stages={"review-required":["REVIEW_REQUIRED","Human Review Required","Start Human Review"],"review-progress":["REVIEW_IN_PROGRESS","Human Review In Progress","Continue Human Review"],"review-complete":["REVIEW_COMPLETE","Human Review Complete","Prepare Rule Update"],"prompt-ready":["CODEX_ANALYSIS_REQUIRED","Codex Analysis Required","Copy Codex Prompt"],candidate:["CANDIDATE_READY","Candidate Ready","Review Candidate"],approved:["CANDIDATE_APPROVED","Candidate Approved","Prepare Release Request"],rejected:["CANDIDATE_REJECTED","Candidate Rejected","Prepare Revision Request"],"release-request-ready":["RELEASE_REQUEST_READY","Release Request Ready","Copy Release Prompt"],released:["RELEASED","Release Complete",null],"no-update-needed":["NO_UPDATE_NEEDED","Cheap Triage Maintenance Complete",null]};
  function currentStep(stage){return stage.startsWith("review-")?(stage==="review-complete"?2:1):stage==="prompt-ready"?3:["approved","release-request-ready","released","no-update-needed"].includes(stage)?5:4;}
  function mount(panel,env=globalThis){
    const doc=env.document;let state,busy=false,reviewOpen=false,candidateOpen=false,changing=false,runtime=null;let updateSummary=()=>{};
    const make=(tag,value)=>{const n=doc.createElement(tag);n.textContent=value;return n;};
    const card=make("section","");card.className="triage-workflow admin-evaluation-card";
    const status=make("p","");status.className="triage-workflow-status admin-evaluation-card";status.setAttribute("role","status");panel.replaceChildren(card,status);
    const button=(label,action,primary=false)=>{const b=make("button",label);b.type="button";b.className="primary-button admin-evaluation-action"+(primary?"":" confirmation-secondary-button");if(primary)b.setAttribute("data-workflow-primary","true");b.disabled=busy;b.addEventListener("click",action);return b;};
    const details=(label)=>{const d=make("details","");d.append(make("summary",label));return d;};
    async function load(){try{const r=await env.fetch(endpoint,{cache:"no-store"});if(!r.ok)throw Error("Unable to load workflow.");state=await r.json();draw();status.textContent="";}catch(e){status.textContent=e.message;card.replaceChildren(button("Retry workflow",load,true));}}
    async function act(path,body={}){if(busy)return;busy=true;draw();try{const r=await env.fetch(endpoint+path,{method:"POST",cache:"no-store",headers:{"Content-Type":"application/json"},body:JSON.stringify({key:state.key,revision:state.revision,...body})});const result=await r.json();if(!r.ok)throw Error(result.error||"Request failed. Reload and retry.");state=result;candidateOpen=false;changing=false;status.textContent="Saved. Running rules have not changed.";}catch(e){status.textContent=e.message;}finally{busy=false;draw();}}
    async function copy(prepared){try{await env.RuleMaintenance.copyPrompt(prepared.prompt,env);status.textContent="Copied. Paste into Codex to continue. Nothing ran automatically.";}catch{status.textContent="Clipboard unavailable. Open View Prompt to select the text manually.";}}
    function draw(){
      const [code,title,next]=stages[state.stage]||["UNKNOWN","Workflow unavailable",null],r=state.review,c=state.candidate;
      card.replaceChildren(make("h3","Cheap Triage Rule Maintenance"));card.setAttribute("data-workflow-state",code);const heading=make("h4",title);heading.className="triage-workflow-title";card.append(heading);
      const technical=details("Technical Details");technical.append(make("p",`Queue ${r.queueVersion} · ${r.queueFingerprint} · Workflow revision ${state.revision}.`),button("Reload Workflow",load));
      const noUpdate=state.noUpdate;const phase=currentStep(state.stage);const steps=make("ol","");steps.className="triage-workflow-steps";steps.setAttribute("aria-label","Rule maintenance progress");
      (noUpdate?["Human Review","Prepare Update","Codex Analysis","No Update Needed","Release"]:["Human Review","Prepare Update","Run in Codex","Candidate Evaluation","Release"]).forEach((label,i)=>{const active=state.stage==="released"||noUpdate?5:phase-1;const progress=noUpdate&&i===4?"not required":i<active?"complete":i===active?"current":"upcoming";const li=make("li",`${i+1}. ${label}`);li.append(make("span",progress));li.setAttribute("data-state",progress);if(i===active)li.setAttribute("aria-current","step");steps.append(li);});card.append(steps);
      const runtimeHash=make("p","");technical.append(runtimeHash);
      if(state.detection){technical.append(make("h4","Automatic evidence detection"),make("pre",JSON.stringify(state.detection,null,2)));}
      const metrics=make("dl","");metrics.className="triage-summary-grid";metrics.setAttribute("aria-label","Workflow summary metrics");
      updateSummary=()=>{
        const percent=n=>Number.isFinite(n)?`${(100*n).toFixed(2)}%`:"Unavailable";
        const frozen=runtime?.metricsCurrent?runtime.frozenEvaluation:null;
        let values=[["Human review",`${r.reviewed} / ${r.cases.length}`],["Human decisions",`KEEP ${r.counts.KEEP} · REJECT ${r.counts.REJECT} · AMBIGUOUS ${r.counts.AMBIGUOUS}`],
          ["Running rules",runtime?.rulesetVersion||state.release?.rulesetVersion||"Loading…"],["Candidate rules",c?.candidateVersion||"Not prepared"],
          ["Live mode",runtime?`${runtime.mode} · Non-gating`:"Loading…"],["Baseline KEEP recall",c?percent(c.before.keepRecall):"No candidate baseline"],
          ["Candidate KEEP recall",c?percent(c.after.keepRecall):"Not evaluated"],["Running frozen recall",percent(frozen?.slices.combined.keepRecall)],
          ["Candidate rejection",c?percent(c.after.rejectionRate):"Not evaluated"],["Live cache rejection",runtime?.live?`${runtime.live.rejectionPercentage.toFixed(2)}% · ${runtime.live.totalJobs} cached`:"Unavailable"]];
        if(state.stage==="review-required"||state.stage==="review-progress")values=values.filter(([label])=>["Human review","Human decisions","Running rules","Live mode","Running frozen recall","Live cache rejection"].includes(label));
        if(noUpdate){values=[
          ["Human review",`${r.reviewed} / ${r.cases.length} complete`],["KEEP",r.counts.KEEP],["REJECT",r.counts.REJECT],["AMBIGUOUS",r.counts.AMBIGUOUS],
          ["Running ruleset",noUpdate.rulesetVersion],["Running frozen recall",percent(noUpdate.metrics.keepRecall)],
          ["Live mode",runtime?`${runtime.mode} / Non-gating`:"Loading..."]];}
        runtimeHash.textContent=`Running ruleset hash: ${runtime?.rulesetFingerprint||"Loading…"}`;
        metrics.replaceChildren();for(const [label,value] of values){const item=make("div","");item.append(make("dt",label),make("dd",value));metrics.append(item);}
      };updateSummary();
      if(noUpdate){technical.append(make("p",`Recorded result SHA-256: ${state.resultHash}`),make("p",`Evaluation artifact SHA-256: ${noUpdate.evaluationArtifactHash}`),make("p","Reported evaluation evidence; identity and saved human revisions verified. Candidate and release paths were skipped because no rule change was proposed."),make("pre",JSON.stringify(noUpdate,null,2)));}
      const action=make("section","");action.className="triage-current-action admin-evaluation-card";action.setAttribute("aria-label","Current workflow action");card.append(action,metrics);
      const summary=()=>{
        action.append(make("p",`${c.humanCorrections} reviewed false rejects corrected.`));
        // Counts are explicitly reported evidence; do not silently turn missing evidence into a guarantee.
        const changedReviewedRejects=c.changedDecisions.filter(x=>x.human?.decision==="REJECT"&&x.after.decision!=="REJECT");
        action.append(make("p",`${r.counts.REJECT-changedReviewedRejects.length} reviewed rejects preserved (reported evaluation).`),make("p","Running rules have NOT changed."));
      };
      const openCandidate=(edit=false)=>{candidateOpen=!candidateOpen||edit;changing=edit;draw();};
      const candidateDetails=()=>{
        const section=make("section","");section.className="admin-evaluation-card triage-candidate-review";section.setAttribute("aria-label","Candidate review");section.append(make("h4","Review the proposed change"),make("p",`${c.humanCorrections} reviewed false rejects corrected; ${r.counts.REJECT} human REJECT decisions in the review set. See changed decisions and safety evidence below.`));
        const table=make("table","");table.className="admin-compact-table";const head=make("thead","");const h=make("tr","");["Metric","Baseline","Candidate","Delta"].forEach(t=>{const cell=make("th",t);cell.setAttribute("scope","col");h.append(cell);});head.append(h);table.append(head);const body=make("tbody","");
        for(const [key,label] of [["keepRecall","KEEP recall"],["describedKeepRecall","Described KEEP recall"],["titleOnlyKeepRecall","Title-only KEEP recall"],["rejectionRate","Rejection rate"],["oldCacheRejectionRate","Old-cache rejection rate"],["falseRejectCount","False rejects"]]){const row=make("tr","");row.append(make("th",label),make("td",key==="falseRejectCount"?c.before[key]:`${(100*c.before[key]).toFixed(2)}%`),make("td",key==="falseRejectCount"?c.after[key]:`${(100*c.after[key]).toFixed(2)}%`));const delta=c.after[key]-c.before[key];row.append(make("td",key==="falseRejectCount"?`${delta>0?"+":""}${delta}`:`${delta>0?"+":""}${(100*delta).toFixed(2)} pp`));body.append(row);}table.append(body);const scroll=make("div","");scroll.className="triage-table-scroll";scroll.tabIndex=0;scroll.setAttribute("role","region");scroll.setAttribute("aria-label","Candidate metric comparison");scroll.append(table);section.append(scroll);
        const newRejects=c.changedDecisions.filter(x=>x.before.decision==="KEEP"&&x.after.decision==="REJECT");section.append(make("p",`New KEEP → REJECT decisions: ${newRejects.length}. Validation: ${c.validationStatus}.`));
        for(const w of c.warnings)section.append(make("p",`Warning: ${w}`));for(const f of c.safetyFailures)section.append(make("p",`Safety failure: ${f}`));
        const changes=details(`Every changed decision (${c.changedDecisions.length})`);
        const changeTable=make("table","");changeTable.className="admin-compact-table triage-changes-table";const ch=make("thead","");const cr=make("tr","");
        ["Employer","Title","Old","New","Human","Reason"].forEach(label=>{const cell=make("th",label);cell.setAttribute("scope","col");cr.append(cell);});ch.append(cr);changeTable.append(ch);const cb=make("tbody","");
        for(const x of c.changedDecisions){const row=make("tr","");[x.employer||x.company||"Not recorded",x.title||x.id,x.before.decision,x.after.decision,x.human?.decision||"Not reviewed",x.after.reason||x.reason||x.after.category||"See technical evidence"].forEach(value=>row.append(make("td",value)));cb.append(row);}
        changeTable.append(cb);const changeScroll=make("div","");changeScroll.className="triage-table-scroll";changeScroll.tabIndex=0;changeScroll.setAttribute("role","region");changeScroll.setAttribute("aria-label","Changed decisions");changeScroll.append(changeTable);changes.append(changeScroll);section.append(changes);
        const evidence=details("Technical Details — changed-decision evidence");evidence.append(make("pre",JSON.stringify(c.changedDecisions,null,2)));section.append(evidence);
        if(!state.disposition||changing){section.append(make("p","Choose whether to accept this candidate. Neither decision changes or deploys running rules."));const controls=make("div","");controls.className="human-review-controls";controls.append(button("Approve Candidate",()=>act("/decision",{candidateHash:c.candidateHash,decision:"approved"})),button("Reject Candidate",()=>act("/decision",{candidateHash:c.candidateHash,decision:"rejected"})));section.append(controls);}
        section.append(button("Close Candidate Details",()=>{candidateOpen=false;changing=false;draw();}));card.append(section);
      };
      if(reviewOpen){
        const host=make("section","");host.className="triage-review-panel";host.setAttribute("aria-label","Human review case");card.append(host);
        env.HumanReview?.mount(host,env,{guided:true,onSaved:report=>{state.review=report;if(report.complete){reviewOpen=false;load();}}});action.append(button("Back to Workflow",()=>{reviewOpen=false;draw();}));
      }else if(noUpdate){
        action.append(make("h4",`Ruleset ${noUpdate.rulesetVersion} is current.`),make("p","The previous maintenance cycle is complete. No additional rule update is required."),make("p","No Rule Update Needed. No action required. New cached Shadow evidence is checked automatically."));
        if(state.detection?.status==="idle")action.append(make("p",state.detection.message));
        technical.append(make("p",`Validation: ${noUpdate.validationStatus}. All ${r.reviewed} reviewed decisions match. Remaining provisional false rejects: ${noUpdate.metrics.falseRejectCount}. A new cycle requires new review evidence; this completed cycle cannot be prepared again.`));
      }else if(state.stage==="review-required"||state.stage==="review-progress"){
        action.append(make("p",r.reviewed?`${r.reviewed} of ${r.cases.length} reviewed; ${r.remaining} remaining.`:r.queueVersion.startsWith("auto-v1-")?`${r.cases.length} new Cheap Triage decisions need review before the next rule-maintenance cycle.`:`${r.cases.length} potentially unsafe decisions need your review before rules can be updated.`),button(next,()=>{reviewOpen=true;draw();},true));
      }else if(state.stage==="review-complete"){
        action.append(make("p",`${r.reviewed} of ${r.cases.length} reviewed. KEEP ${r.counts.KEEP} · REJECT ${r.counts.REJECT} · AMBIGUOUS ${r.counts.AMBIGUOUS}.`),make("p","Next, prepare instructions for Codex to build and evaluate a candidate using your decisions. Preparing the request does not run Codex or change rules."),button(next,()=>act("/prepare"),true));
      }else if(state.stage==="prompt-ready"){
        action.append(make("p","Paste this prompt into Codex, let the analysis finish, then return with candidate-result.json (CANDIDATE or NO_UPDATE_NEEDED). JSM cannot detect when Codex finishes. When you return, open Technical Details → Codex finished? Supply analysis result. Nothing changes automatically."),button(next,()=>copy(state.prompt),true));
        const handoff=details("Codex finished? Supply analysis result");handoff.append(make("p","Choose the result file to import it, or import the result Codex synced to curiosity."));
        const input=make("input","");input.type="file";input.accept=".json,application/json";input.hidden=true;input.tabIndex=-1;input.setAttribute("aria-label","Maintenance analysis result file");
        const filename=make("span","No file selected");filename.id="candidate-selected-file";filename.setAttribute("aria-live","polite");
        const picker=button("Choose File",()=>{input.value="";input.click();});picker.setAttribute("aria-describedby","candidate-selected-file");const controls=make("div","");controls.className="human-review-controls";controls.append(picker,filename,input);handoff.append(controls,button("Import Synced Result",()=>act("/import-synced")));
        input.addEventListener("change",async()=>{try{const f=input.files?.[0];if(!f)return;filename.textContent=f.name;if(f.size>2000000)throw Error("Candidate file must be below 2 MB.");const result=JSON.parse(await f.text());await act("/result",{result:result.resultType?result:{resultType:"CANDIDATE",candidate:result}});}catch(e){status.textContent=e.message;}});technical.append(handoff);
        action.append(button("View Prompt",e=>env.RuleMaintenance.open(e?.target,env,{title:"Codex Analysis Request",prepared:state.prompt})));
      }else if(state.stage==="candidate"){
        action.append(make("p","Candidate imported successfully. Review its corrections, metrics and safety warnings before accepting or rejecting it. Running rules have not changed."),button(next,()=>openCandidate(),true));
      }else if(state.stage==="approved"||state.stage==="rejected"){
        if(state.stage==="approved")summary();else action.append(make("p","Your rejection is saved. Prepare a revision request for Codex; running rules remain unchanged."));
        action.append(make("p",state.stage==="approved"?"Prepare the release prompt to hand to Codex. Preparing it does not commit, push or deploy anything.":"The new request uses your completed human review. Codex must evaluate the revision before you decide again."),button(next,()=>act(state.stage==="approved"?"/prepare-release":"/prepare"),true),button("View Candidate Details",()=>openCandidate()),button("Change Decision",()=>openCandidate(true)));
      }else if(state.stage==="release-request-ready"){
        action.append(make("p","Copy this release prompt into Codex. Codex will finalize, commit, push, validate CI and deploy after successful checks. Cheap Triage remains Shadow unless separately changed. Copying alone runs nothing."),button(next,()=>copy(state.releasePrompt),true),button("View Prompt",e=>env.RuleMaintenance.open(e?.target,env,{title:"Release Request",prepared:state.releasePrompt})),button("View Candidate Details",()=>openCandidate()),button("Change Decision",()=>openCandidate(true)));
        action.append(make("p","Release remains pending until a release receipt matches the running committed build and ruleset. JSM does not detect external Codex progress."));
      }else if(state.stage==="released"){
        action.append(make("p",`Ruleset ${state.release.rulesetVersion} is released. Validation: ${state.release.validationStatus}. No maintenance action is needed until a new review cycle.`));technical.append(make("pre",JSON.stringify(state.release,null,2)));
      }
      if(c){technical.append(make("p",`Candidate ${c.candidateVersion} · SHA-256 ${c.candidateHash}`),make("p",`Baseline ${c.baselineVersion} · SHA-256 ${c.baselineHash}`),make("p","Metrics are imported evaluation evidence. JSM validates structure and identity, not the external evaluation itself."));if(candidateOpen&&!reviewOpen)candidateDetails();}
      if(r.complete&&!reviewOpen){const completed=make("section","");completed.className="triage-completed-review";completed.append(make("p",`Human Review complete · ${r.reviewed} reviewed · ${r.remaining} remaining`),button("Revisit Human Review",()=>{reviewOpen=true;candidateOpen=false;draw();}));if(c&&!candidateOpen&&!Array.from(action.children).some(n=>n.textContent==="View Candidate Details"))completed.append(button("View Candidate Details",()=>openCandidate()));card.append(completed);}
      card.append(technical);
    }
    // Refresh detection without navigation or a scan button; never replace an in-progress review draft.
    const timer=env.setInterval?.(async()=>{if(panel.isConnected===false){env.clearInterval?.(timer);return;}if(!busy&&!reviewOpen&&!candidateOpen)await load();},60000);
    return {ready:load(),setStatus:value=>{runtime=value;updateSummary();}};
  }
  return {mount,currentStep,stages};
});
