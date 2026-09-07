import pathlib,json,tarfile,hashlib,gzip,base64
root=pathlib.Path(r'D:\Computer Everything\Programming\CS\JobSearchManager'); out=pathlib.Path('reviewed-rules-candidate')
snapshot=json.loads((out/'human-snapshot.json').read_text()); hm={x['posting']['stableJobId']:x['human']['decision'] for x in snapshot['decisions']}
manifest=json.loads((root/'Tests/cheap_reject_evaluation/live_shadow_v1/manifest.json').read_text());
for f,h in manifest['artifactSha256'].items():
 p=root/'Tests/cheap_reject_evaluation/live_shadow_v1'/f
 if p.exists():assert hashlib.sha256(p.read_bytes()).hexdigest()==h,f
obs=[json.loads(l) for l in (root/'Tests/cheap_reject_evaluation/live_shadow_v1/observations.jsonl').read_text().splitlines()]
with tarfile.open(out/'jsm-rule-audit-inputs.tar.gz') as t: caches={m.name[:-5]:json.loads(t.extractfile(m).read()) for m in t.getmembers()}
for c in manifest['caches']:
 with tarfile.open(out/'jsm-rule-audit-inputs.tar.gz') as t: assert hashlib.sha256(t.extractfile(c['cache']+'.json').read()).hexdigest()==c['sourceSha256'],c['cache']
unique={}
for o in sorted(obs,key=lambda o:(o['sourceRefreshed'],o['descriptionBacked'],o['cache']),reverse=True): unique.setdefault(o['stableId'],o)
obs=list(unique.values())
rows=[]
for o in obs:
 j=next(j for j in caches[o['cache']]['jobs'] if j['stableId']==o['stableId'])
 body=j.get('descriptionHtml') or ''
 if not body and j.get('compressedDescriptionHtml'): body=gzip.decompress(base64.b64decode(j['compressedDescriptionHtml'])).decode()
 rows.append(dict(id=o['stableId'],employer=o['employer'],title=j['title'],body=body,label=hm.get(o['stableId']),cache=o['cache'],historicalDecision=o['observation']['decision']))
# observations artifact is the frozen deduplicated 2205-job selection.
assert len(rows)==2205 and len({r['id'] for r in rows})==2205
(out/'live-inputs.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in rows))
r=json.loads((root/'CheapTriage/rulesets/1.0.0.json').read_text());r['rulesetVersion']='1.0.1';r['name']='human-reviewed-technology-safety';r['description']='Candidate only: corroborated Technology KEEP guards from human review; legacy rejects and active configuration unchanged.'
def guard(name,title,objects,duties):
 ids=[]
 for suffix,scope,pattern in [('title','title',title),('object','body',objects),('duty','body',duties)]:
  pid=name+'-'+suffix;ids.append(pid);r['predicates'].append(dict(id=pid,scope=scope,pattern=pattern))
 r['predicates'].append(dict(id=name+'-confirmed',all=ids))
 r['rules'].insert(7,dict(id='keep-'+name,order=7+sum(x['id'].startswith('keep-reviewed-') for x in r['rules']),decision='KEEP',category='technology-safety',reason='KEEP: corroborated '+name.removeprefix('reviewed-').replace('-',' ')+' duties',when=name+'-confirmed'))
guard('reviewed-electronic-design',r'\b(?:ecad|pcb|electronic(?:s)?)\b',r'\b(?:pcb|printed circuit board|ecad)\b',r'\b(?:layout|routing|schematic|component placement)\b')
guard('reviewed-test-systems',r'\b(?:test|evaluation|laboratory)\b',r'\b(?:electrical engineering drawings|electrical schematics|sensors and instrumentation)\b',r'\b(?:systems integration|test.system design|designs systems)\b')
guard('reviewed-application-integration',r'\b(?:project manager|support|analyst|administrator)\b',r'\b(?:file transfer|data flows|system dependencies)\b',r'\b(?:integration (?:issues|error)|configuration improvements|triage issues)\b')
guard('reviewed-software-product',r'\bproduct manager\b',r'\b(?:software|ai native|saas)\b',r'\b(?:customer deployment|product strategy|implementation capacity)\b')
guard('reviewed-solution-sales',r'\bsolution sales\b',r'\b(?:ai.platform|software.platform|saas|workflow automation)\b',r'\b(?:demonstrations|technical discovery|technical questions)\b')
guard('reviewed-telecom-infrastructure',r'\b(?:osp|outside.plant|telecommunications)\b',r'\btelecommunications infrastructure\b',r'\b(?:design programs|design management|infrastructure design)\b')
guard('reviewed-it-architecture',r'\b(?:digital technology|program architecture)\b',r'\b(?:it and engineering|information technology|program architects)\b',r'\b(?:requirements analysis|agile workflows|proposal response)\b')
r['rules'].sort(key=lambda x:x['order']);(out/'1.0.1.json').write_text(json.dumps(r,indent=2)+'\n')
print('Verified immutable audit source/artifact hashes, 2205 unique inputs; prepared seven additive KEEP guards')
