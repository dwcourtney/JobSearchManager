"""Render complete metrics from frozen results; no fitting or threshold selection."""
import csv,json,statistics
from pathlib import Path
ROOT=Path(__file__).resolve().parent
def pct(x):return '—' if x is None else f'{100*x:.2f}%'
def main():
    results=json.loads((ROOT/'results.json').read_text());protocol=json.loads((ROOT/'protocol.json').read_text());split=json.loads((ROOT/'splits.json').read_text())
    lines=['# Supervised cheap triage: 3,198-posting corpus','',
    'Offline experiment only. No production changes, new triage rules, generative inference, training on AMBIGUOUS, production holdout access, deployment, or commits. See interpretation.md for the recommendation and error analysis.','',
    '## Data and protocol','',
    'Source: machine_corpus_v1/machine-labeled.jsonl. All 3,198 frozen Codex decisions retained: 2,121 KEEP, 548 REJECT, 529 AMBIGUOUS. Only 2,669 binary rows are eligible for fitting/calibration/evaluation: 1,159 described (914 KEEP / 245 REJECT), 1,510 title-only (1,207 KEEP / 303 REJECT). The 1,014 high and 1,655 medium labels exactly comprise this binary subset; all 529 low labels are AMBIGUOUS. Thus design B (high/medium) and C (all binary) are exactly identical. No artificial extra B run is claimed.','',
    'A/description: train only described rows. B=C/all: train all binary rows. D/weighted: train all binary rows with quality multiplier described/high=1, described/medium=0.75, title-only/medium=0.25. Every regime also uses train-only inverse-frequency class weights; combined weights normalize to mean one. All regimes share unweighted binary validation and identical evaluation sets to isolate training treatment. A therefore means description-only training, not description-only calibration; described evaluation is reported separately.','',
    'Three outer employer-held-out folds, with different employers for inner validation. Purge connected corpus lexical title-family, duplicate-cluster, exact-title and identical nonempty-body components across partitions. These lexical groups are not guaranteed semantic occupation families. Missing-body deduplication remains limited. Every binary corpus row is evaluated once, never on a training model. All corpus rows including AMBIGUOUS belonging to the outer employers supply evaluation-side purge groups.','',
    '| Fold | Evaluation employers | Train total / described | Validation KEEP | Evaluation KEEP / REJECT |','|---|---|---:|---:|---:|']
    for f in split['folds']:
        c=f['counts'];lines.append(f"| {f['fold']} | {', '.join(f['evaluationEmployers'])} | {c['train']['total']} / {sum(c['train']['described'].values())} | {c['validation']['labels']['KEEP']} | {c['evaluation']['labels']['KEEP']} / {c['evaluation']['labels']['REJECT']} |")
    lines+=['','Models and revisions:']
    for k,m in protocol['models'].items():lines.append(f"- {k}: `{m['name']}@{m['revision']}`, {m['epochs']} epochs, learning rate {m['learningRate']}. {m['initialization']}")
    lines+=['','FP32 on curiosity GTX 1070; seed 20260905 + fold; AdamW weight decay 0.01, microbatch 4, accumulation 4, clipping 1.0, linear schedule with 10% warmup. Fresh binary head and all encoder parameters fine-tuned. Lowest validation cross entropy selects checkpoint. Exactly 36 fits; epochs/hyperparameters fixed before outer results.','',
    'Title preserved first, then beginning of body within 256 tokens including prefixes/special tokens. Missing body is empty; no imputation. Overlong titles fail open during inference and abort fitting. No occupational extraction. Body truncation can omit the same duties that Codex used during adjudication.','',
    'Labels are provisional machine judgments, not independent human truth. Title-only labels are weak; the 281-row human review queue has not been adjudicated by humans. Confidence buckets are not calibrated probabilities. Employer concentration and label ambiguity limit inference; these data cannot certify population 98–99% recall.','',
    '## Baseline on the same subsets','', '| Subset | n | KEEP recall | False rejects | Rejection | Reject precision |','|---|---:|---:|---:|---:|---:|']
    b=results['baseline']
    for name,m in b['slices'].items():lines.append(f"| {name} | {m['n']} | {pct(m['keepRecall'])} | {m['falseRejects']} | {pct(m['rejectionRate'])} | {pct(m['rejectPrecision'])} |")
    lines+=['',f"Original 1,503-posting cache: {pct(b['cache']['rejectionRate'])} rejection. New 3,198 corpus including AMBIGUOUS: {pct(b['corpus']['rejectionRate'])}. Rule timing on Windows: {b['timing']['msPerPosting']:.4f} ms/posting. Hardware differs from encoder GPU timing.", '',
    '## Validation-selected operating points','',
    'KEEP probability is the score. Reject strictly below threshold; ties survive. Per fold, choose the largest validation KEEP-score boundary allowing floor((1-target)*KEEP_n) validation misses. Freeze it before outer evaluation. Actual outer recall may miss its target. Fold 2 has only 42 validation KEEP examples: both 99% and 98% allow zero validation misses. Validation also selects checkpoints, so it is not untouched evaluation.','',
    '| Model / view / training | Validation target | Outer KEEP recall | False rejects | Outer rejection | Reject precision | Old cache rejection | New corpus rejection |','|---|---:|---:|---:|---:|---:|---:|---:|']
    for c in results['candidates']:
        for p in c['operatingPoints']:
            m=p['slices']['combined'];lines.append(f"| {c['model']} / {c['view']} / {c['regime']} | {pct(p['validationTarget'])} | {pct(m['keepRecall'])} | {m['falseRejects']} | {pct(m['rejectionRate'])} | {pct(m['rejectPrecision'])} | {pct(p['cache']['rejectionRate'])} | {pct(p['corpus']['rejectionRate'])} |")
    lines+=['','Old cache scores are cross-fitted by employer: each posting is scored only by the fold excluding its employer from training and validation. The prior 347 labeled references in that cache are evaluation-only stress cases, never fitting/calibration data. Their old occupational definitions differ from the new corpus, so comparisons are diagnostic. Cache rejection is not validated safe workload reduction. New corpus workload includes AMBIGUOUS, whose rejected fraction must be audited. No final full-data model was fitted.','',
    '## Per-candidate slices and errors','']
    for c in results['candidates']:
        lines+= [f"### {c['model']} / {c['view']} / {c['regime']}",'','| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |','|---|---|---:|---:|---:|---:|---:|']
        for p in c['operatingPoints']:
            for name,m in p['slices'].items():lines.append(f"| {pct(p['validationTarget'])} | {name} | {m['keeps']} | {pct(m['keepRecall'])} | {m['falseRejects']} | {pct(m['rejectionRate'])} | {pct(m['rejectPrecision'])} |")
        p=c['operatingPoints'][1];lines+=['',f"99%-target fold thresholds: {p['thresholds']}. Outer Brier: {c['calibration']['brier']:.4f}; cross entropy: {c['calibration']['crossEntropy']:.4f}. These probabilities are not calibrated confidence.",f"AMBIGUOUS rejection: {pct(p['ambiguous']['rejectionRate'])}. Prior 347-reference KEEP recall: {pct(p['legacy']['keepRecall'])}.", '', 'False rejects at the validation 99% target:','']
        for r in p['falseRejects'][:10]:lines.append(f"- {r['title']} ({r['employer']}; {r['confidence']}; {'described' if r['bodyAvailable'] else 'title-only'}): score {r['keepScore']:.5f} < {r['threshold']:.5f}. {r['reason']}")
        if not p['falseRejects']:lines.append('- None against these provisional labels.')
        lines+=['','Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):','']
        for r in p['obviousRejects'][:5]:lines.append(f"- {r['title']} ({r['employer']}): score {r['keepScore']:.5f}; label confidence {r['confidence']}.")
        lines+=['','Core technical KEEP category results at 99%-validation target:','']
        for cat in ['software','infrastructure-support','cybersecurity','engineering','data-ai','technical-leadership']:
            if cat in p['categories']:
                m=p['categories'][cat];lines.append(f"- {cat}: {m['keeps']} KEEP, {m['falseRejects']} false rejects, {pct(m['keepRecall'])} recall.")
        for mode,d in p['diagnostics'].items():lines.append(f"- {mode}: {d['decisionFlips']} decision flips; mean absolute score shift {d['meanAbsoluteScoreChange']:.4f}; combined recall {pct(d['slices']['combined']['keepRecall'])}.")
        lines+=['','Timing across employer folds (tokenization included):','']
        n=sum(f['gpuTiming']['n'] for f in c['folds']);sec=sum(f['gpuTiming']['totalSeconds'] for f in c['folds'])
        lines.append(f"- Full cross-fit stream: {1000*sec/n:.3f} ms/posting, {n/sec:.1f} postings/second. Single-post median by fold: {[round(f['gpuTiming']['singleMedianMs'],2) for f in c['folds']]}. Maximum training allocated GPU MiB: {max(f['training']['peakGpuAllocatedMiB'] for f in c['folds']):.1f}.")
        tag=f"{c['model']}-{c['view']}-{c['regime']}-fold2";benchpath=ROOT/'output'/(tag+'-benchmark.json')
        if benchpath.exists():
            x=json.loads(benchpath.read_text());lines.append(f"- Clean-process benchmark, 40 outer rows: GPU {x['gpuBatchMsPerPosting']:.3f} ms/posting / {x['gpuThroughput']:.1f} per second; batch-one median {x['gpuSingleMedianMs']:.2f} ms; peak allocated/reserved {x['gpuPeakAllocatedMiB']:.1f}/{x['gpuPeakReservedMiB']:.1f} MiB. CPU four-thread median {x['cpuSingleMedianMs']:.2f} ms / {x['cpuThroughput']:.1f} per second; process high-water RSS after CPU {x['processPeakRssAfterCpuMiB']:.1f} MiB.")
        lines+=['','Retrospective combined ranking bounds, NOT usable deployment thresholds:','']
        for f in c['retrospectiveFrontier']['combined']:lines.append(f"- {pct(f['target'])} empirical recall constraint: threshold {f['threshold']:.6f}, {f['metrics']['falseRejects']} false rejects, {pct(f['metrics']['rejectionRate'])} evaluation rejection, {pct(f['cache']['rejectionRate'])} cache rejection.")
        lines.append('')
    lines+=['## Full sweep and reproducibility','', 'threshold-sweep.csv includes the complete fixed grid with all evidence/confidence slices and original-cache rejection. results.json additionally contains per-employer/category metrics, validation-selected points and retrospective slice-specific frontiers. These retrospective frontiers are evaluation-selected and cannot establish generalization.', '', 'See README.md for reproduction commands, validation-results.md for checks and file list, and interpretation.md for the final recommendation.']
    probes={'legacy:40fe8e5c3da7b18b':'Controls engineer','legacy:5825a7ce39542c8d':'Kahua developer','legacy:652cbadbec892215':'PC support','legacy:a80e53ba150606d2':'Enterprise systems','legacy:b16454b1f005895e':'Mechanical engineer','legacy:c693e80c12e88b35':'Cybersecurity PM'}
    lines+=['','## Previously failing technical probes','', 'All six are fixed legacy KEEP references, excluded from fitting and calibration. These are diagnostic cases, not a separately calibrated benchmark.']
    for point_index in [0,1]:
        lines+=['',f"Validation target: {pct(results['candidates'][0]['operatingPoints'][point_index]['validationTarget'])}",'', '| Candidate | '+' | '.join(probes.values())+' |','|---|'+'---|'*len(probes)]
        for c in results['candidates']:
            p=c['operatingPoints'][point_index];decisions={}
            for f in range(3):
                run=json.loads((ROOT/'output'/f"{c['model']}-{c['view']}-{c['regime']}-fold{f}.json").read_text())
                for z in run['predictions']:
                    if z['id'] in probes:decisions[z['id']]='REJECT' if z['keepScore']<p['thresholds'][str(f)] else 'KEEP'
            lines.append(f"| {c['model']} / {c['view']} / {c['regime']} | "+' | '.join(decisions[i] for i in probes)+' |')
    lines+=['','## Input evidence alignment','']
    auditpath=ROOT/'input-audit.json'
    if auditpath.exists():
        for k,a in json.loads(auditpath.read_text()).items():lines.append(f"- {k}: {a['truncated']}/{a['describedBinary']} described binary inputs truncated; all recorded adjudication spans outside the body prefix for {a['allReviewedEvidenceOutside']} rows ({pct(a['allReviewedEvidenceOutside']/a['describedBinary'])}). This measures overlap with recorded evidence, not whether the remaining text is sufficient.")
    rp=ROOT/'output/rules-benchmark.json'
    if rp.exists():
        rb=json.loads(rp.read_text());lines+=['','## Same-host rule timing','',f"Unchanged electrical-safe rules on curiosity: {rb['msPerPosting']:.4f} ms/posting, {rb['postingsPerSecond']:.1f} postings/second (median of five full passes). Every decision matches the Windows frozen baseline; both source hashes match. Compare with the four-thread CPU encoder benchmarks above; GPU throughput is a separate hardware path."]
    (ROOT/'report.md').write_text('\n'.join(lines)+'\n',encoding='utf8')
    with (ROOT/'threshold-sweep.csv').open('w',newline='',encoding='utf8') as f:
        fields=['model','view','regime','threshold','subset','n','keeps','keepRecall','falseRejects','rejectionRate','rejectPrecision','cacheRejectionRate'];w=csv.DictWriter(f,fieldnames=fields);w.writeheader()
        for c in results['candidates']:
            for p in c['sweep']:
                for sub,m in p['slices'].items():w.writerow({**{k:c[k] for k in ['model','view','regime']},'threshold':p['threshold'],'subset':sub,**{k:m[k] for k in ['n','keeps','keepRecall','falseRejects','rejectionRate','rejectPrecision']},'cacheRejectionRate':p['cache']['rejectionRate']})
if __name__=='__main__':main()
