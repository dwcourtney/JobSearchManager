"""Preserve experiment checkpoints outside /tmp; never deploy."""
import argparse,hashlib,json,shutil
from pathlib import Path
def main(root,destination):
    paths=list((root/'output').glob('*-fold[012].json'));assert len(paths)==36
    if destination.exists():raise FileExistsError('Refuse to overwrite archive')
    destination.mkdir(parents=True);records=[]
    for p in sorted(paths):
        r=json.loads(p.read_text());target=destination/r['tag'];shutil.copytree(r['checkpointPath'],target)
        digest=hashlib.sha256((target/'model.safetensors').read_bytes()).hexdigest();assert digest==r['checkpointSha256']
        records.append(dict(tag=r['tag'],checkpointPath=str(target),sha256=digest,baseRevision=r['baseRevision']))
    text=json.dumps(dict(host='curiosity',checkpoints=records),indent=2)+'\n'
    (root/'checkpoint-manifest.json').write_text(text);(destination/'checkpoint-manifest.json').write_text(text)
    print('Archived and verified 36 experimental checkpoints',flush=True)
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--destination',type=Path,required=True);a=p.parse_args();main(a.root,a.destination)
