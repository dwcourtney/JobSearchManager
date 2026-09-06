"""Freeze corpus-only training partitions. No inference or label generation."""
import argparse, collections, gzip, hashlib, json, sys, time
from pathlib import Path

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x): p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf8')
def prepare(root,out):
    out.mkdir(parents=True,exist_ok=True)
    source=root/'machine_corpus_v1/machine-labeled.jsonl'
    rows=[json.loads(x) for x in source.read_text(encoding='utf8').splitlines()]
    assert len(rows)==3198
    binary=[r for r in rows if r['label'] in ['KEEP','REJECT']]
    assert len(binary)==2669 and all(r['confidence'] in ['high','medium'] for r in binary)
    groups=[['leidos'],['boeing','servicenow','amentum','nxp-semiconductors'],['nvidia','parsons','rtx','aecom','northrop-grumman','kbr']]
    vals=[['boeing'],['nvidia','kbr'],['servicenow','amentum']]
    # Union corpus lexical families, duplicate clusters, identical bodies and titles.
    parent={r['id']:r['id'] for r in rows}
    def find(i):
        while parent[i]!=i: parent[i]=parent[parent[i]];i=parent[i]
        return i
    seen={}
    for r in rows:
        keys=[('family',r['titleFamilyCluster']),('duplicate',r['duplicateCluster']),('title',r['titleGroup'])]
        if r['body']:keys.append(('body',hashlib.sha256(r['body'].encode()).hexdigest()))
        for key in keys:
            if key in seen:parent[find(r['id'])]=find(seen[key])
            else:seen[key]=r['id']
    for r in rows:r['splitGroup']=find(r['id'])
    folds=[]
    for f in range(3):
        ev=[r for r in binary if r['employer'] in groups[f]]
        # Purge using all evaluation-employer corpus rows, including AMBIGUOUS.
        eg={r['splitGroup'] for r in rows if r['employer'] in groups[f]}
        va=[r for r in binary if r['employer'] in vals[f] and r['splitGroup'] not in eg]
        vg={r['splitGroup'] for r in va}
        tr=[r for r in binary if r['employer'] not in groups[f]+vals[f] and r['splitGroup'] not in eg|vg]
        parts={'train':tr,'validation':va,'evaluation':ev}
        counts={k:{'total':len(v),'labels':dict(collections.Counter(r['label'] for r in v)), 'described':dict(collections.Counter(r['label'] for r in v if r['bodyAvailable']))} for k,v in parts.items()}
        assert all(set(r['label'] for r in v if r['bodyAvailable'])=={'KEEP','REJECT'} for v in parts.values())
        folds.append(dict(fold=f,partitions={k:[r['id'] for r in v] for k,v in parts.items()},counts=counts,evaluationEmployers=groups[f],validationEmployers=vals[f]))
    pool=json.loads(gzip.decompress((root/'deberta_v1/eligible.json.gz').read_bytes()))
    legacy={r['id']:r for r in [json.loads(x) for x in (root/'supervised_v1/corrected.jsonl').read_text(encoding='utf8').splitlines()]}
    for r in pool:
        original=r['id'];r['id']='legacy:'+original;r['employer']=r['company'];r['bodyAvailable']=bool(r['body']);r['sourceId']=original
        r['label']=legacy[original]['label'] if original in legacy else None
        r['confidence']='legacy-provisional';r['category']='legacy'
    sys.path.insert(0,str(root));import leakage
    baseline={};times=[]
    for r in rows+pool:
        t=time.perf_counter();keep,reason=leakage.decide(r,'electrical-safe');times.append(time.perf_counter()-t)
        baseline[r['id']]={'keep':keep,'reason':reason}
    for name,data in [('corpus.jsonl',rows),('cache.jsonl',pool)]:
        (out/name).write_text(''.join(json.dumps(r,ensure_ascii=False)+'\n' for r in data),encoding='utf8')
    write(out/'baseline.json',dict(decisions=baseline,n=len(times),seconds=sum(times),msPerPosting=1000*sum(times)/len(times),postingsPerSecond=len(times)/sum(times),sourceHashes={n:sha(root/n) for n in ['leakage.py','evaluate.py']}))
    write(out/'splits.json',dict(sourceSha256=sha(source),corpusSha256=sha(out/'corpus.jsonl'),cacheSha256=sha(out/'cache.jsonl'),folds=folds,companyFold={e:f for f,g in enumerate(groups) for e in g},method='Employer-disjoint outer evaluation and inner validation; corpus lexical family/duplicate/title/body components purged across all partitions. AMBIGUOUS excluded from binary fit/calibration/evaluation; scored separately.',binary=2669,ambiguous=529,qualityBEqualsC=True))
    print(json.dumps([f['counts'] for f in folds],indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--evaluation-root',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();prepare(a.evaluation_root,a.output)
