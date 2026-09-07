import pathlib,json,hashlib,subprocess,shutil
r=pathlib.Path(r'D:\Computer Everything\Programming\CS\JobSearchManager');p=pathlib.Path('step3-b2c269').resolve();bundle='b2c269cc766fe41cd82ed594f8bcbff04f940103361dbff172b7a480f051db0c';s=r/'docs/evaluations/human-review-snapshots'/bundle
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(s/'manifest.json')==bundle
for name,h in json.loads((s/'manifest.json').read_text())['artifacts'].items():assert sha(s/name)==h
assert json.loads((r/'CheapTriage/evaluation-context.json').read_text())['rulesetFingerprint']==sha(r/'CheapTriage/rulesets/1.0.0.json')
assert 'All 140 deterministic architecture tests passed.' in (p/'dotnet.log').read_text()
print('Snapshot manifest, artifact hashes, baseline context and all 140 tests verified')
