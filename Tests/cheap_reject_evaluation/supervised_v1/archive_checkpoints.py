"""Preserve measured experimental weights outside /tmp; never deploy them."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--predictions',type=Path,required=True);p.add_argument('--destination',type=Path,required=True);p.add_argument('--manifest',type=Path,required=True)
    a=p.parse_args()
    raw=json.loads(a.predictions.read_text())
    assert len(raw['runs'])==12
    if a.destination.exists():raise FileExistsError('Refuse to overwrite experiment archive')
    a.destination.mkdir(parents=True)
    records=[]
    for run in raw['runs']:
        target=a.destination/run['tag']
        shutil.copytree(run['checkpointPath'],target)
        digest=hashlib.sha256((target/'model.safetensors').read_bytes()).hexdigest()
        assert digest==run['checkpointSha256']
        records.append(dict(tag=run['tag'],checkpointPath=str(target),sha256=digest))
    a.manifest.write_text(json.dumps(dict(host=raw['environment']['host'],checkpoints=records),indent=2)+'\n')
    shutil.copy2(a.manifest,a.destination/'checkpoint-manifest.json')
    print('Archived and verified',len(records),'experimental checkpoints at',a.destination)
