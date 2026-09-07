import pathlib,json,csv,hashlib
repo=pathlib.Path(r'D:\Computer Everything\Programming\CS\JobSearchManager');p=pathlib.Path('step3-b2c269');f=json.loads((p/'frozen/comparison.json').read_text());live=json.loads((p/'live-changes.json').read_text());h=json.loads((p/'human-comparison.json').read_text());hm={x['id']:x['human'] for x in h};old=json.loads((repo/'docs/evaluations/human-reviewed-1.0.1/changed-decisions.json').read_text());data={}
for file in ['corpus.jsonl','cache.jsonl']:
 for l in (repo/'Tests/cheap_reject_evaluation/supervised_v2'/file).read_text(encoding='utf-8').splitlines():
  r=json.loads(l);data[r['id']]=r
rows=[]
for c in f['runs']['candidate']['changedDecisions']:
 r=data[c['id']];rows.append(dict(dataset='frozen',id=c['id'],stableId=r.get('originalStableId'),title=r['title'],employer=r['employer'],frozenLabel=r.get('label'),before=c['before'],after=c['after']))
for x in live: rows.append({**x,'stableId':x['id']})
for x in rows:x['human']=hm.get(x['stableId'])
assert len(rows)==19
assert {(x['dataset'],x['id'],x['before']['decision'],x['after']['decision']) for x in rows}=={(x['dataset'],x['id'],x['before']['decision'],x['after']['decision']) for x in old}
(p/'changed-decisions.json').write_text(json.dumps(rows,indent=2))
with (p/'changed-decisions.csv').open('w',newline='',encoding='utf-8-sig') as fh:
 w=csv.writer(fh);w.writerow(['dataset','id','stableId','employer','title','before','after','oldRuleIds','newRuleIds','humanDecision','humanNote'])
 for x in rows:w.writerow([x['dataset'],x['id'],x['stableId'],x['employer'],x['title'],x['before']['decision'],x['after']['decision'],'; '.join(x['before']['ruleIds']),'; '.join(x['after']['ruleIds']),(x['human'] or {}).get('decision','not human-labeled'),(x['human'] or {}).get('note','')])
print('All 19 changed rows match prior candidate; full evidence and human notes exported')
for x in rows:print(x['dataset'],x['id'],x['title'])
