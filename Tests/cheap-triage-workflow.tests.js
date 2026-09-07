"use strict";
const assert=require('node:assert/strict'),api=require('../wwwroot/cheap-triage-workflow.js');
class Node{constructor(tag){this.tag=tag;this.children=[];this.handlers={};this.attrs={};}append(...x){this.children.push(...x);}replaceChildren(...x){this.children=x;}setAttribute(k,v){this.attrs[k]=v;}addEventListener(k,v){this.handlers[k]=v;}click(){return this.handlers.click?.();}}
const all=n=>[n,...n.children.flatMap(all)],text=n=>all(n).map(x=>x.textContent||'').join(' '),button=(n,t)=>all(n).find(x=>x.tag==='button'&&x.textContent===t);
const review={cases:Array.from({length:29},(_,i)=>({stableJobId:String(i)})),reviewed:29,remaining:0,complete:true,counts:{KEEP:7,REJECT:22,AMBIGUOUS:0},queueVersion:'v1',queueFingerprint:'hash'};
const m={keepRecall:.995,describedKeepRecall:.99,titleOnlyKeepRecall:1,rejectionRate:.1,oldCacheRejectionRate:.13,falseRejectCount:6};const candidate={candidateVersion:'1.0.1',candidateHash:'hash',baselineVersion:'1.0.0',baselineHash:'old',humanCorrections:7,before:m,after:m,validationStatus:'PASS',warnings:['<script>data</script>'],safetyFailures:[],changedDecisions:[{id:'id',title:'Example',before:{decision:'REJECT'},after:{decision:'KEEP'}}]};
(async()=>{
let state={key:'key',revision:0,stage:'review-required',review:{...review,reviewed:0,remaining:29,complete:false}},requests=[],options;
const env={document:{createElement:t=>new Node(t)},HumanReview:{mount:(n,e,o)=>{options=o;}},RuleMaintenance:{copyPrompt:async p=>{env.copied=p;},open:async(b,e,o)=>{env.viewed=o.prepared;}},fetch:async(url,o)=>{
 if(o.method==='POST'){const b=JSON.parse(o.body);requests.push([url,b]);assert.equal(b.key,'key');state={...state,revision:state.revision+1};
 if(url.endsWith('/prepare'))state={...state,stage:'prompt-ready',candidate:null,disposition:null,prompt:{prompt:'compact safe prompt'}};
 if(url.endsWith('/candidate'))state={...state,stage:'candidate',candidate:b.candidate};
 if(url.endsWith('/decision'))state={...state,stage:b.decision,disposition:b.decision};
 if(url.endsWith('/prepare-release'))state={...state,stage:'release-request-ready',releasePrompt:{prompt:'release safely in Shadow'}};
 }return {ok:true,json:async()=>structuredClone(state)};
}};
const panel=new Node('section');await api.mount(panel,env).ready;
const primary=label=>{const bs=all(panel).filter(x=>x.attrs['data-workflow-primary']==='true');assert.equal(bs.length,label?1:0);if(label)assert.equal(bs[0].textContent,label);};
primary('Start Human Review');assert.match(text(panel),/29 potentially unsafe/);await button(panel,'Start Human Review').click();assert.equal(options.guided,true);await button(panel,'Back to Workflow').click();
state={...state,stage:'review-progress',review:{...review,reviewed:17,remaining:12,complete:false}};await button(panel,'Reload Workflow').click();primary('Continue Human Review');assert.match(text(panel),/17 of 29/);
state={...state,stage:'review-complete',review};await button(panel,'Reload Workflow').click();primary('Prepare Rule Update');await button(panel,'Prepare Rule Update').click();primary('Copy Codex Prompt');await button(panel,'Copy Codex Prompt').click();assert.equal(env.copied,'compact safe prompt');assert.match(text(panel),/cannot detect when Codex finishes/);
const input=all(panel).find(x=>x.tag==='input');assert.equal(input.hidden,true);assert.equal(input.tabIndex,-1);assert.equal(input.className,undefined);let opened=0;input.handlers.click=()=>opened++;await button(panel,'Choose File').click();assert.equal(opened,1);
input.files=[{name:'large.json',size:2000001}];const count=requests.length;await input.handlers.change();assert.equal(requests.length,count);assert.match(text(panel),/below 2 MB/);
input.files=[{name:'candidate-result.json',size:500,text:async()=>JSON.stringify(candidate)}];await input.handlers.change();primary('Review Candidate');assert.equal(button(panel,'Choose File'),undefined);assert.equal(button(panel,'Copy Codex Prompt'),undefined);assert.equal(button(panel,'Approve Candidate'),undefined);
await button(panel,'Review Candidate').click();primary('Review Candidate');assert.match(text(panel),/Before After/);assert.match(text(panel),/<script>data<\/script>/);assert.match(text(panel),/Every changed decision \(1\)/);assert.match(button(panel,'Approve Candidate').className,/confirmation-secondary-button/);
await button(panel,'Approve Candidate').click();primary('Prepare Release Request');assert.match(text(panel),/7 reviewed false rejects corrected/);assert.match(text(panel),/22 reviewed rejects preserved/);assert.match(text(panel),/Running rules have NOT changed/);
for(const stale of ['Approve Candidate','Reject Candidate','Choose File','Import Synced Result','Copy Codex Prompt'])assert.equal(button(panel,stale),undefined,stale);
await button(panel,'View Candidate Details').click();primary('Prepare Release Request');assert.equal(button(panel,'Approve Candidate'),undefined);
await button(panel,'Change Decision').click();await button(panel,'Reject Candidate').click();primary('Prepare Revision Request');assert.equal(button(panel,'Approve Candidate'),undefined);
await button(panel,'Change Decision').click();await button(panel,'Approve Candidate').click();await button(panel,'Prepare Release Request').click();primary('Copy Release Prompt');await button(panel,'Copy Release Prompt').click();assert.equal(env.copied,'release safely in Shadow');assert.equal(button(panel,'Prepare Release Request'),undefined);assert.equal(button(panel,'Approve Candidate'),undefined);
state={...state,stage:'released',release:{rulesetVersion:'1.0.1',candidateHash:'hash',deploymentIdentity:'commit',validationStatus:'PASS'}};await button(panel,'Reload Workflow').click();primary(null);assert.match(text(panel),/Release Complete/);assert.equal(button(panel,'Change Decision'),undefined);
for(const n of all(panel).filter(x=>x.tag==='details'))assert.ok(!n.open,'Diagnostics collapsed by default');for(const n of all(panel).filter(x=>x.tag==='button'))assert.match(n.className,/primary-button/);
assert.ok(requests.every(([url])=>!url.includes('/deploy')));assert.equal(Object.keys(api.stages).length,9);
console.log('PASS all nine single-next-action states, import hiding, disposition, revision/release handoff, safe themed rendering and no deployment');
})().catch(e=>{console.error(e);process.exitCode=1;});
