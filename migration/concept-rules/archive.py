"""Phase 1 archive: read-only source + SQLite online backup; never open the app store.

Run on curiosity with an unused destination directory. Originals are never moved.
The archive is sealed read-only and its manifest is independently hash-verifiable.
"""
import argparse, base64, collections, datetime, hashlib, json, pathlib, shutil, sqlite3, subprocess

def digest(data): return hashlib.sha256(data).hexdigest()
def write(path, value): path.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('destination', type=pathlib.Path)
    ap.add_argument('--database', type=pathlib.Path, default=pathlib.Path('/home/codex/jsm-lab/data/app/regex-rules.db'))
    ap.add_argument('--container', default='jsm-lab-jsm-1')
    a=ap.parse_args(); out=a.destination.resolve(); out.mkdir(parents=True, exist_ok=False)
    started=datetime.datetime.now(datetime.timezone.utc).isoformat()
    config=json.loads(subprocess.check_output(['docker','inspect',a.container]))[0]
    version=json.loads(subprocess.check_output(['curl','--fail','--silent','http://192.168.1.20:8080/version']))
    if any(x.startswith('SemanticRules__') for x in config['Config']['Env']): raise ValueError('Unexpected policy override; inspect before archiving')
    settings=json.loads(subprocess.check_output(['docker','exec',a.container,'cat','/app/appsettings.json']))
    if settings.get('SemanticRules'): raise ValueError('Unexpected configured policy')
    policy=dict(reviewAfterDays=30,retiredRetentionDays=180,telemetryFlushSeconds=10,maximumPatternLength=4096,regexTimeoutMilliseconds=100)
    for name in ['JobConceptCatalog.json','LegacyJobConceptRules.json','RegexValidationCorpus.json']:
        (out/name).write_bytes(subprocess.check_output(['docker','exec',a.container,'cat','/app/'+name]))
    source=sqlite3.connect(a.database.resolve().as_uri()+'?mode=ro',uri=True)
    source.execute('PRAGMA query_only=ON')
    target=sqlite3.connect(out/'authority.db')
    source.backup(target); target.close(); source.close()
    backup=sqlite3.connect((out/'authority.db').as_uri()+'?mode=ro&immutable=1',uri=True); backup.row_factory=sqlite3.Row
    integrity=[r[0] for r in backup.execute('PRAGMA integrity_check')]; foreign=[list(r) for r in backup.execute('PRAGMA foreign_key_check')]
    if integrity != ['ok'] or foreign: raise ValueError('Backup integrity failure')
    schema=[dict(r) for r in backup.execute('SELECT type,name,tbl_name,sql FROM sqlite_schema ORDER BY type,name')]
    names=[r['name'] for r in schema if r['type']=='table']
    expected={'SemanticRules','RuleRelationships','CandidateValidations','EvaluationRuns','RuleEvaluationResults','ConceptEvaluationResults','LlmEvaluationRunDetails','SchemaInfo'}
    if set(names)!=expected: raise ValueError('Unexpected tables')
    (out/'history').mkdir(); counts={}
    for name in names:
        rows=[dict(r) for r in backup.execute('SELECT * FROM "'+name+'" ORDER BY rowid')]
        # Preserve binary values explicitly if a future schema introduces them.
        rows=[{k:({'$sqliteBlobBase64':base64.b64encode(v).decode()} if isinstance(v,bytes) else v) for k,v in r.items()} for r in rows]
        counts[name]=len(rows); write(out/'history'/(name+'.json'),rows)
    backup.close(); write(out/'schema.json',schema)
    inventory=[]
    roots=[a.database.parent/'evaluation',pathlib.Path('/home/codex/jsm-research/archives/cheap-triage-retirement-be1a6a725cc7')]
    for index,root in enumerate(roots):
        if not root.is_dir(): raise FileNotFoundError(root)
        for file in sorted(root.rglob('*')):
            if file.is_symlink(): raise ValueError('Inspect symlink before archiving: '+str(file))
            if not file.is_file(): continue
            rel=pathlib.Path('artifacts')/str(index)/file.relative_to(root); dest=out/rel; dest.parent.mkdir(parents=True,exist_ok=True)
            raw=file.read_bytes(); dest.write_bytes(raw)
            inventory.append(dict(source=str(file),archive=rel.as_posix(),bytes=len(raw),sha256=digest(raw)))
    # Full cache JSON copies are private archive inputs, never repository fixtures.
    for file in sorted((a.database.parent/'workspaces').glob('*/shared/job-caches/**/*.json')):
        rel=pathlib.Path('cache-inputs')/file.relative_to(a.database.parent/'workspaces');dest=out/rel;dest.parent.mkdir(parents=True,exist_ok=True)
        raw=file.read_bytes();dest.write_bytes(raw);inventory.append(dict(source=str(file),archive=rel.as_posix(),bytes=len(raw),sha256=digest(raw)))
    catalog=json.loads((out/'JobConceptCatalog.json').read_bytes())
    manifest=dict(formatVersion=1,backupMethod='sqlite3.Connection.backup from mode=ro/query_only source',startedUtc=started,completedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),sourceDatabase=str(a.database.resolve()),sourceVersion=version,sourceImage=config['Config']['Image'],sourceImageId=config['Image'],sourceMounts=[dict(source=m['Source'],destination=m['Destination']) for m in config['Mounts']],runtimeFingerprint='9d491a52ed1046fa319f2b6b9f4d81048b77f9d4a6774d803a14afee5ae06d2a',runtimeFingerprintProvenance='Prior live Admin observation; independently verified against backup by C# harness',taxonomyVersion=catalog['version'],taxonomyHash=digest((out/'JobConceptCatalog.json').read_bytes().replace(b'\r\n',b'\n')),policy=policy,policyFingerprint=digest(json.dumps(policy,separators=(',',':')).encode()),integrityCheck=integrity,foreignKeyCheck=foreign,tableCounts=counts,archiveDatabaseSha256=digest((out/'authority.db').read_bytes()),artifactInventory=inventory)
    manifest['files']=[dict(path=p.relative_to(out).as_posix(),sha256=digest(p.read_bytes()),bytes=p.stat().st_size) for p in sorted(out.rglob('*')) if p.is_file()]
    write(out/'manifest.json',manifest)
    (out/'manifest.sha256').write_text(digest((out/'manifest.json').read_bytes())+'  manifest.json\n')
    for p in out.rglob('*'):
        if p.is_file(): p.chmod(0o444)
    for p in sorted((p for p in out.rglob('*') if p.is_dir()),reverse=True): p.chmod(0o555)
    out.chmod(0o555)
    print(json.dumps(dict(archive=str(out),databaseSha256=manifest['archiveDatabaseSha256'],counts=counts,files=len(manifest['files']))))

if __name__=='__main__': main()
