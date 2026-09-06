"""Rebuild descriptive tables and paired errors from frozen out-of-sample scores."""
import csv
import json
from pathlib import Path
from score import metrics

ROOT=Path(__file__).resolve().parent


def read(name):
    return json.loads((ROOT/name).read_text(encoding='utf8'))


def pct(value):
    return 'n/a' if value is None else f'{100*value:.2f}%'


def table(headers,rows):
    return '\n'.join(['| '+' | '.join(headers)+' |','| '+' | '.join(['---']*len(headers))+' |']+
                     ['| '+' | '.join(str(x).replace('|','/') for x in row)+' |' for row in rows])


def main():
    result=read('results.json');cfg=read('protocol.json');coverage=read('evidence-coverage.json')
    corpus=[json.loads(x) for x in (ROOT/'corpus.jsonl').read_text(encoding='utf8').splitlines()]
    cache=[json.loads(x) for x in (ROOT/'cache.jsonl').read_text(encoding='utf8').splitlines()]
    binary=[r for r in corpus if r['label']!='AMBIGUOUS'];byid={r['id']:r for r in corpus+cache}
    ordered=[next(c for c in result['candidates'] if c['view']==v) for v in cfg['views']]
    baseline=next(c for c in ordered if c['view']=='prefix256')
    details={};csvrows=[];frontier=[];rule_matched=[]
    for c in ordered:
        view=c['view']
        runs=[read(f'output/deberta-small-{view}-all-fold{f}.json') for f in range(3)]
        scores={p['id']:p['keepScore'] for run in runs for p in run['predictions']}
        fold={p['id']:run['fold'] for run in runs for p in run['predictions']}
        allowed=result['baseline']['slices']['combined']['falseRejects']
        t=sorted(scores[r['id']] for r in binary if r['label']=='KEEP')[allowed]
        ts={i:t for i in scores}
        rule_matched.append(dict(representation=view,threshold=t,
                                 combined=metrics(binary,scores,ts),
                                 description=metrics([r for r in binary if r['bodyAvailable']],scores,ts),
                                 cache=metrics(cache,scores,ts),warning='Evaluation-selected diagnostic only; match rule false-reject count.'))
        for point in c['operatingPoints']:
            target=point['validationTarget'];thresholds={i:point['thresholds'][str(fold[i])] for i in scores}
            rejected=[r for r in binary if scores[r['id']]<thresholds[r['id']]]
            errors=[r for r in rejected if r['label']=='KEEP']
            cov={r['id']:r for r in coverage[view]['records']}
            details.setdefault(view,{})[str(target)]=dict(
                falseRejects=[dict(id=r['id'],title=r['title'],employer=r['employer'],bodyAvailable=r['bodyAvailable'],confidence=r['confidence'],category=r['category'],keepScore=scores[r['id']],threshold=thresholds[r['id']],evidence=cov.get(r['id'])) for r in errors],
                legacyProbes=[dict(id=r['id'],title=r['title'],label=r['label'],rejected=scores[r['id']]<thresholds[r['id']],keepScore=scores[r['id']],threshold=thresholds[r['id']]) for r in cache if r['id'] in ['legacy:40fe8e5c3da7b18b','legacy:5825a7ce39542c8d','legacy:652cbadbec892215','legacy:a80e53ba150606d2','legacy:b16454b1f005895e','legacy:c693e80c12e88b35']],
                namedTechnicalProbes=[dict(id=r['id'],title=r['title'],label=r['label'],rejected=scores[r['id']]<thresholds[r['id']],keepScore=scores[r['id']],threshold=thresholds[r['id']]) for r in binary if any(term in r['title'].casefold() for term in ['comsec','satcom','enterprise-wide applications','pc support','application development','noc/soc'])])
            for subset,m in point['slices'].items():
                csvrows.append(dict(representation=view,validationTarget=target,subset=subset,**m,cacheRejection=point['cache']['rejectionRate'],thresholds=json.dumps(point['thresholds'])))
        for subset,points in c['retrospectiveFrontier'].items():
            for point in points:
                frontier.append(dict(representation=view,subset=subset,target=point['target'],threshold=point['threshold'],**point['metrics'],cacheRejection=point['cache']['rejectionRate'],warning='Evaluation-selected diagnostic only'))
    for target in ['1.0','0.99','0.98']:
        base={r['id'] for r in details['prefix256'][target]['falseRejects']}
        for view in cfg['views']:
            errors={r['id'] for r in details[view][target]['falseRejects']}
            details[view][target]['pairedBaseline']=dict(recoveredBaselineFalseRejectIds=sorted(base-errors),newFalseRejectIds=sorted(errors-base),persistentFalseRejectIds=sorted(base&errors))
    (ROOT/'paired-error-analysis.json').write_text(json.dumps(details,indent=2)+'\n',encoding='utf8')
    (ROOT/'rule-matched-diagnostic.json').write_text(json.dumps(rule_matched,indent=2)+'\n',encoding='utf8')
    for name,records in [('selected-thresholds.csv',csvrows),('diagnostic-frontiers.csv',frontier)]:
        with (ROOT/name).open('w',newline='',encoding='utf8') as f:
            writer=csv.DictWriter(f,fieldnames=list(records[0]));writer.writeheader();writer.writerows(records)
    grid=[]
    for c in ordered:
        for point in c['sweep']:
            for subset,m in point['slices'].items():
                grid.append(dict(representation=c['view'],threshold=point['threshold'],subset=subset,**m,cacheRejection=point['cache']['rejectionRate'],warning='Fixed threshold diagnostic; not validation-selected'))
    with (ROOT/'fixed-threshold-sweep.csv').open('w',newline='',encoding='utf8') as f:
        w=csv.DictWriter(f,fieldnames=list(grid[0]));w.writeheader();w.writerows(grid)
    text=['# Input-representation experiment',
          'All results are employer-held-out development results against provisional Codex labels. No training-set scores are presented. This is a focused follow-up on reused development folds, not a new independent test.',
          '## Frozen data and training',
          'Same 3,198-posting corpus as supervised_v2: 2,669 binary examples (2,121 KEEP / 548 REJECT); 529 AMBIGUOUS excluded from training/calibration. Binary described: 1,159 (914 KEEP); binary title-only: 1,510 (1,207 KEEP). Exact corpus/cache/split hashes are in baseline-provenance.json. The old 1,503-posting cache is scored by employer-out models; its 347 older labeled references are stress probes, not training data.',
          'Model: cross-encoder/nli-deberta-v3-small @ fa2804872c3b4bd748f38c0185cc85775361e735; initial safetensors SHA256 ebc79588dd73ccfb6a3f6078519cfbf512c5305384c5ea1845bc71cd32216e86. Fresh binary head, full encoder fine-tuning, 141,896,450 parameters. Fifteen new fits (five inputs × three folds) plus three reused prefix256 fits. No BERT-Tiny rerun.',
          'All-data regime only; train-only inverse-frequency class weights; FP32; four epochs; AdamW lr=2e-5, weight decay=.01; batch 4 × accumulation 4; 10% warmup; clip 1; seeds 20260905+fold. Lowest unweighted validation cross entropy selects each checkpoint. See README.md and inputs.py for exact deterministic span construction. Inputs never use labels or adjudication evidence.',
          'Employer/lexical-title-family/duplicate purging is unchanged. Fold train/validation/evaluation counts: 700/481/1441, 1595/351/592, 1830/83/636. Validation KEEP counts are 392, 327, and 42; the last fold cannot permit one false reject at either 99% or 98%. Broad semantic-family generalization remains unproven.',
          '## Evidence coverage',
          'Denominator: 1,159 described binary examples. Any overlap can be only a fragment. Full coverage means all recorded reviewed-evidence characters, not all relevant content in the job. Audit spans never construct model inputs.',
          table(['Input','Any evidence','At least half','All evidence','Mean fraction'],[[v]+[pct(coverage[v]['binary'][k]) for k in ['anyEvidence','halfEvidence','fullEvidence','meanFraction']] for v in cfg['views']]),
          'All 1,312 described postings, including AMBIGUOUS (audit only):',
          table(['Input','Any evidence','At least half','All evidence','Mean fraction'],[[v]+[pct(coverage[v]['allDescribed'][k]) for k in ['anyEvidence','halfEvidence','fullEvidence','meanFraction']] for v in cfg['views']]),
          'The JSON also reports each class separately and per-posting selected source ranges. Recorded evidence is concentrated toward the beginning: 1,111/1,159 described binary postings have every recorded span within the first half of the body. Median body length is 5,500 characters and median last-evidence position is 30.1% of body length. These are properties of the adjudication record, not proof that later text is irrelevant.',
          '## Validation-selected comparisons',
          'Reject iff KEEP probability is strictly below the fold threshold; ties KEEP. Thresholds come only from inner validation KEEP-score boundaries. All outer evaluation scores stay untouched. The 100% validation target is the conservative default inherited from v2; 99% and 98% are separate predeclared alternatives, not guarantees on evaluation recall.']
    headers=['Input','Val target','KEEP recall','Described recall','Title-only recall','FN','Eval reject','Old-cache reject','Reject precision']
    r=result['baseline'];m=r['slices'];table_rows=[['electrical-safe','fixed',pct(m['combined']['keepRecall']),pct(m['description']['keepRecall']),pct(m['title_only']['keepRecall']),m['combined']['falseRejects'],pct(m['combined']['rejectionRate']),pct(r['cache']['rejectionRate']),pct(m['combined']['rejectPrecision'])]]
    for c in ordered:
        for p in c['operatingPoints']:
            s=p['slices'];table_rows.append([c['view'],pct(p['validationTarget']),pct(s['combined']['keepRecall']),pct(s['description']['keepRecall']),pct(s['title_only']['keepRecall']),s['combined']['falseRejects'],pct(s['combined']['rejectionRate']),pct(p['cache']['rejectionRate']),pct(s['combined']['rejectPrecision'])])
    text.append(table(headers,table_rows))
    text += ['### Fold thresholds',table(['Input','Val target','Fold 0','Fold 1','Fold 2'],[[c['view'],pct(p['validationTarget'])]+[f"{p['thresholds'][str(f)]:.9f}" for f in range(3)] for c in ordered for p in c['operatingPoints']]),
             '### Conservative threshold confidence and cache slices',
             table(['Input','High-confidence recall','Medium-confidence recall','Unreviewed cache reject','AMBIGUOUS reject'],[[c['view'],pct(c['operatingPoints'][0]['slices']['confidence_high']['keepRecall']),pct(c['operatingPoints'][0]['slices']['confidence_medium']['keepRecall']),pct(c['operatingPoints'][0]['unreviewedCache']['rejectionRate']),pct(c['operatingPoints'][0]['ambiguous']['rejectionRate'])] for c in ordered]),
             'AMBIGUOUS rejection is a review signal, not measured accuracy. The unreviewed cache remainder has 1,156 postings. All subset/threshold rows are in selected-thresholds.csv.',
             '## Diagnostic retrospective frontiers — NOT deployment thresholds',
             'The following global thresholds use evaluation labels and therefore measure ranking potential only. They cannot be selected for production or cited as independently validated operating points.',
             table(['Input','Diagnostic target','Threshold','Eval recall','FN','Eval reject','Cache reject'],[[c['view'],pct(p['target']),f"{p['threshold']:.9f}",pct(p['metrics']['keepRecall']),p['metrics']['falseRejects'],pct(p['metrics']['rejectionRate']),pct(p['cache']['rejectionRate'])] for c in ordered for p in c['retrospectiveFrontier']['combined']]),
             'diagnostic-frontiers.csv separately includes described, title-only, and confidence slices. fixed-threshold-sweep.csv contains the entire fixed threshold grid. Neither selects a deployable threshold.',
             '### Diagnostic comparison at the rule false-reject count',
             'A separate evaluation-selected threshold allows the same nine false rejects as electrical-safe. This is not a proposed threshold; it checks whether the apparent ranking gains persist at the rule baseline\'s observed recall.',
             table(['Input','Diagnostic threshold','KEEP recall','Described recall','FN','Cache reject'],[[r['representation'],f"{r['threshold']:.9f}",pct(r['combined']['keepRecall']),pct(r['description']['keepRecall']),r['combined']['falseRejects'],pct(r['cache']['rejectionRate'])] for r in rule_matched]),
             '## Inference performance',
             'Fresh process per representation; same deterministic 40-posting fold-2 sample; GTX 1070 8 GB, i7-4770K, four CPU threads. Includes complete body tokenization and span selection. GPU batch=16 and batch-one measurements; CPU batch-one. Memory is measured allocated tensor memory plus reserved allocator memory, not total system/GPU-driver consumption. This small benchmark is comparative, not a production load test.']
    perf=[]
    for c in ordered:
        b=read(f"output/deberta-small-{c['view']}-all-fold2-benchmark.json")
        perf.append([c['view'],f"{b['gpuBatchMsPerPosting']:.2f}",f"{b['gpuThroughput']:.1f}",f"{b['gpuSingleMedianMs']:.2f}",f"{b['gpuPeakAllocatedMiB']:.0f}/{b['gpuPeakReservedMiB']:.0f}",f"{b['cpuSingleMedianMs']:.1f}",f"{b['cpuThroughput']:.1f}",f"{b['processPeakRssAfterCpuMiB']:.0f}"])
    text += [table(['Input','GPU ms/post','GPU post/s','GPU single ms','GPU allocated/reserved MiB','CPU single ms','CPU post/s','RSS MiB'],perf),
             'Electrical-safe prior same-host measurement: 0.0913 ms/post, approximately 10,949 postings/s. No Qwen inference or matched Qwen benchmark was run, so no measured cost ratio to Qwen is claimed.',
             '## Errors at the conservative validation target',
             'The paired analysis uses separately validation-calibrated thresholds for each representation; it measures the whole trained configuration, not a causal span-only perturbation. Full error IDs, scores, source spans and recovered/new errors are in paired-error-analysis.json.']
    for c in ordered:
        view=c['view'];p=c['operatingPoints'][0];d=details[view]['1.0'];pair=d['pairedBaseline']
        text += [f'### {view}',f"False rejects: {len(d['falseRejects'])}. Recovered prefix256 false rejects: {len(pair['recoveredBaselineFalseRejectIds'])}; new false rejects: {len(pair['newFalseRejectIds'])}.",
                 table(['False-rejected title','Employer','Body','Confidence','P(KEEP)','Threshold'],[[r['title'],r['employer'],r['bodyAvailable'],r['confidence'],f"{r['keepScore']:.6f}",f"{r['threshold']:.6f}"] for r in d['falseRejects'][:16]]),
                 'Representative low-KEEP-probability rejects that agree with the provisional REJECT labels:',
                 table(['Title','Employer','P(KEEP)'],[[r['title'],r['employer'],f"{r['keepScore']:.6f}"] for r in p['obviousRejects'][:6]])]
    categories=['software','infrastructure-support','cybersecurity','engineering','technical-leadership','data-ai']
    categories=[k for k in categories if k in ordered[0]['operatingPoints'][0]['categories']]
    text += ['## Recorded evidence in remaining errors',
             table(['Input','Described false rejects','No evidence overlap','At least half included','All evidence included'],[[view,len([r for r in details[view]['1.0']['falseRejects'] if r['bodyAvailable']])]+[sum(bool(r['evidence'][key]) if key!='anyEvidence' else not r['evidence'][key] for r in details[view]['1.0']['falseRejects'] if r['bodyAvailable']) for key in ['anyEvidence','halfEvidence','fullEvidence']] for view in cfg['views']]),
             'These are errors against provisional labels. Seeing all recorded evidence does not establish that the label is correct, that the model attended to it, or that the recorded excerpt contains every relevant fact. It does show that missing recorded evidence alone cannot explain those decisions.',
             '## Technical families and employer stability',
             table(['Input']+categories,[[c['view']]+[pct(c['operatingPoints'][0]['categories'][k]['keepRecall']) for k in categories] for c in ordered]),
             'These are frozen provisional label categories, not new rejection rules. Named COMSEC/SATCOM/enterprise-application/PC-support probes are preserved in paired-error-analysis.json. Results include six unchanged older technical stress probes scored strictly employer-out.',
             table(['Input','Employer-mask flips','Masked KEEP recall','Mean score change'],[[c['view'],c['operatingPoints'][0]['diagnostics']['company_masked']['decisionFlips'],pct(c['operatingPoints'][0]['diagnostics']['company_masked']['slices']['combined']['keepRecall']),f"{c['operatingPoints'][0]['diagnostics']['company_masked']['meanAbsoluteScoreChange']:.4f}"] for c in ordered]),
             'Employer masking is an evaluation-only sensitivity test and cannot prove absence of boilerplate or employer proxies. Per-employer metrics and calibration Brier/cross-entropy are in results.json. Training/checkpoint history, all three folds, and GPU peaks are preserved per run.',
             '## Interpretation and validation',
             'See interpretation.md for the decision and detailed reading of the completed results. See validation-results.md for executed checks. No deployment or commit is part of this experiment.']
    (ROOT/'report.md').write_text('\n\n'.join(text)+'\n',encoding='utf8')


if __name__=='__main__':
    main()
