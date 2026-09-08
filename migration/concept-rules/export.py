"""Verify a Phase 1 archive and export an offline oracle. No production connection."""
import argparse, collections, hashlib, json, pathlib, sqlite3

FIELDS=('RuleId','ConceptId','Pattern','Scope','RuleType','ContextGroupId')
def sha(data): return hashlib.sha256(data).hexdigest()
def load(p): return json.loads(p.read_bytes())
def dump(p,v): p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
def verify(root):
    manifest=load(root/'manifest.json')
    assert sha((root/'manifest.json').read_bytes())==(root/'manifest.sha256').read_text().split()[0], 'manifest hash'
    for f in manifest['files']:
        p=(root/f['path']).resolve(); assert p.is_relative_to(root.resolve()), 'unsafe manifest path'
        assert sha(p.read_bytes())==f['sha256'], f['path']
    assert sha((root/'authority.db').read_bytes())==manifest['archiveDatabaseSha256']
    with sqlite3.connect((root/'authority.db').resolve().as_uri()+'?mode=ro&immutable=1',uri=True) as db:
        db.row_factory=sqlite3.Row
        assert [r[0] for r in db.execute('PRAGMA integrity_check')]==['ok']
        assert not list(db.execute('PRAGMA foreign_key_check'))
        for table,count in manifest['tableCounts'].items():
            rows=[dict(r) for r in db.execute('SELECT * FROM "'+table+'" ORDER BY rowid')]
            assert len(rows)==count and rows==load(root/'history'/(table+'.json')),table
    return manifest
def seed_rules(doc):
    rules=[]
    def add(cid,pattern,scope,kind,group=None):
        rid='legacy-'+sha('\n'.join([cid,pattern,scope,kind,group or '']).encode())[:24]
        rules.append(dict(zip(FIELDS,[rid,cid,pattern,scope,kind,group])))
    for c in doc['concepts']:
        for field,kind,scope in [('evidencePatterns','positive-evidence','both'),('titleEvidencePatterns','title-evidence','title'),('titleExclusionPatterns','exclusion','title'),('remoteSignalCategories','remote-signal','both'),('extendedLocationCategories','extended-location-signal','both')]:
            for pattern in c.get(field,[]) or []: add(c['id'],pattern,scope,kind)
        for i,group in enumerate(c.get('contextRules',[]) or []):
            for pattern in group['requiredPatterns']: add(c['id'],pattern,'both','required-context',f"legacy:{c['id']}:context:{i+1}")
        if c.get('remoteDesignation'): add(c['id'],'remote-designation','both','remote-designation')
    return rules
def compare(seed,live):
    a={r['RuleId']:r for r in seed};b={r['RuleId']:{k:r[k] for k in FIELDS} for r in live}
    assert len(a)==len(seed) and len(b)==len(live),'duplicate IDs'
    result=dict(seedRules=len(a),effectiveRules=len(b),seedConcepts=len({r['ConceptId'] for r in seed}),effectiveConcepts=len({r['ConceptId'] for r in live}),added=sorted(b.keys()-a.keys()),missing=sorted(a.keys()-b.keys()),changed=[k for k in sorted(a.keys()&b.keys()) if a[k]!=b[k]])
    if result['added'] or result['missing'] or result['changed'] or len(a)!=288 or result['effectiveConcepts']!=85: raise ValueError(json.dumps(result))
    return result
def export(root,out):
    m=verify(root)
    rows=sorted((r for r in load(root/'history/SemanticRules.json') if r['Status'] in ('active','review-due')),key=lambda r:(r['ConceptId'],r['RuleType'],r['RuleId']))
    parity=compare(seed_rules(load(root/'LegacyJobConceptRules.json')),rows) # STOP before producing oracle on any difference.
    rules=[]
    for index,r in enumerate(rows):
        rule={k[0].lower()+k[1:]:r[k] for k in (*FIELDS,'Provenance','Reason')}
        kind=r['RuleType']; rule['executionIndex']=index
        rule['matcher']=({'kind':'regex','pattern':r['Pattern']} if kind not in ('remote-designation','remote-signal','extended-location-signal') else {'kind':'parsed-fact','source':'extendedLocation.signals' if kind=='extended-location-signal' else 'remoteWork.isRemoteDesignated' if kind=='remote-designation' else 'remoteWork.signals','operator':'isTrue' if kind=='remote-designation' else 'firstCategoryEquals','category':None if kind=='remote-designation' else r['Pattern']})
        rules.append(rule)
    result=dict(formatVersion=1,purpose='Frozen SQLite migration oracle; NOT production authority',sourceDatabaseSha256=m['archiveDatabaseSha256'],runtimeFingerprint=m['runtimeFingerprint'],taxonomyVersion=m['taxonomyVersion'],taxonomyHash=m['taxonomyHash'],policy=m['policy'],policyFingerprint=m['policyFingerprint'],ruleCount=len(rows),conceptCount=85,typeCounts=dict(sorted(collections.Counter(r['RuleType'] for r in rows).items())),scopeCounts=dict(sorted(collections.Counter(r['Scope'] for r in rows).items())),executionOrder=['ConceptId','RuleType','RuleId'],contextGroups={g:[r['RuleId'] for r in rows if r['ContextGroupId']==g] for g in sorted({r['ContextGroupId'] for r in rows if r['ContextGroupId']})},rules=rules,seedParity=parity)
    out.mkdir(parents=True,exist_ok=True);dump(out/'sqlite-effective-rules.json',result)
    # All mutable metadata stays in the private complete archive, not definitions.
    dump(out/'archive-reference.json',dict(sourceDatabase=m['sourceDatabase'],sourceVersion=m['sourceVersion'],databaseSha256=m['archiveDatabaseSha256'],manifestSha256=sha((root/'manifest.json').read_bytes()),tableCounts=m['tableCounts'],schemaHistory=load(root/'history/SchemaInfo.json'),artifacts=m['artifactInventory'],effectiveExportSha256=sha((out/'sqlite-effective-rules.json').read_bytes())))
    print(json.dumps(parity))
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('archive',type=pathlib.Path);p.add_argument('output',type=pathlib.Path);a=p.parse_args();export(a.archive,a.output)
