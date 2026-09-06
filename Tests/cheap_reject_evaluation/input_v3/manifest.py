"""Inventory the final offline bundle, or verify its exact contents."""
import hashlib
import json
import sys
from pathlib import Path

root=Path(__file__).resolve().parent
path=root/'artifact-manifest.json'
files={str(p.relative_to(root)).replace('\\','/'):
       dict(bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest())
       for p in sorted(root.rglob('*')) if p.is_file() and p!=path}
if '--verify' in sys.argv:
    assert files==json.loads(path.read_text(encoding='utf8'))['files']
    print('MANIFEST VERIFIED',len(files),'files')
else:
    path.write_text(json.dumps(dict(files=files),indent=2)+'\n',encoding='utf8')
    print('MANIFEST CREATED',len(files),'files plus manifest')
