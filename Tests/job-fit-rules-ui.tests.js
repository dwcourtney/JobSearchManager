"use strict";
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const root=path.resolve(__dirname,'..'),app=fs.readFileSync(path.join(root,'wwwroot/app.js'),'utf8'),css=fs.readFileSync(path.join(root,'wwwroot/styles.css'),'utf8');
class Node{constructor(tag){this.tag=tag;this.tagName=tag.toUpperCase();this.children=[];this.attrs={};this.handlers={};this.value='';this.classList={toggle(){}};}append(...n){this.children.push(...n);}prepend(...n){this.children.unshift(...n);}replaceChildren(...n){this.children=n;}setAttribute(k,v){this.attrs[k]=v;}addEventListener(k,v){this.handlers[k]=v;}after(){}click(){if(!this.disabled)return this.handlers.click?.();}}
const all=n=>[n,...n.children.flatMap(all)],text=n=>all(n).map(x=>x.textContent||'').join(' ');
function extract(name){const start=app.indexOf(`function ${name}(`);assert.ok(start>=0);return app.slice(start,app.indexOf('\n}\n',start)+3);}
const elements={settingsTab:new Node('button'),settingsView:new Node('section')},state={activeView:'jobs',adminRegexRulePage:0};let actions=[];
const ctx={document:{createElement:t=>new Node(t)},elements,state,window:{location:{hash:''}},formatLongDate:x=>x,applyRegexRuleAction:(...x)=>actions.push(x),startClassifierBackfill(){},evaluateRegexRules(){},reloadRegexRules(){}};
vm.createContext(ctx);vm.runInContext(['synchronizeAdminNavigation','renderAdminRegexRules','regexRuleActions'].map(extract).join('\n'),ctx);ctx.synchronizeAdminNavigation(true);
const panel=elements.adminClassifierPanel;
for(const b of all(panel).filter(x=>x.tag==='button'))assert.match(b.className,/primary-button/);
for(const c of all(panel).filter(x=>['input','select'].includes(x.tag))){assert.match(c.className,/text-control/);assert.ok(c.attrs['aria-label']);if(c.tag==='select')assert.match(c.className,/admin-evaluation-filter-select/);}
assert.match(text(panel),/Runtime summary.*Rule maintenance actions.*Browse rules/);
state.adminRegexRules=Array.from({length:55},(_,i)=>({ruleId:`rule-${String(i).padStart(2,'0')}`,conceptId:`concept.${String(i).padStart(2,'0')}`,pattern:'long-pattern-'.repeat(20),status:'active',ruleType:'include',scope:'body',provenance:'fixture',matchCountLifetime:i,matchCountSinceReview:1,timeoutCountLifetime:0}));
ctx.renderAdminRegexRules();let list=elements.adminRegexRulesList;assert.match(text(list),/55 matching rules · page 1 of 2/);
const findButton=label=>all(list).find(x=>x.tag==='button'&&x.textContent===label);
assert.equal(findButton('Previous').disabled,true);assert.equal(findButton('Next').disabled,false);
assert.equal(all(list).filter(x=>x.tag==='th').length,8);for(const th of all(list).filter(x=>x.tag==='th'))assert.equal(th.scope,'col');
const region=all(list).find(x=>x.attrs.role==='region');assert.equal(region.tabIndex,0);
const details=all(list).find(x=>x.tag==='details');const expanded=all(list).find(x=>x.className==='admin-rule-expanded-row');assert.equal(expanded.hidden,true);assert.equal(expanded.children[0].colSpan,8);
details.open=true;details.handlers.toggle();assert.equal(expanded.hidden,false);assert.equal(details.children[0].attrs['aria-expanded'],'true');assert.match(text(expanded),/long-pattern-/);
findButton('Retire').click();assert.deepEqual(actions,[['rule-00','retired']]);
details.open=false;details.handlers.toggle();assert.equal(expanded.hidden,true);
findButton('Next').click();assert.match(text(list),/page 2 of 2/);assert.equal(findButton('Next').disabled,true);assert.equal(findButton('Previous').disabled,false);findButton('Previous').click();assert.match(text(list),/page 1 of 2/);
for(const b of all(list).filter(x=>x.tag==='button'))assert.match(b.className,/primary-button/);
elements.adminRegexConceptFilter.value='concept.54';elements.adminRegexConceptFilter.handlers.input();assert.match(text(list),/1 matching rules/);assert.match(text(list),/concept.54/);
elements.adminRegexConceptFilter.value='missing';elements.adminRegexConceptFilter.handlers.input();assert.match(text(list),/0 matching rules/);assert.equal(findButton('Previous').disabled,true);
const scoped=css.slice(css.indexOf('.settings-view:has(.admin-job-fit-rules'),css.indexOf('\n@media',css.indexOf('.settings-view:has(.admin-job-fit-rules')));
assert.doesNotMatch(scoped,/#[0-9a-f]{3,8}\b|rgba?\(|hsla?\(|min-width:\s*32rem|position:\s*absolute/i);assert.match(scoped,/font-size: var\(--font-size-ui\)/);assert.match(scoped,/overflow: auto/);assert.match(scoped,/color-focus-ring/);
console.log('PASS Job Fit Rules themed controls/labels, hierarchy, 50-row paging/filtering, full-width accessible expansion, unchanged lifecycle action, responsive token styling');
