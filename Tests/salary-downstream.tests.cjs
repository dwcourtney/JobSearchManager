const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const root=path.resolve(process.argv[2]),input=process.argv[3];
const app=fs.readFileSync(path.join(root,'wwwroot/app.js'),'utf8').replace(/\r\n/g,'\n');
function extract(name){const start=app.indexOf(`function ${name}(`),end=app.indexOf('\n}\n',start);assert(start>=0&&end>start,name);return app.slice(start,end+3);}
const state={minimumSalary:null,jobFitEnabled:true,jobFitSignals:[],jobFitConcepts:JSON.parse(fs.readFileSync(path.join(root,'JobConceptCatalog.json'),'utf8')).concepts,jobFitGroupHardConflicts:{}};
const context={state,JobFit:require(path.join(root,'wwwroot/job-fit.js'))};vm.createContext(context);
const threshold=app.match(/^const HEADROOM_WARNING_THRESHOLD = [^;]+;/m);assert(threshold);
vm.runInContext(threshold[0]+['formatCurrency','formatPay','calculateSalaryHeadroom','evaluateJobFit'].map(extract).join('\n'),context);
const pairs=JSON.parse(fs.readFileSync(input,'utf8'));let displays=0,scores=0;
function job(p){return {payMinimum:p.minimum,payMaximum:p.maximum,payPeriod:p.period,payParseStatus:p.parseStatus,detectedConcepts:[{conceptId:'work.remote.full'}],semanticClassificationStatus:'complete'};}
for(const p of pairs){const before=job(p.old),after=job(p.current);
 assert.equal(context.formatPay(after),context.formatPay(before),p.id);displays++;
 for(const minimum of [null,50000,80000,115000,150000]){state.minimumSalary=minimum;assert.equal(JSON.stringify(context.calculateSalaryHeadroom(after)),JSON.stringify(context.calculateSalaryHeadroom(before)),p.id);displays++;}
 for(const preference of [null,0,3,5])for(const tolerance of [null,0,5]){state.preferredWorkLocation=preference;state.travelTolerance=tolerance;assert.equal(JSON.stringify(context.evaluateJobFit(after)),JSON.stringify(context.evaluateJobFit(before)),p.id);scores++;}
}
assert.equal(context.formatPay(job({minimum:30,maximum:45,period:'hourly'})),'$30 – $45 per hour');
state.minimumSalary=115000;assert.equal(context.calculateSalaryHeadroom(job({minimum:30,maximum:45,period:'hourly'})),null);
console.log(`PASS ${displays} exact pay display/headroom comparisons and ${scores} exact actual Jobs-caller scores; hourly remains unconverted.`);
