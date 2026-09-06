"""Score frozen employer-out predictions; select operating thresholds on validation only."""
import argparse
import hashlib
import importlib
import json
import math
import platform
import sys
from pathlib import Path
import numpy as np
from sklearn.feature_extraction.text import TfidfVectorizer

def metrics(rows, scores, threshold):
    keep=[scores[r['id']]>=threshold for r in rows]
    k=sum(r['label']=='KEEP' for r in rows)
    fn=sum(r['label']=='KEEP' and not pred for r,pred in zip(rows,keep))
    reject=sum(not pred for pred in keep)
    return dict(n=len(rows),keeps=k,falseRejects=fn,rejected=reject,keepRecall=(k-fn)/k if k else None,
                rejectionRate=reject/len(rows) if rows else None,rejectPrecision=(reject-fn)/reject if reject else None)

def choose_threshold(rows, scores, recall):
    values=sorted(scores[r['id']] for r in rows if r['label']=='KEEP')
    if not values:return 0.
    allowed=math.floor((1-recall)*len(values)+1e-9)
    return values[allowed]  # strict below rejects, equality is KEEP

def calibration(rows,scores):
    y=np.array([r['label']=='KEEP' for r in rows],float)
    p=np.array([scores[r['id']] for r in rows])
    bins=[];ece=0
    for low in np.arange(0,1,.1):
        mask=(p>=low)&(p<low+.1 if low<.9 else p<=1)
        if mask.any():
            mean=float(p[mask].mean());rate=float(y[mask].mean());count=int(mask.sum())
            ece+=count/len(rows)*abs(mean-rate)
            bins.append(dict(low=float(low),n=count,meanKeepScore=mean,keepFraction=rate))
    return dict(brier=float(np.mean((p-y)**2)),crossEntropy=float(np.mean(-(y*np.log(np.maximum(p,1e-7))+(1-y)*np.log(np.maximum(1-p,1e-7))))),ece10Bins=ece,bins=bins)

