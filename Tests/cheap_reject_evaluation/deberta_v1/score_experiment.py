"""Score frozen DeBERTa predictions against existing provisional references."""
import argparse
import collections
import hashlib
import importlib
import json
import math
import sys
from pathlib import Path


def metrics(rows, keep):
    if len(rows)!=len(keep):
        raise ValueError('Prediction length mismatch')
    positives=sum(r['label']=='KEEP' for r in rows)
    fn=sum(r['label']=='KEEP' and not k for r,k in zip(rows,keep))
    rejected=sum(not k for k in keep)
    return dict(n=len(rows),keeps=positives,falseRejects=fn,rejected=rejected,
                keepRecall=(positives-fn)/positives if positives else None,
                rejectionRate=rejected/len(rows) if rows else None,
                rejectPrecision=(rejected-fn)/rejected if rejected else None)


def retains(prediction, mode, threshold, max_keep_entailment=.1):
    if prediction['failOpen']:
        return True
    if mode=='paired':
        return prediction['keepScore']>=threshold
    if mode=='entailment-gate':
        return not (prediction['rejectNli'][1]>=threshold and prediction['keepNli'][1]<=max_keep_entailment)
    raise ValueError('Unknown decision mode')


def load_rows(path):
    return [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()]


def evaluate(args):
    raw=json.loads(args.predictions.read_text(encoding='utf-8'))
    cfg=raw['protocol']
    pool=json.loads(args.pool.read_text(encoding='utf-8'))
    manifest=json.loads((args.evaluation_root/'manifest.json').read_text(encoding='utf-8'))
    canonical=hashlib.sha256(json.dumps(pool,sort_keys=True,ensure_ascii=False,separators=(',',':')).encode()).hexdigest()
    assert canonical==manifest['eligiblePoolCanonicalSha256']
    datasets={name:load_rows(args.evaluation_root/file) for name,file in {
        'development':'development.jsonl','audit':'leakage-audit.jsonl',
        'delta':'leakage-delta-audit.jsonl','synthetic':'fixtures.jsonl'}.items()}
    # Audited labels take precedence only in explicitly separate references.
    merged={r['id']:r for r in datasets['development']}
    merged.update({r['id']:r for r in datasets['audit']+datasets['delta']})
    datasets['developmentCorrected']=[merged[r['id']] for r in datasets['development']]
    datasets['reviewedUnique']=list(merged.values())
    sys.path.insert(0,str(args.evaluation_root))
    rules=importlib.import_module('leakage')
    sweep=json.loads(args.score_protocol.read_text(encoding='utf-8')) if args.score_protocol else None
    result=dict(model=cfg['model'],revision=cfg['revision'],protocol=cfg,scoreProtocol=sweep,environment=raw['environment'],
                predictionSha256=hashlib.sha256(args.predictions.read_bytes()).hexdigest(),
                scorerSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                cacheCanonicalSha256=canonical,baseline={},configurations=[],diagnostics=[],choices={})
    result['references']={name:dict(n=len(rows),keep=sum(r['label']=='KEEP' for r in rows)) for name,rows in datasets.items()}
    result['baseline']['datasets']={name:metrics(rows,[rules.decide(r,'electrical-safe')[0] for r in rows]) for name,rows in datasets.items()}
    baseline_cache=[rules.decide(r,'electrical-safe')[0] for r in pool]
    result['baseline']['cache']=dict(n=len(pool),rejected=sum(not k for k in baseline_cache),rejectionRate=sum(not k for k in baseline_cache)/len(pool))
    result['baseline']['latencyOnScoringHost']=rules.benchmark(datasets['development'],'electrical-safe')
    assert result['baseline']['cache']['rejected']==208
    expected={r['id'] for r in pool+datasets['synthetic']}
    for view,data in raw['views'].items():
        predictions={p['id']:p for p in data['predictions']}
        assert len(predictions)==len(data['predictions']) and set(predictions)==expected
        for p in predictions.values():
            assert math.isfinite(p['keepScore']) and 0<=p['keepScore']<=1
            for key in ['keepNli','rejectNli']:
                assert len(p[key])==3 and all(math.isfinite(v) and 0<=v<=1 for v in p[key])
                assert abs(sum(p[key])-1)<1e-5
        sweeps=[('paired',t) for t in (sweep or cfg)['paired_keep_thresholds']]+[('entailment-gate',t) for t in cfg['reject_entailment_thresholds']]
        for mode,threshold in sweeps:
            def keep(row):
                return retains(predictions[row['id']],mode,threshold,cfg['max_keep_entailment_for_reject_gate'])
            entry=dict(view=view,mode=mode,threshold=threshold,
                       datasets={name:metrics(rows,[keep(r) for r in rows]) for name,rows in datasets.items()},
                       cache=dict(n=len(pool),rejected=sum(not keep(r) for r in pool),rejectionRate=sum(not keep(r) for r in pool)/len(pool)),
                       timing=data['timing'],memory=data['memory'],truncation=data['truncation'])
            entry['falseRejects']=[dict(id=r['id'],title=r['title'],labelReason=r['labelReason'],
                keepScore=predictions[r['id']]['keepScore'],rejectEntailment=predictions[r['id']]['rejectNli'][1])
                for r in datasets['reviewedUnique'] if not keep(r) and r['label']=='KEEP']
            negatives=[r for r in datasets['reviewedUnique'] if not keep(r) and r['label']=='REJECT']
            negatives.sort(key=lambda r:-predictions[r['id']]['rejectNli'][1])
            entry['highScoreRejectExamples']=[dict(id=r['id'],title=r['title'],labelReason=r['labelReason'],
                keepScore=predictions[r['id']]['keepScore'],rejectEntailment=predictions[r['id']]['rejectNli'][1]) for r in negatives[:8]]
            entry['auditedLeakageFamilies']={}
            for family in sorted({r['leakageFamily'] for r in datasets['audit']}):
                rows=[r for r in datasets['audit'] if r['leakageFamily']==family and r['label']=='REJECT']
                entry['auditedLeakageFamilies'][family]=dict(reviewed=len(rows),rejected=sum(not keep(r) for r in rows))
            result['configurations'].append(entry)
    # Report both recall targets, rather than selecting an advantageous definition.
    for recall in [.98,.99]:
        eligible=[e for e in result['configurations'] if all(e['datasets'][d]['keepRecall']>=recall for d in ['development','developmentCorrected','reviewedUnique'])]
        best=max(eligible,key=lambda e:e['cache']['rejectionRate']) if eligible else None
        result['choices'][str(recall)]=None if best is None else dict(view=best['view'],mode=best['mode'],threshold=best['threshold'],cache=best['cache'],reviewed=best['datasets']['reviewedUnique'],falseRejects=best['falseRejects'])
    # Fixed mechanism examples come from the preceding audit, not new rules.
    indices={0,2,6,7,8,9,12,18,19,20,51,55,95,103,108,114,121,128,135,141,156}
    for row in datasets['audit']:
        if row['auditIndex'] in indices:
            result['diagnostics'].append(dict(id=row['id'],title=row['title'],label=row['label'],reason=row['labelReason'],baselineKeep=rules.decide(row,'electrical-safe')[0],scores={v:{k:next(p for p in data['predictions'] if p['id']==row['id'])[k] for k in ['keepScore','keepNli','rejectNli','bodyTokensUsed','truncated'] if k in next(p for p in data['predictions'] if p['id']==row['id'])} for v,data in raw['views'].items()}))
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--evaluation-root',type=Path,required=True)
    p.add_argument('--pool',type=Path,required=True)
    p.add_argument('--predictions',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--score-protocol',type=Path)
    args=p.parse_args();result=evaluate(args)
    args.output.write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    for e in result['configurations']:
        print(e['view'],e['mode'],e['threshold'],'dev',e['datasets']['development'],'unique',e['datasets']['reviewedUnique'],'cache',e['cache'],flush=True)
    print('CHOICES',json.dumps(result['choices'],indent=2),flush=True)
