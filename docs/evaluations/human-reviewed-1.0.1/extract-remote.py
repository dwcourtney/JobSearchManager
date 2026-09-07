import pathlib,json,tarfile
root=pathlib.Path('/home/codex/jsm-lab/experiments/live-shadow-audit-20260906')
with tarfile.open('/tmp/jsm-rule-audit-inputs.tar.gz','w:gz') as t:
 for p in root.glob('*/input.json'): t.add(p,arcname=p.parent.name+'.json')
x=json.loads(next(root.glob('*/input.json')).read_text());print(x.keys());print(x['jobs'][0].keys())
