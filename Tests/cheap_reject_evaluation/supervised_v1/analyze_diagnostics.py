"""Summarize post-training controls at validation-selected thresholds."""
import json
from pathlib import Path
import numpy as np
from score import metrics

ROOT=Path(__file__).resolve().parent
results=json.loads((ROOT/'results.json').read_text())
raw=json.loads((ROOT/'predictions.json').read_text())
diags=json.loads((ROOT/'diagnostics.json').read_text())
rows=[json.loads(line) for line in (ROOT/'corrected.jsonl').read_text(encoding='utf-8').splitlines()]
output=[]
for candidate in results['candidates']:
    selected=[r for r in raw['runs'] if r['model']==candidate['model'] and r['view']==candidate['view']]
    thresholds=candidate['operatingPoints'][0]['thresholds']
    original={p['id']:p['keepScore'] for r in selected for p in r['cache']}
    decision={p['id']:float(p['keepScore']>=thresholds[str(r['fold'])]) for r in selected for p in r['cache']}
    item=dict(model=candidate['model'],view=candidate['view'],modes={},benchmark=None)
    for mode in ['original','company_mask_preserve_case','casefold_control']:
        values={};keeps={}
        for run in selected:
            d=next(d for d in diags if d['tag']==run['tag'])
            if 'benchmark' in d:item['benchmark']=d['benchmark']
            values.update({p['id']:p['keepScore'] for p in d['predictions'][mode]})
            keeps.update({p['id']:float(p['keepScore']>=thresholds[str(run['fold'])]) for p in d['predictions'][mode]})
        if mode=='original':assert max(abs(values[r['id']]-original[r['id']]) for r in rows)<1e-5
        item['modes'][mode]=dict(evaluation=metrics(rows,keeps,.5),meanAbsoluteScoreChange=float(np.mean([abs(values[r['id']]-original[r['id']]) for r in rows])),
                               maxAbsoluteScoreChange=max(abs(values[r['id']]-original[r['id']]) for r in rows),decisionChanges=sum(keeps[r['id']]!=decision[r['id']] for r in rows),
                               examples=[dict(id=r['id'],title=r['title'],company=r['company'],label=r['label'],oldScore=original[r['id']],newScore=values[r['id']]) for r in rows if keeps[r['id']]!=decision[r['id']]])
    output.append(item)
(ROOT/'diagnostic-results.json').write_bytes((json.dumps(output,indent=2,ensure_ascii=False)+'\n').encode())
for c in output:print(c['model'],c['view'],[(k,v['decisionChanges'],v['evaluation']['falseRejects']) for k,v in c['modes'].items()])
