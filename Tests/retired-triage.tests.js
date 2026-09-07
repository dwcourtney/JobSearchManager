"use strict";
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root=path.resolve(__dirname,'..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
for(const file of ['Program.cs','wwwroot/app.js','wwwroot/index.html','appsettings.json'])
  assert.doesNotMatch(read(file),/CheapTriage|CheapReject|HumanReview|cheap-triage|human-review|RuleMaintenance|rule-maintenance/,file);
for(const file of ['cheap-triage.js','cheap-triage-workflow.js','human-review.js','rule-maintenance.js']) {
  assert.ok(!fs.existsSync(path.join(root,'wwwroot',file)),file+' is still served');
  assert.ok(fs.existsSync(path.join(root,'research/cheap-triage/archive/wwwroot',file)),'Missing archived '+file);
}
assert.doesNotMatch(read('JobCatalog.cs'),/ScheduleCheapTriage|ReconcileCheapTriage|GetCheapTriage|CheapTriageShadow/);
assert.match(read('JobCatalog.cs'),/PreserveLegacyCacheFields/);
assert.match(read('JobModels.cs'),/CheapTriageObservation\? CheapTriage = null/);
assert.match(read('JobModels.cs'),/CheapTriage = null/);
assert.match(read('JobSearchManager.csproj'),/Compile Remove="CheapRejectRules.cs;CheapTriageShadow.cs/);
assert.match(read('JobSearchManager.csproj'),/Content Remove="CheapTriage\/\*\*\/\*"/);
assert.doesNotMatch(read('scripts/deploy-curiosity.sh'),/cheap-triage|HumanReview|human-review/);
for(const file of ['CheapTriage/rulesets/1.0.0.json','CheapTriage/rulesets/1.0.1.json','CheapTriage/review-queues/human-review-v1.json','CheapTriage/maintenance-trigger-policy-v1.json']) assert.ok(fs.existsSync(path.join(root,file)));
console.log('PASS retired triage: no DI/HTTP/UI/background/deployment dependencies; legacy cache and research preserved');
