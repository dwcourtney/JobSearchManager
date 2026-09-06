"""Analyze frozen out-of-sample scores. Never trains or accesses production holdout."""
import argparse, collections, hashlib, json, math, statistics
from pathlib import Path

def metrics(rows,scores,thresholds):
    reject=[r for r in rows if scores[r['id']]<thresholds[r['id']]]
    k=sum(r.get('label')=='KEEP' for r in rows);fn=sum(r.get('label')=='KEEP' for r in reject)
    labeled=all(r.get('label') in ['KEEP','REJECT'] for r in rows)
    return dict(n=len(rows),keeps=k,falseRejects=fn if labeled else None,rejected=len(reject),keepRecall=1-fn/k if k and labeled else None,rejectionRate=len(reject)/len(rows) if rows else None,rejectPrecision=sum(r.get('label')=='REJECT' for r in reject)/len(reject) if reject and labeled else None)

def choose(rows,scores,target):
    values=sorted(scores[r['id']] for r in rows if r['label']=='KEEP')
    return values[math.floor((1-target)*len(values)+1e-9)] if values else 0.

def slices(rows):
    return {'combined':rows,'description':[r for r in rows if r['bodyAvailable']],'title_only':[r for r in rows if not r['bodyAvailable']],**{'confidence_'+c:[r for r in rows if r['confidence']==c] for c in ['high','medium']}}

def examples(rows,scores,thresholds,label,limit=12):
    ordered=sorted([r for r in rows if r['label']==label and scores[r['id']]<thresholds[r['id']]],key=lambda r:scores[r['id']])
    selected=[];seen=set()
    for r in ordered:
        key=(r['employer'],r['title'].casefold())
        if key not in seen:selected.append(r);seen.add(key)
        if len(selected)==limit:break
    return [{**{k:r.get(k) for k in ['id','employer','title','label','confidence','bodyAvailable','category','reason']},'keepScore':scores[r['id']],'threshold':thresholds[r['id']]} for r in selected]

