import json,pathlib,subprocess,sys
root=pathlib.Path(r'D:\Computer Everything\Programming\CS\JobSearchManager');out=pathlib.Path('reviewed-rules-candidate').resolve();rows=[json.loads(l) for l in (out/'live-inputs.jsonl').read_text().splitlines()]
for name,rules in [('baseline',root/'CheapTriage/rulesets/1.0.0.json'),('candidate',out/'1.0.1.json')]:
 p=subprocess.run(['dotnet',str(root/'bin/Release/net10.0/JobSearchManager.dll'),'--cheap-triage','evaluate',str(rules),str(out/'live-inputs.jsonl')],cwd=root,capture_output=True,text=True,encoding='utf-8',check=True);(out/(name+'-live.jsonl')).write_text(p.stdout)
 d={x['id']:x['result'] for x in map(json.loads,p.stdout.splitlines())};print(name,len(d),sum(x['decision']=='REJECT' for x in d.values()),'failOpen',sum(x['failOpen'] for x in d.values()))
 if name=='baseline':base=d;print('historical mismatch',[(r['id'],r['historicalDecision'],d[r['id']]['decision']) for r in rows if r['historicalDecision']!=d[r['id']]['decision']])
 else:
  changed=[]
  for r in rows:
   if base[r['id']]['decision']!=d[r['id']]['decision']:print(r['id'],r['title'],'human',r['label'],d[r['id']]['ruleIds']);changed.append({**r,'before':base[r['id']],'after':d[r['id']]})
  print('Human still wrong',[(r['id'],r['label']) for r in rows if r['label'] and r['label']!=d[r['id']]['decision']]);(out/'live-changes.json').write_text(json.dumps(changed,indent=2))
f=json.loads((out/'frozen/comparison.json').read_text());
for name,run in f['runs'].items(): print(name,{k:{m:x[m] for m in ['n','keepRecall','falseRejectCount','rejectionCount']} for k,x in run['cohorts'].items()})
