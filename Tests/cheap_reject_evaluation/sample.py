import json,hashlib,re,collections,argparse
from pathlib import Path

def select(rows,manifest):
    """Select before labels: stable order, family quotas, then background coverage."""
    seed=manifest['seed']
    ordered=sorted(rows,key=lambda r:hashlib.sha256((seed+'|'+r['id']).encode()).hexdigest())
    selected=[];seen=set()
    for family,pattern in list(manifest['samplingFamilies'].items())+[('background',r'.*')]:
        quota=60 if family=='core-technical' else 30 if family=='background' else 15
        # Enforce unique titles inside each stratum as well as across strata.
        for r in [r for r in ordered if r['titleGroup'] not in seen and re.search(pattern,r['title'],re.I)]:
            if r['titleGroup'] in seen: continue
            selected.append(dict(r,reviewId=len(selected),samplingStratum=family));seen.add(r['titleGroup']);quota-=1
            if quota==0:break
    return selected

if __name__=='__main__':
    p=argparse.ArgumentParser(description='Sample an already holdout-excluded public-text pool. Never accepts labels/predictions as selection inputs.')
    p.add_argument('pool',type=Path);p.add_argument('manifest',type=Path);p.add_argument('output',type=Path)
    a=p.parse_args(); rows=select(json.loads(a.pool.read_text(encoding='utf-8')),json.loads(a.manifest.read_text(encoding='utf-8')))
    a.output.write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(dict(collections.Counter(r['samplingStratum'] for r in rows)))
