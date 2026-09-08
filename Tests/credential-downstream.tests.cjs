const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const root=path.resolve(process.argv[2]),pairs=JSON.parse(fs.readFileSync(process.argv[3],'utf8'));
const app=fs.readFileSync(path.join(root,'wwwroot/app.js'),'utf8').replace(/\r\n/g,'\n');
function extract(name){const start=app.indexOf(`function ${name}(`),end=app.indexOf('\n}\n',start);assert(start>=0&&end>start,name);return app.slice(start,end+3);}
const state={jobFitEnabled:true,jobFitSignals:[],jobFitConcepts:JSON.parse(fs.readFileSync(path.join(root,'JobConceptCatalog.json'),'utf8')).concepts,jobFitGroupHardConflicts:{}};
const context={state,JobFit:require(path.join(root,'wwwroot/job-fit.js'))};vm.createContext(context);vm.runInContext(extract('evaluateJobFit'),context);
const credentialFit=require(path.join(root,'wwwroot/credential-fit.js'));
const profiles=[{inventoryStatus:'notConfigured'},{inventoryStatus:'none'},{inventoryStatus:'complete',heldCredentialIds:[]},{inventoryStatus:'complete',heldCredentialIds:['cissp','pmp']},{inventoryStatus:'complete',heldCredentialIds:JSON.parse(fs.readFileSync(path.join(root,'CredentialCatalog.json'),'utf8')).credentials.map(c=>c.id)}];
let assessments=0,badges=0,scores=0;
function job(p){return {credentials:p.credentials,unrecognizedCredentialMentions:p.unrecognizedMentions,unknownCredentialRequirements:p.unknownRequirements,credentialCatalogVersion:p.catalogVersion,detectedConcepts:[{conceptId:'work.remote.full'}],semanticClassificationStatus:'complete'};}
for(const p of pairs){const before=job(p.old),after=job(p.current);
 assert.deepEqual(after,before,p.id);
 for(const profile of profiles){const old=credentialFit.evaluate(before.credentials,before.unknownCredentialRequirements,profile),current=credentialFit.evaluate(after.credentials,after.unknownCredentialRequirements,profile);assert.deepEqual(current,old,p.id);assessments++;assert.deepEqual(credentialFit.jobCardBadges(current),credentialFit.jobCardBadges(old),p.id);badges++;}
 for(const preference of [null,0,3,5])for(const tolerance of [null,0,5]){state.preferredWorkLocation=preference;state.travelTolerance=tolerance;assert.equal(JSON.stringify(context.evaluateJobFit(after)),JSON.stringify(context.evaluateJobFit(before)),p.id);scores++;}
}
console.log(`PASS ${assessments} exact credential assessments, ${badges} card badges and ${scores} actual Jobs-caller score comparisons.`);
