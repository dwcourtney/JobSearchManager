"""Retrospective per-fold oracle: separate ranking potential from calibration failure.

Uses evaluation labels and cache counts. NOT an operating point or validation.
"""
import itertools
import json
import math
from pathlib import Path
from score import metrics

ROOT=Path(__file__).resolve().parent
raw=json.loads((ROOT/'predictions.json').read_text())
split=json.loads((ROOT/'splits.json').read_text())
rows={r['id']:r for r in map(json.loads,(ROOT/'corrected.jsonl').read_text(encoding='utf-8').splitlines())}
result=[]
for model in raw['protocol']['models']:
    for view in raw['protocol']['views']:
        runs=[r for r in raw['runs'] if r['model']==model and r['view']==view]
        options=[]
        for run in runs:
            ev=[rows[i] for i in split['folds'][run['fold']]['partitions']['evaluation']]
            scores={p['id']:p['keepScore'] for p in run['cache']}
            positives=sorted(scores[r['id']] for r in ev if r['label']=='KEEP')
            choices=[]
            for allowed in range(4):
                t=positives[allowed]
                choices.append(dict(fold=run['fold'],threshold=t,evaluation=metrics(ev,scores,t),cacheRejected=sum(p<t for p in scores.values())))
            options.append(choices)
        for recall in [1.,.99,.98]:
            allowed=math.floor((1-recall)*150+1e-9)
            feasible=[combo for combo in itertools.product(*options) if sum(c['evaluation']['falseRejects'] for c in combo)<=allowed]
            best=max(feasible,key=lambda combo:sum(c['cacheRejected'] for c in combo))
            fn=sum(c['evaluation']['falseRejects'] for c in best)
            result.append(dict(model=model,view=view,targetRecall=recall,falseRejects=fn,keepRecall=(150-fn)/150,
                               evaluationRejected=sum(c['evaluation']['rejected'] for c in best),cacheRejected=sum(c['cacheRejected'] for c in best),folds=best))
(ROOT/'calibration-frontier.json').write_bytes((json.dumps(result,indent=2)+'\n').encode())
for r in result:print(r['model'],r['view'],r['targetRecall'],r['falseRejects'],r['cacheRejected'],r['cacheRejected']/1503)
