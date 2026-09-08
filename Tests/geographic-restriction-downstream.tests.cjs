const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const root=path.resolve(process.argv[2]),pairs=JSON.parse(fs.readFileSync(process.argv[3],'utf8'));
const app=fs.readFileSync(path.join(root,'wwwroot/app.js'),'utf8').replace(/\r\n/g,'\n');
function extract(name){const start=app.indexOf(`function ${name}(`),end=app.indexOf('\n}\n',start);assert(start>=0&&end>start,name);return app.slice(start,end+3);}
const state={jobFitEnabled:true,jobFitSignals:[],jobFitConcepts:JSON.parse(fs.readFileSync(path.join(root,'JobConceptCatalog.json'),'utf8')).concepts,jobFitGroupHardConflicts:{}};
const elements={detailLocationNote:{},detailLocationNoteText:{}};
const context={state,elements,badges:[],appendJobBadge:(a,...values)=>a.push(values),JobFit:require(path.join(root,'wwwroot/job-fit.js'))};vm.createContext(context);vm.runInContext(extract('evaluateJobFit'),context);
const start=app.indexOf('  elements.detailLocationNote.hidden ='),end=app.indexOf('\n\n',start),display=app.slice(start,end);assert(start>=0&&end>start);
const cardStart=app.indexOf('  if (job.isRemoteLocationRestricted) {'),cardEnd=app.indexOf('\n  }',cardStart)+4,card=app.slice(cardStart,cardEnd);assert(cardStart>=0);
function job(p){return {isRemoteLocationRestricted:p.isRestricted,remoteLocationRestrictionCategory:p.category,remoteLocationRestrictionSnippet:p.snippet,detectedConcepts:[{conceptId:'work.remote.full'}],semanticClassificationStatus:'complete'};}
function render(j){context.job=j;context.badges=[];vm.runInContext(display+'\n'+card,context);return JSON.stringify({hidden:elements.detailLocationNote.hidden,text:elements.detailLocationNoteText.textContent,badges:context.badges});}
let displays=0,scores=0,listDetail=0;
for(const p of pairs){const before=job(p.old),after=job(p.current);assert.equal(render(after),render(before),p.id);displays++;
 for(const preference of [null,0,3,5])for(const tolerance of [null,0,5]){state.preferredWorkLocation=preference;state.travelTolerance=tolerance;const current=context.evaluateJobFit(after);assert.equal(JSON.stringify(current),JSON.stringify(context.evaluateJobFit(before)),p.id);scores++;
 const compact={...after};delete compact.remoteLocationRestrictionCategory;delete compact.remoteLocationRestrictionSnippet;assert.equal(JSON.stringify(context.evaluateJobFit(compact)),JSON.stringify(current),p.id);listDetail++;}
}
console.log(`PASS ${displays} exact actual geographic card/At a Glance render comparisons, ${scores} old/new Jobs-caller scores and ${listDetail} list/detail scores.`);
