import pathlib,json,hashlib,subprocess,collections,time
repo=pathlib.Path(r'D:\Computer Everything\Programming\CS\JobSearchManager'); prior=pathlib.Path('reviewed-rules-candidate'); out=pathlib.Path('step3-b2c269').resolve()
bundle=repo/'docs/evaluations/human-review-snapshots/b2c269cc766fe41cd82ed594f8bcbff04f940103361dbff172b7a480f051db0c'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
h=json.loads((bundle/'human-review.json').read_text()); old=json.loads((prior/'human-snapshot.json').read_text()); assert {x['posting']['stableJobId']:x['human'] for x in h['decisions']}=={x['posting']['stableJobId']:x['human'] for x in old['decisions']},'Human revisions changed'
m=json.loads((repo/'docs/evaluations/human-reviewed-1.0.1/manifest.json').read_text())
for name in ['live-inputs.jsonl','current-inputs.jsonl','current-parsons.json']:assert sha(prior/name)==m['sources'][name]
assert sha(repo/'CheapTriage/rulesets/1.0.1.json')==m['candidateSha256']
a=json.loads((repo/'CheapTriage/rulesets/1.0.0.json').read_text());b=json.loads((repo/'CheapTriage/rulesets/1.0.1.json').read_text())
for key in ['predicates','rules']:
 new={r['id']:r for r in b[key]}
 assert all(new[r['id']]==r for r in a[key]),'Existing rule/predicate changed'
added=[r for r in b['rules'] if r['id'] not in {x['id'] for x in a['rules']}];assert len(added)==7 and all(x['decision']=='KEEP' for x in added)
dll=repo/'bin/Release/net10.0/JobSearchManager.dll'
records={};changes=[]
for cohort,file in [('historical-live','live-inputs.jsonl'),('snapshot-cache','current-inputs.jsonl')]:
 input_path=(out/file) if cohort=='snapshot-cache' else (prior/file).resolve(); rows=[json.loads(l) for l in input_path.read_text().splitlines()]; runs={}
 for name,version in [('baseline','1.0.0'),('candidate','1.0.1')]:
  start=time.perf_counter();raw=subprocess.check_output(['dotnet',str(dll),'--cheap-triage','evaluate',str(repo/f'CheapTriage/rulesets/{version}.json'),str(input_path)],cwd=repo)
  (out/f'{cohort}-{name}.jsonl').write_bytes(raw);d={x['id']:x['result'] for x in map(json.loads,raw.splitlines())};runs[name]=d
  print(cohort,name,len(d),collections.Counter(x['decision'] for x in d.values()),'failOpen',sum(x['failOpen'] for x in d.values()),round(time.perf_counter()-start,2))
 records[cohort]={name:dict(n=len(d),counts=dict(collections.Counter(x['decision'] for x in d.values())),failOpen=sum(x['failOpen'] for x in d.values())) for name,d in runs.items()}
 for row in rows:
  before=runs['baseline'][row['id']];after=runs['candidate'][row['id']]
  if before['decision']!=after['decision']:
   assert before['decision']=='REJECT' and after['decision']=='KEEP'
   changes.append(dict(dataset=cohort,id=row['id'],title=row['title'],employer=row['employer'],before=before,after=after))
 if cohort=='historical-live':
  human=[]
  for x in h['decisions']:
   id=x['posting']['stableJobId'];result=runs['candidate'][id];assert result['decision']==x['human']['decision'],id
   human.append(dict(id=id,title=x['posting']['title'],human=x['human'],baseline=runs['baseline'][id],candidate=result))
  (out/'human-comparison.json').write_text(json.dumps(human,indent=2))
  observations=[json.loads(l) for l in (repo/'Tests/cheap_reject_evaluation/live_shadow_v1/observations.jsonl').read_text().splitlines()]
  unique={}
  for x in sorted(observations,key=lambda o:(o['sourceRefreshed'],o['descriptionBacked'],o['cache']),reverse=True):unique.setdefault(x['stableId'],x)
  for x in unique.values():assert runs['baseline'][x['stableId']]['postingFingerprint']==x['observation']['result']['postingFingerprint']
  records['historical-input-fingerprint-mismatches']=0
 else:
  snap=json.loads((bundle/'live-shadow.json').read_text())['liveShadow'];assert snap['totalJobs']==len(rows)
  for x in snap['rejectedJobs']:assert runs['baseline'][x['job']['stableId']]['postingFingerprint']==x['observation']['result']['postingFingerprint']
  print('All',len(snap['rejectedJobs']),'exported Shadow REJECT input fingerprints match cached replay')
(out/'live-changes.json').write_text(json.dumps(changes,indent=2));(out/'live-summary.json').write_text(json.dumps(records,indent=2))
print('Human exact revisions match earlier candidate; all 29 outcomes match human decisions; existing rules/predicates unchanged')
