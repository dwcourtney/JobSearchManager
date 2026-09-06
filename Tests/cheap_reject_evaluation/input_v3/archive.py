"""Persist trained experiment checkpoints on curiosity; never deploy them."""
import hashlib
import json
import shutil
from pathlib import Path

root=Path(__file__).resolve().parent
destination=Path('/home/codex/jsm-lab/experiments/input-v3-20260905')
destination.mkdir(parents=True,exist_ok=True)
records=[]
for path in sorted((root/'output').glob('*-fold[012].json')):
    run=json.loads(path.read_text())
    source=Path(run['checkpointPath'])
    if not source.is_absolute():
        source=root/source
    if run['view']=='prefix256':
        target=source
    else:
        target=destination/run['tag']
        shutil.copytree(source,target,dirs_exist_ok=True)
    digest=hashlib.sha256((target/'model.safetensors').read_bytes()).hexdigest()
    assert digest==run['checkpointSha256']
    records.append(dict(tag=run['tag'],path=str(target),sha256=digest,reused=run['view']=='prefix256'))
(root/'checkpoint-manifest.json').write_text(json.dumps(dict(checkpoints=records),indent=2)+'\n')
print('ARCHIVED',len(records),flush=True)
