'use strict';
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const root=process.argv[2],input=process.argv[3];
const app=fs.readFileSync(path.join(root,'wwwroot/app.js'),'utf8').replace(/\r\n/g,'\n');
const start=app.indexOf('function evaluateJobFit('),end=app.indexOf('\n}\n',start);assert(start>=0&&end>start);
const JobFit=require(path.resolve(root,'wwwroot/job-fit.js'));
const catalog=JSON.parse(fs.readFileSync(path.join(root,'JobConceptCatalog.json'),'utf8'));
const state={jobFitEnabled:true,jobFitSignals:[],jobFitConcepts:catalog.concepts,jobFitGroupHardConflicts:[]};
const context={JobFit,state};vm.createContext(context);vm.runInContext(app.slice(start,end+3),context);
const pairs=JSON.parse(fs.readFileSync(input,'utf8'));
const signals=(ids,preference)=>ids.map(conceptId=>({conceptId,preference}));
const profiles=[[],signals(['technical.software-development'],'ideal'),signals(['work.deployment','work.extended-away-assignment'],'negative'),[...signals(['work.deployment'],'hardConflict'),...signals(['role.software-engineering'],'ideal')],signals(['technical.artificial-intelligence','technical.machine-learning','technical.nlp','technical.large-language-models'],'ideal')];
let comparisons=0,hardCaps=0,dimensionCaps=0,pending=0;
for(const preferenceSignals of profiles)for(const groups of [[],['software-development']])for(const preferred of [null,0,3,5])for(const travel of [null,0,5]){
 Object.assign(state,{jobFitSignals:preferenceSignals,jobFitGroupHardConflicts:groups,preferredWorkLocation:preferred,travelTolerance:travel});
 for(const pair of pairs){
  const before=context.evaluateJobFit({detectedConcepts:pair.old,semanticClassificationStatus:'complete'});
  const after=context.evaluateJobFit({detectedConcepts:pair.current,semanticClassificationStatus:'complete'});
  assert.equal(JSON.stringify(after),JSON.stringify(before),pair.id);comparisons++;
  if(after?.hardConflictCap?.applied)hardCaps++;
  if(after?.dimensionBreakdown?.some(d=>d.capped))dimensionCaps++;
 }
}
Object.assign(state,{jobFitSignals:[],jobFitGroupHardConflicts:[]});delete state.preferredWorkLocation;delete state.travelTolerance;
for(const pair of pairs){
 for(const status of [undefined,'pending','failed','incomplete']){
  assert.equal(context.evaluateJobFit({detectedConcepts:pair.old,semanticClassificationStatus:status}),null);
  assert.equal(context.evaluateJobFit({detectedConcepts:pair.current,semanticClassificationStatus:status}),null);pending++;
 }
 const old={detectedConcepts:pair.old,semanticClassificationStatus:'complete'},current={detectedConcepts:pair.current,semanticClassificationStatus:'complete'};
 assert.equal(JSON.stringify(context.evaluateJobFit(old)),JSON.stringify(context.evaluateJobFit(current)));
 state.jobFitEnabled=false;assert.equal(context.evaluateJobFit(old),null);assert.equal(context.evaluateJobFit(current),null);state.jobFitEnabled=true;
}
function score(ids){return context.evaluateJobFit({detectedConcepts:ids.map(conceptId=>({conceptId})),semanticClassificationStatus:'complete'});}
for(const [id,preferred,expected] of [['work.remote.full',0,6],['work.onsite',0,3],['work.onsite',5,6]]){state.preferredWorkLocation=preferred;state.travelTolerance=null;assert.equal(score([id]).score,expected);}
state.preferredWorkLocation=null;
const travelId=catalog.concepts.find(c=>c.travelLevel===5).id;
for(const [travel,expected] of [[0,2],[5,5]]){state.travelTolerance=travel;assert.equal(score([travelId]).score,expected);}
state.travelTolerance=null;state.jobFitSignals=signals(['work.deployment'],'hardConflict');assert.equal(score(['work.deployment']).hardConflictCap.applied,true);
state.jobFitSignals=signals(['work.deployment','work.extended-away-assignment'],'negative');assert(score(['work.deployment','work.extended-away-assignment']).dimensionBreakdown.some(d=>d.capped));
console.log(JSON.stringify({result:'PASS',records:pairs.length,scoreAndExplanationComparisons:comparisons,hardCapComparisons:hardCaps,dimensionCapComparisons:dimensionCaps,pendingChecks:pending,missingDisabledRemoteTravel:true}));
