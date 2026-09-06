"""Freeze corrected references and employer-disjoint nested development splits."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import numpy as np
from sklearn.model_selection import StratifiedGroupKFold

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def prepare(root, output):
    merged, origins, corrections = {}, collections.defaultdict(list), []
    files = ['development.jsonl','leakage-audit.jsonl','leakage-delta-audit.jsonl']
    for file in files:
        for line in (root/file).read_text(encoding='utf-8').splitlines():
            row = json.loads(line)
            if row['id'] in merged and merged[row['id']]['label'] != row['label']:
                corrections.append(dict(id=row['id'], old=merged[row['id']]['label'], new=row['label'], reason=row['labelReason']))
            merged[row['id']] = row
            origins[row['id']].append(file)
    rows = [dict(r, annotationSources=origins[r['id']]) for r in sorted(merged.values(),key=lambda r:r['id'])]
    assert len(rows)==347 and sum(r['label']=='KEEP' for r in rows)==150
    assert [r['id'] for r in corrections]==['d44a1bd2c6c20832']
    y=np.array([int(r['label']=='KEEP') for r in rows])
    groups=np.array([r['company'] for r in rows])
    def title(r):
        return ' '.join(r['titleGroup'].casefold().split())
    folds=[]
    for fold,(rest,test) in enumerate(StratifiedGroupKFold(3,shuffle=True,random_state=20260905).split(rows,y,groups)):
        train,val=next(StratifiedGroupKFold(3,shuffle=True,random_state=20260906+fold).split(rest,y[rest],groups[rest]))
        train,val=rest[train],rest[val]
        # Purge identical normalized titles from earlier partitions, not tests.
        test_titles={title(rows[i]) for i in test}
        val=[i for i in val if title(rows[i]) not in test_titles]
        val_titles={title(rows[i]) for i in val}
        train=[i for i in train if title(rows[i]) not in test_titles|val_titles]
        partitions={k:[rows[i]['id'] for i in indices] for k,indices in [('train',train),('validation',val),('evaluation',test)]}
        companies={k:sorted({merged[i]['company'] for i in ids}) for k,ids in partitions.items()}
        counts={k:dict(collections.Counter(merged[i]['label'] for i in ids)) for k,ids in partitions.items()}
        folds.append(dict(fold=fold,partitions=partitions,companies=companies,counts=counts))
    company_fold={c:f['fold'] for f in folds for c in f['companies']['evaluation']}
    output.mkdir(parents=True,exist_ok=True)
    (output/'corrected.jsonl').write_bytes(('\n'.join(json.dumps(r,ensure_ascii=False) for r in rows)+'\n').encode())
    manifest=dict(seed=20260905,sourceHashes={f:sha(root/f) for f in files},correctedSha256=sha(output/'corrected.jsonl'),
                  corrections=corrections,folds=folds,companyFold=company_fold,
                  limitations='Provisional previously inspected editorial labels. Employer-disjoint folds, exact-title purging; not broad occupation-family-disjoint or independently blinded human evaluation.')
    (output/'splits.json').write_bytes((json.dumps(manifest,indent=2)+'\n').encode())
    print(json.dumps([(f['fold'],f['counts'],f['companies']) for f in folds],indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--evaluation-root',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();prepare(a.evaluation_root,a.output)
