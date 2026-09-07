import pathlib,json,gzip,base64,subprocess
p=pathlib.Path('reviewed-rules-candidate').resolve();r=pathlib.Path(r'D:\Computer Everything\Programming\CS\JobSearchManager');data=json.loads((p/'current-parsons.json').read_text());rows=[]
for j in data['jobs']:
 body=j.get('descriptionHtml') or (gzip.decompress(base64.b64decode(j['compressedDescriptionHtml'])).decode() if j.get('compressedDescriptionHtml') else '')
 rows.append(dict(id=j['stableId'],title=j['title'],body=body,employer='parsons'))
(p/'current-inputs.jsonl').write_text(''.join(json.dumps(x)+'\n' for x in rows))
for name,f in [('baseline',r/'CheapTriage/rulesets/1.0.0.json'),('candidate',p/'1.0.1.json')]:
 raw=subprocess.check_output(['dotnet',str(r/'bin/Release/net10.0/JobSearchManager.dll'),'--cheap-triage','evaluate',str(f),str(p/'current-inputs.jsonl')],cwd=r,text=True,encoding='utf-8');(p/(name+'-current.jsonl')).write_text(raw);d=[json.loads(l)['result'] for l in raw.splitlines()];print(name,len(d),sum(x['decision']=='REJECT' for x in d),sum(x['failOpen'] for x in d))