def score(a):
    raw=json.loads((a.root/'predictions.json').read_text())
    split=json.loads((a.root/'splits.json').read_text())
    rows=[json.loads(line) for line in (a.root/'corrected.jsonl').read_text(encoding='utf-8').splitlines()]
    byid={r['id']:r for r in rows}
    pool=json.loads(a.pool.read_text(encoding='utf-8'))
    pool_hash=hashlib.sha256(json.dumps(pool,sort_keys=True,ensure_ascii=False,separators=(',',':')).encode()).hexdigest()
    assert pool_hash==json.loads((a.evaluation_root/'manifest.json').read_text())['eligiblePoolCanonicalSha256']
    assert len(pool)==1503 and {r['id'] for r in rows}<={r['id'] for r in pool}
    sys.path.insert(0,str(a.evaluation_root));rules=importlib.import_module('leakage')
    baseline={r['id']:float(rules.decide(r,'electrical-safe')[0]) for r in pool}
    unreviewed=[r for r in pool if r['id'] not in byid]
    result=dict(protocol=raw['protocol'],environment=raw['environment'],scoringHost=platform.node(),scorerSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),candidates=[],baseline=dict(
        evaluation=metrics(rows,baseline,.5),cache=dict(n=len(pool),rejected=sum(s<.5 for s in baseline.values())),
        unreviewedCache=dict(n=len(unreviewed),rejected=sum(baseline[r['id']]<.5 for r in unreviewed)),
        timing=rules.benchmark(rows,'electrical-safe')),
        predictionSha256=hashlib.sha256((a.root/'predictions.json').read_bytes()).hexdigest())
    # Lexical novelty is fit only on each training partition, with no labels.
    similarity={}
    for f in split['folds']:
        train=[byid[i] for i in f['partitions']['train']]
        test=[byid[i] for i in f['partitions']['evaluation']]
        vectorizer=TfidfVectorizer(analyzer='char_wb',ngram_range=(3,5),min_df=1)
        x=vectorizer.fit_transform([r['title'] for r in train])
        scores=(vectorizer.transform([r['title'] for r in test])@x.T).toarray().max(axis=1)
        similarity.update({r['id']:float(v) for r,v in zip(test,scores)})
    for model in raw['protocol']['models']:
        for view in raw['protocol']['views']:
            runs=[r for r in raw['runs'] if r['model']==model and r['view']==view]
            assert len(runs)==3
            predictions={p['id']:p['keepScore'] for r in runs for p in r['cache']}
            assert len(predictions)==1503 and sum(len(r['cache']) for r in runs)==1503
            c=dict(model=model,view=view,calibration=calibration(rows,predictions),sweep=[],operatingPoints=[],folds=[],
                   checkpoints=[{k:r[k] for k in ['fold','baseRevision','baseWeightSha256','checkpointPath','checkpointSha256','parameters','selectedEpoch','history','training','gpuTiming','inferenceMemory','cpuTiming','truncation']} for r in runs])
            for t in raw['protocol']['thresholds']:
                c['sweep'].append(dict(threshold=t,evaluation=metrics(rows,predictions,t),cacheRejected=sum(v<t for v in predictions.values()),
                                     unreviewedRejected=sum(predictions[r['id']]<t for r in unreviewed)))
            for recall in [1.,.99,.98]:
                normalized={};thresholds={};diag={mode:{} for mode in ['company_masked','body_tail']};foldresults=[]
                synth=[]
                for run in runs:
                    f=split['folds'][run['fold']];val=[byid[i] for i in f['partitions']['validation']]
                    valp={r['id']:r['keepScore'] for r in run['validation']}
                    t=choose_threshold(val,valp,recall);thresholds[str(run['fold'])]=t
                    normalized.update({p['id']:float(p['keepScore']>=t) for p in run['cache']})
                    ev=[byid[i] for i in f['partitions']['evaluation']]
                    foldresults.append(dict(fold=run['fold'],threshold=t,validation=metrics(val,valp,t),evaluation=metrics(ev,predictions,t)))
                    for mode in diag:diag[mode].update({p['id']:float(p['keepScore']>=t) for p in run['diagnostics'][mode]})
                    fixtures=[json.loads(line) for line in (a.evaluation_root/'fixtures.jsonl').read_text(encoding='utf-8').splitlines()]
                    synth.append(dict(fold=run['fold'],metrics=metrics(fixtures,{p['id']:p['keepScore'] for p in run['synthetic']},t)))
                entry=dict(validationTargetRecall=recall,thresholds=thresholds,folds=foldresults,evaluation=metrics(rows,normalized,.5),
                           cacheRejected=sum(v<.5 for v in normalized.values()),unreviewedRejected=sum(normalized[r['id']]<.5 for r in unreviewed),synthetic=synth)
                entry['falseRejects']=[dict(id=r['id'],title=r['title'],company=r['company'],reason=r['labelReason'],score=predictions[r['id']],titleSimilarity=similarity[r['id']]) for r in rows if r['label']=='KEEP' and normalized[r['id']]<.5]
                negatives=sorted([r for r in rows if r['label']=='REJECT' and normalized[r['id']]<.5],key=lambda r:predictions[r['id']])
                entry['obviousRejects']=[dict(id=r['id'],title=r['title'],company=r['company'],reason=r['labelReason'],score=predictions[r['id']]) for r in negatives[:12]]
                entry['noveltySlices']={name:dict(model=metrics(selected,normalized,.5),baseline=metrics(selected,baseline,.5)) for name,selected in {
                    'lowSimilarityBelow035':[r for r in rows if similarity[r['id']]<.35],
                    'higherSimilarity':[r for r in rows if similarity[r['id']]>=.35]}.items()}
                entry['employers']={company:dict(model=metrics([r for r in rows if r['company']==company],normalized,.5),baseline=metrics([r for r in rows if r['company']==company],baseline,.5)) for company in sorted(split['companyFold'])}
                entry['diagnostics']={mode:dict(evaluation=metrics(rows,vals,.5),decisionChanges=sum(vals[r['id']]!=normalized[r['id']] for r in rows)) for mode,vals in diag.items()}
                c['operatingPoints'].append(entry)
            # Descriptive outer-score frontier is clearly segregated from
            # validation-selected operating points; no model is deployed at it.
            c['optimisticEvaluationFrontier']=[]
            for recall in [1.,.99,.98]:
                t=choose_threshold(rows,predictions,recall)
                c['optimisticEvaluationFrontier'].append(dict(targetRecall=recall,threshold=t,evaluation=metrics(rows,predictions,t),cacheRejected=sum(v<t for v in predictions.values())))
            result['candidates'].append(c)
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for key in ['root','pool','evaluation-root','output']:p.add_argument('--'+key,type=Path,required=True)
    a=p.parse_args();result=score(a)
    a.output.write_bytes((json.dumps(result,indent=2,ensure_ascii=False)+'\n').encode())
    for c in result['candidates']:
        print(c['model'],c['view'])
        for o in c['operatingPoints']:print(o['validationTargetRecall'],o['evaluation'],'cache',o['cacheRejected'])
