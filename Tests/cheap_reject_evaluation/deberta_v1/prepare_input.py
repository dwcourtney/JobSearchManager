"""Build label-free model input from the already-excluded frozen cache."""
import argparse
import hashlib
import json
from pathlib import Path


def prepare(pool_path, evaluation_root):
    pool = json.loads(pool_path.read_text(encoding='utf-8'))
    manifest = json.loads((evaluation_root/'manifest.json').read_text(encoding='utf-8'))
    canonical = hashlib.sha256(json.dumps(pool,sort_keys=True,ensure_ascii=False,separators=(',',':')).encode()).hexdigest()
    if canonical != manifest['eligiblePoolCanonicalSha256']:
        raise ValueError('Wrong cache snapshot')
    ids={r['id'] for r in pool}
    for name in ['development.jsonl','leakage-audit.jsonl','leakage-delta-audit.jsonl']:
        rows=[json.loads(line) for line in (evaluation_root/name).read_text(encoding='utf-8').splitlines()]
        if not {r['id'] for r in rows} <= ids:
            raise ValueError('Audited posting missing from pool')
    fixtures=[json.loads(line) for line in (evaluation_root/'fixtures.jsonl').read_text(encoding='utf-8').splitlines()]
    return [dict(id=r['id'],title=r['title'],body=r['body']) for r in pool+fixtures]


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--pool',type=Path,required=True)
    p.add_argument('--evaluation-root',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args()
    rows=prepare(args.pool,args.evaluation_root)
    # Match the frozen Windows-exported input bytes on every platform.
    args.output.write_bytes((json.dumps(rows,ensure_ascii=False)+'\r\n').encode('utf-8'))
    print(len(rows),'label-free input records')
