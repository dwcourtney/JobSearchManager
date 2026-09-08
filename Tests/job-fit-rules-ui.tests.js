"use strict";
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const root=path.resolve(__dirname,'..'),app=fs.readFileSync(path.join(root,'wwwroot/app.js'),'utf8'),css=fs.readFileSync(path.join(root,'wwwroot/styles.css'),'utf8');
class Node{constructor(tag){this.tag=tag;this.tagName=tag.toUpperCase();this.children=[];this.attrs={};this.handlers={};this.value='';this.classList={toggle(){}};}append(...n){this.children.push(...n);}prepend(...n){this.children.unshift(...n);}replaceChildren(...n){this.children=n;}setAttribute(k,v){this.attrs[k]=v;}addEventListener(k,v){this.handlers[k]=v;}after(){}click(){if(!this.disabled)return this.handlers.click?.();}}
const all=n=>[n,...n.children.flatMap(all)],text=n=>all(n).map(x=>x.textContent||'').join(' ');
function extract(name){const start=app.indexOf(`function ${name}(`);assert.ok(start>=0);return app.slice(start,app.indexOf('\n}\n',start)+3);}
const elements={settingsTab:new Node('button'),settingsView:new Node('section')},state={activeView:'jobs',adminRegexRulePage:0};let actions=[];
const ctx={document:{createElement:t=>new Node(t)},elements,state,window:{location:{hash:''}},formatLongDate:x=>x,applyRegexRuleAction:(...x)=>actions.push(x),startClassifierBackfill(){},evaluateRegexRules(){},reloadRegexRules(){}};
vm.createContext(ctx);vm.runInContext(['synchronizeAdminNavigation','renderAdminRegexRules'].map(extract).join('\n'),ctx);ctx.synchronizeAdminNavigation(true);
const panel=elements.adminClassifierPanel;
for(const b of all(panel).filter(x=>x.tag==='button'))assert.match(b.className,/primary-button/);
for(const c of all(panel).filter(x=>['input','select'].includes(x.tag))){assert.match(c.className,/text-control/);assert.ok(c.attrs['aria-label']);if(c.tag==='select')assert.match(c.className,/admin-evaluation-filter-select/);}
assert.match(text(panel),/Runtime summary.*Browse rules/);
state.adminRegexRules=Array.from({length:55},(_,i)=>({ruleId:`rule-${String(i).padStart(2,'0')}`,conceptId:`concept.${String(i).padStart(2,'0')}`,pattern:'long-pattern-'.repeat(20),kind:'positive-evidence',executionOrder:i,scope:'both',provenance:'fixture',matchCountLifetime:i,matchCountSinceReview:1,timeoutCountLifetime:0}));
ctx.renderAdminRegexRules();let list=elements.adminRegexRulesList;assert.match(text(list),/55 matching rules · page 1 of 2/);
const findButton=label=>all(list).find(x=>x.tag==='button'&&x.textContent===label);
assert.equal(findButton('Previous').disabled,true);assert.equal(findButton('Next').disabled,false);
assert.equal(all(list).filter(x=>x.tag==='th').length,8);for(const th of all(list).filter(x=>x.tag==='th'))assert.equal(th.scope,'col');
const region=all(list).find(x=>x.attrs.role==='region');assert.equal(region.tabIndex,0);
const details=all(list).find(x=>x.tag==='details');const expanded=all(list).find(x=>x.className==='admin-rule-expanded-row');assert.equal(expanded.hidden,true);assert.equal(expanded.children[0].colSpan,8);
details.open=true;details.handlers.toggle();assert.equal(expanded.hidden,false);assert.equal(details.children[0].attrs['aria-expanded'],'true');assert.match(text(expanded),/long-pattern-/);
assert.equal(findButton('Retire'),undefined);assert.deepEqual(actions,[]);
assert.doesNotMatch(text(panel),/Activate|Retire|Apply Current|Import|Create rule|Review due/);
details.open=false;details.handlers.toggle();assert.equal(expanded.hidden,true);
findButton('Next').click();assert.match(text(list),/page 2 of 2/);assert.equal(findButton('Next').disabled,true);assert.equal(findButton('Previous').disabled,false);findButton('Previous').click();assert.match(text(list),/page 1 of 2/);
for(const b of all(list).filter(x=>x.tag==='button'))assert.match(b.className,/primary-button/);
elements.adminRegexConceptFilter.value='concept.54';elements.adminRegexConceptFilter.handlers.input();assert.match(text(list),/1 matching rules/);assert.match(text(list),/concept.54/);
elements.adminRegexConceptFilter.value='missing';elements.adminRegexConceptFilter.handlers.input();assert.match(text(list),/0 matching rules/);assert.equal(findButton('Previous').disabled,true);
const scoped=css.slice(css.indexOf('.settings-view:has(.admin-job-fit-rules'),css.indexOf('\n@media',css.indexOf('.settings-view:has(.admin-job-fit-rules')));
assert.doesNotMatch(scoped,/#[0-9a-f]{3,8}\b|rgba?\(|hsla?\(|min-width:\s*32rem|position:\s*absolute/i);assert.match(scoped,/font-size: var\(--font-size-ui\)/);assert.match(scoped,/overflow: auto/);assert.match(scoped,/color-focus-ring/);
console.log('PASS Job Fit Rules themed controls/labels, hierarchy, 50-row paging/filtering, full-width accessible expansion, read-only authority, responsive token styling');

vm.runInContext(['renderConceptEvaluationCard','renderEvaluationMetrics','renderHoldoutConceptTable','formatMetric','formatPercent'].map(extract).join('\n'),ctx);
const index=JSON.parse(fs.readFileSync(path.join(root,'evaluation/concept-detection/index-v1.json'),'utf8'));
for(const entry of index.reports){
 const artifact=JSON.parse(fs.readFileSync(path.join(root,'evaluation/concept-detection',entry.file),'utf8'));
 for(const status of ['CURRENT','STALE','HISTORICAL']){
  const card=ctx.renderConceptEvaluationCard({role:entry.role,status,artifact},entry.role);
  assert.ok(text(card).includes(status));assert.ok(text(card).includes(artifact.authority.pipelineFingerprint));
  assert.equal(all(card).filter(n=>n.tag==='button').length,0,'Reports must have no execution/mutation controls');
  if(entry.role==='holdout')assert.match(text(card),/Machine-reference \/ AI-adjudicated labels, not human ground truth/);
 }
}
console.log('PASS immutable evaluation cards show server-verified CURRENT/STALE/HISTORICAL identities and no mutation controls');

// Exercise actual renderers with immutable reports and native disclosure visibility semantics.
vm.runInContext(['renderEvaluationNavigation','renderCuratedEvaluationCard','renderHoldoutEvaluationCard'].map(extract).join('\n'),ctx);
const reports=index.reports.map(entry=>({role:entry.role,status:'CURRENT',artifact:JSON.parse(fs.readFileSync(path.join(root,'evaluation/concept-detection',entry.file),'utf8'))}));
const beforeReports=JSON.stringify(reports);
const references=ctx.renderEvaluationNavigation({reports});
const visible=n=>[n,...(n.tag==='details'&&!n.open?n.children.slice(0,1):n.children).flatMap(visible)];
const visibleText=n=>visible(n).map(x=>x.textContent||'').join(' ');
assert.match(text(elements.adminEvaluationPanel),/Production accuracy.*Not yet measured.*No independently human-labeled validation corpus is currently available/);
assert.match(text(elements.adminEvaluationPanel),/do not establish real-world production accuracy/);
assert.equal(references.tag,'details');assert.ok(!references.open);
assert.equal(visibleText(references).trim(),'Historical / Regression References');
references.open=true;
assert.match(visibleText(references),/not measures of production accuracy/);
assert.match(visibleText(references),/Curated regression.*148 postings · 1,740 labeled decisions.*Macro F1: 0.894598 · Micro F1: 0.915771/);
assert.match(visibleText(references),/Frozen machine-reference holdout.*Machine-reference \/ AI-adjudicated labels, not human ground truth.*200 postings · 16,903 eligible decisions · 97 unresolved.*Macro F1: 0.454672 · Micro F1: 0.359490/);
const technical=all(references).filter(n=>n.className==='admin-evaluation-technical');assert.equal(technical.length,2);
for(const [i,details] of technical.entries()){
 assert.ok(!details.open);assert.equal(details.children[0].textContent,'Technical details');
 const report=reports[i].artifact;
 for(const value of [report.datasetFingerprint,report.referenceFingerprint,report.authority.pipelineFingerprint,report.authority.byteHash,report.sourceIdentity,report.metricImplementationHash,report.runId,report.evaluatedUtc]){
  assert.ok(!visibleText(references).includes(value),'Technical identity must be hidden until requested');
  assert.ok(text(details).includes(value),'Technical identity must remain accessible');
 }
 details.open=true;assert.ok(visibleText(details).includes(report.authority.pipelineFingerprint));details.open=false;
}
assert.equal(JSON.stringify(reports),beforeReports,'Presentation must not mutate evaluation data');
assert.doesNotMatch(visibleText(references),/Macro Precision|Micro Recall|CURATED REGRESSION BENCHMARK|FROZEN CODEX-REFERENCE HOLDOUT/);
assert.equal(all(references).filter(n=>['canvas','svg'].includes(n.tag)).length,0,'Do not invent confidence or PR curves');
console.log('PASS unmeasured production accuracy, collapsed historical references, compact immutable summaries, nested technical disclosures and honest machine-reference wording');

vm.runInContext(extract('renderConceptRuntimeSummary'),ctx);
const runtimeIdentity={authority:'json-regex-v1',version:'1.0.0',taxonomyIdentity:{version:9,sha256:'a'.repeat(64)},byteHash:'b'.repeat(64),schemaHash:'c'.repeat(64),engineContract:{id:'jsm-concept-matching',version:1},engineContractHash:'d'.repeat(64),policyHash:'e'.repeat(64),pipelineFingerprint:'0bc0baf2'+'f'.repeat(50)+'cad76b',regexPolicy:{timeoutMilliseconds:100},factualDependencies:{remoteWork:{version:'1.0.0',analysisVersion:3,hash:'1'.repeat(64)},extendedLocation:{version:'1.0.0',analysisVersion:4,hash:'2'.repeat(64)}}};
const overview={identity:runtimeIdentity,validation:'validated-before-listening',totalRules:288,totalConcepts:85,countsByKind:{'positive-evidence':224,'title-evidence':26,exclusion:7,'required-context':6,'extended-location-signal':15,'remote-signal':9,'remote-designation':1},rules:[]};
const runtime=ctx.renderConceptRuntimeSummary(overview,{current:277,total:277,pending:0});
const mainText=visibleText(runtime);
for(const expected of ['Validated before startup','288 / 85','277 / 277 current','v9','v1.0.0','jsm-concept-matching v1','100 ms bounded matching','Remote Work dependency v1.0.0 / analysis 3','Extended Location dependency v1.0.0 / analysis 4','Positive evidence 224','Title evidence 26','Exclusions 7','Required context 6','Extended-location signals 15','Remote signals 9','Remote designation 1','0bc0baf2...cad76b'])assert.ok(mainText.includes(expected),expected);
assert.doesNotMatch(mainText,/[{}]|"remoteWork"|"positive-evidence"|[a-f0-9]{64}/);
const runtimeDetails=all(runtime).find(n=>n.tag==='details');assert.ok(!runtimeDetails.open);assert.equal(runtimeDetails.children[0].textContent,'Technical details');
runtimeDetails.open=true;
for(const hash of [runtimeIdentity.pipelineFingerprint,runtimeIdentity.taxonomyIdentity.sha256,runtimeIdentity.byteHash,runtimeIdentity.schemaHash,runtimeIdentity.engineContractHash,runtimeIdentity.policyHash,runtimeIdentity.factualDependencies.remoteWork.hash,runtimeIdentity.factualDependencies.extendedLocation.hash])assert.ok(visibleText(runtimeDetails).includes(hash));
assert.match(visibleText(runtimeDetails),/Remote Work analysis version 3.*Extended Location analysis version 4/);
assert.match(text(panel),/Cache reconciliation|Runtime summary/);assert.match(text(panel),/Reclassify stale cache.*Browse rules/);
vm.runInContext('async '+extract('loadClassifierStatus'),ctx);
(async()=>{
 let cache={current:277,total:277,pending:0,running:false};ctx.fetch=async url=>({ok:true,json:async()=>url.endsWith('concept-detection')?overview:cache});
 await ctx.loadClassifierStatus(true);assert.equal(elements.adminClassifierBackfill.disabled,true);assert.match(text(elements.adminClassifierStatus),/Cache reconciliation idle/);
 cache={current:276,total:277,pending:1,running:false};await ctx.loadClassifierStatus(true);assert.equal(elements.adminClassifierBackfill.disabled,false);
 cache.running=true;await ctx.loadClassifierStatus(true);assert.equal(elements.adminClassifierBackfill.disabled,true);assert.match(text(elements.adminClassifierStatus),/Cache reclassification running/);
 console.log('PASS human-readable runtime versions/policy/dependencies/counts, collapsed identities and unchanged cache controls');
})().catch(error=>{console.error(error);process.exitCode=1;});
