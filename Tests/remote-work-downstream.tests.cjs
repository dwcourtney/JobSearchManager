const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const root=process.argv[2],input=process.argv[3];
const app=fs.readFileSync(path.join(root,'wwwroot/app.js'),'utf8').replace(/\r\n/g,'\n');
const start=app.indexOf('function evaluateJobFit('),end=app.indexOf('\n}\n',start);assert(start>=0&&end>start);
const JobFit=require(path.resolve(root,'wwwroot/job-fit.js'));
const catalog=JSON.parse(fs.readFileSync(path.join(root,'JobConceptCatalog.json'),'utf8'));
const state={jobFitEnabled:true,jobFitSignals:[],jobFitConcepts:catalog.concepts,jobFitGroupHardConflicts:{}};
const context={JobFit,state};vm.createContext(context);vm.runInContext(app.slice(start,end+3),context);
const pairs=JSON.parse(fs.readFileSync(input,'utf8'));let count=0;
for(const pair of pairs) for(const preference of [null,0,3,5]) for(const tolerance of [null,0,5]) {
 state.preferredWorkLocation=preference;state.travelTolerance=tolerance;
 const before=context.evaluateJobFit({detectedConcepts:pair.old,semanticClassificationStatus:'complete'});
 const after=context.evaluateJobFit({detectedConcepts:pair.current,semanticClassificationStatus:'complete'});
 assert.equal(JSON.stringify(after),JSON.stringify(before),pair.id);count++;
}
for(const [id,preference,expected] of [['work.remote.full',0,6],['work.onsite',0,3],['work.onsite',5,6],['work.hybrid',0,3],['work.hybrid',3,6]]) {
 assert(catalog.concepts.some(c=>c.id===id),id);state.preferredWorkLocation=preference;state.travelTolerance=null;
 const job={detectedConcepts:[{conceptId:id}],semanticClassificationStatus:'complete'};
 assert.equal(context.evaluateJobFit(job).score,expected,id);console.log(`${id} / preference ${preference}: ${expected}/10`);
 assert.equal(context.evaluateJobFit({...job,semanticClassificationStatus:'pending'}),null);
 state.preferredWorkLocation=null;assert.equal(context.evaluateJobFit(job).score,5);
 state.jobFitEnabled=false;assert.equal(context.evaluateJobFit(job),null);state.jobFitEnabled=true;
}
console.log(`PASS ${count} exact old/new downstream scores through actual Jobs caller; controlled remote/onsite/hybrid/null/disabled/TBD passed.`);