def main(root):
    rows=[json.loads(x) for x in (root/'corpus.jsonl').read_text(encoding='utf8').splitlines()]
    cache=[json.loads(x) for x in (root/'cache.jsonl').read_text(encoding='utf8').splitlines()]
    binary=[r for r in rows if r['label']!='AMBIGUOUS'];amb=[r for r in rows if r['label']=='AMBIGUOUS'];legacy=[r for r in cache if r['label'] in ['KEEP','REJECT']]
    byid={r['id']:r for r in rows+cache};split=json.loads((root/'splits.json').read_text());cfg=json.loads((root/'protocol.json').read_text());baseline=json.loads((root/'baseline.json').read_text())
    rule={i:float(x['keep']) for i,x in baseline['decisions'].items()};half={i:.5 for i in rule}
    result=dict(baseline=dict(slices={k:metrics(v,rule,half) for k,v in slices(binary).items()},cache=metrics(cache,rule,half),corpus=metrics(rows,rule,half),ambiguous=metrics(amb,rule,half),legacy=metrics(legacy,rule,half),falseRejects=examples(binary,rule,half,'KEEP',100),timing={k:baseline[k] for k in ['n','seconds','msPerPosting','postingsPerSecond']}),candidates=[])
    buckets={}
    for path in sorted((root/'output').glob('*-fold[012].json')):
        run=json.loads(path.read_text());buckets.setdefault((run['model'],run['view'],run['regime']),[]).append(run)
    assert len(buckets)==6 and all(len(x)==3 for x in buckets.values())
    for (model,view,regime),runs in sorted(buckets.items()):
        scores={p['id']:p['keepScore'] for run in runs for p in run['predictions']};assert set(scores)==set(byid)
        foldof={p['id']:run['fold'] for run in runs for p in run['predictions']}
        entry=dict(model=model,view=view,regime=regime,operatingPoints=[],sweep=[],retrospectiveFrontier={},folds=[dict(fold=r['fold'],selectedEpoch=r['selectedEpoch'],history=r['history'],training=r['training'],gpuTiming=r['gpuTiming'],memory=r['inferenceMemory'],truncation=r['truncation'],checkpointSha256=r['checkpointSha256']) for r in runs])
        for target in [1.,.99,.98]:
            thresholds={}
            for run in runs:
                vp={p['id']:p['keepScore'] for p in run['validation']};vr=[byid[i] for i in vp]
                thresholds[run['fold']]=choose(vr,vp,target)
            ts={i:thresholds[foldof[i]] for i in scores}
            point=dict(validationTarget=target,thresholds=thresholds,slices={k:metrics(v,scores,ts) for k,v in slices(binary).items()},cache=metrics(cache,scores,ts),corpus=metrics(rows,scores,ts),ambiguous=metrics(amb,scores,ts),legacy=metrics(legacy,scores,ts),falseRejects=examples(binary,scores,ts,'KEEP',100),obviousRejects=examples(binary,scores,ts,'REJECT'),legacyFalseRejects=examples(legacy,scores,ts,'KEEP',100),employers={e:metrics([r for r in binary if r['employer']==e],scores,ts) for e in split['companyFold']},categories={c:metrics([r for r in binary if r['category']==c],scores,ts) for c in sorted({r['category'] for r in binary})},diagnostics={})
            for mode in ['company_masked']:
                ds={p['id']:p['keepScore'] for run in runs for p in run['diagnostics'][mode]}
                point['diagnostics'][mode]=dict(slices={k:metrics(v,ds,ts) for k,v in slices(binary).items()},decisionFlips=sum((scores[r['id']]<ts[r['id']])!=(ds[r['id']]<ts[r['id']]) for r in binary),meanAbsoluteScoreChange=statistics.mean(abs(scores[r['id']]-ds[r['id']]) for r in binary))
            entry['operatingPoints'].append(point)
        for t in cfg['thresholds']:
            ts={i:t for i in scores};entry['sweep'].append(dict(threshold=t,slices={k:metrics(v,scores,ts) for k,v in slices(binary).items()},cache=metrics(cache,scores,ts)))
        for name,sub in slices(binary).items():
            entry['retrospectiveFrontier'][name]=[]
            for target in [1.,.99,.98]:
                t=choose(sub,scores,target);ts={i:t for i in scores};entry['retrospectiveFrontier'][name].append(dict(target=target,threshold=t,metrics=metrics(sub,scores,ts),cache=metrics(cache,scores,ts),warning='Evaluation-selected descriptive ranking bound; NOT deployable threshold or independent validation.'))
        entry['calibration']=dict(brier=statistics.mean((scores[r['id']]-(r['label']=='KEEP'))**2 for r in binary),crossEntropy=statistics.mean(-math.log(max(1e-7,scores[r['id']] if r['label']=='KEEP' else 1-scores[r['id']])) for r in binary))
        entry['allFalseRejectIdsAt99']=[r['id'] for r in binary if scores[r['id']]<entry['operatingPoints'][1]['thresholds'][foldof[r['id']]] and r['label']=='KEEP']
        result['candidates'].append(entry)
    # Separate the previously unaudited cache remainder from legacy references.
    unseen=[r for r in cache if r['label'] is None]
    result['baseline']['unreviewedCache']=metrics(unseen,rule,half)
    for entry in result['candidates']:
        runs=buckets[(entry['model'],entry['view'],entry['regime'])]
        scores={p['id']:p['keepScore'] for run in runs for p in run['predictions']}
        foldof={p['id']:run['fold'] for run in runs for p in run['predictions']}
        for point in entry['operatingPoints']:
            ts={i:point['thresholds'][foldof[i]] for i in scores};point['unreviewedCache']=metrics(unseen,scores,ts)
    (root/'results.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf8')
    for c in result['candidates']:
        p=c['operatingPoints'][1];print(c['model'],c['view'],c['regime'],p['slices']['combined'],'cache',p['cache']['rejectionRate'])

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);main(p.parse_args().root)
