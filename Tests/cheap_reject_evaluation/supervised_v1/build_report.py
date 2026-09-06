"""Render complete, frozen supervised experiment tables."""
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parent
r=json.loads((ROOT/'results.json').read_text())
s=json.loads((ROOT/'splits.json').read_text())
d=json.loads((ROOT/'diagnostic-results.json').read_text())
def pct(x):return '—' if x is None else f'{100*x:.2f}%'
lines=['''# Task-specific compact cheap-reject experiment

**Decision: KEEP REGEX FOR NOW.** The supervised encoders do not match the electrical-safe baseline at the required recall on employer-out evaluation. DeBERTa with body text reaches 98% recall but only 6.52% cache rejection; no validation-selected candidate reaches 99% recall. Even the retrospective per-fold oracle falls slightly below baseline rejection while accepting known KEEP losses. This is a negative result for the tested data/models/protocol, not proof that task-specific classification cannot eventually work.

This is development research only. No production behavior, RegEx rules, application dependencies, commits, generative-model inference/labeling, or second-stage work were changed. The blinded production holdout was not accessed. Read the recommendation and interpretation in `interpretation.md` alongside the experiment artifacts.

## Models and training protocol

* DeBERTa: `cross-encoder/nli-deberta-v3-small`, immutable revision `fa2804872c3b4bd748f38c0185cc85775361e735`. The NLI encoder/pooler is reused, with a fresh binary classification head. This is task-specific sequence classification, not NLI hypothesis scoring. All parameters are fine-tuned. [Upstream checkpoint](https://huggingface.co/cross-encoder/nli-deberta-v3-small/tree/fa2804872c3b4bd748f38c0185cc85775361e735).
* BERT-Tiny: `google/bert_uncased_L-2_H-128_A-2`, immutable revision `30b0a37ccaaa32f332884b96992754e246e48c5f`, two layers, hidden size 128, approximately 4.4M parameters with a new binary head. All parameters are fine-tuned. [Upstream checkpoint](https://huggingface.co/google/bert_uncased_L-2_H-128_A-2/tree/30b0a37ccaaa32f332884b96992754e246e48c5f).

Both checkpoints use safetensors and have recorded upstream and trained weight SHA-256 values. The full dependency lock is bundled. Twelve fine-tunes ran on curiosity's GTX 1070: two models, two input views, three employer folds. Windows was only the source/artifact workspace.

Protocol fixed before training: FP32; microbatch four, gradient accumulation four; AdamW, weight decay 0.01; inverse-frequency class weights from the training partition only; gradient clipping 1.0; linear learning-rate schedule with 10% warm-up. DeBERTa uses learning rate 2e-5 for eight epochs; BERT-Tiny 1e-4 for twelve. Seed 20260905 plus fold index. No subsequent hyperparameter, label, split or model changes were made in response to evaluation scores. Save the epoch with minimum unweighted validation cross entropy. Validation supplies both checkpoint selection and threshold calibration, so it is not an untouched evaluation set.

Title-only and title + body use a maximum of 384 tokens including special tokens. Preserve complete `Title:` plus title first; use remaining space for `Body:` and beginning of body. Overlong titles fail open during inference and are errors during training. No title was silently truncated. This bounded body view often omits later duties; it is not full-document understanding. The complete tokenizer strategy and truncation counts are recorded.

## Corrected data and split design

347 deduplicated real reviewed postings: 150 KEEP / 197 REJECT. Merge frozen development, stratified audit, and targeted delta audit, retaining source provenance. Correct the sole known disagreement before splitting: `d44a1bd2c6c20832`, Principal Specialist, General Finance, becomes KEEP because its duties include SQL, configuration and system support. Original files remain unchanged. These are provisional editorial labels, previously inspected during rule/zero-shot development, not independent human gold labels.

Each real row appears in exactly one outer employer-held-out evaluation fold. Inner validation employers are also absent from training. Exact normalized title overlaps are purged across partitions (none required in this snapshot); this does not establish broad occupation-family independence. The synthetic 60-row contrast set is never trained on or used for checkpoint/threshold selection. Dataset and source hashes and every partition ID are frozen in `splits.json`.

| Fold | Train KEEP / REJECT | Validation KEEP / REJECT | Evaluation KEEP / REJECT | Evaluation employers |
|---|---:|---:|---:|---|''']
for f in s['folds']:
    count=lambda k:f"{f['counts'][k].get('KEEP',0)} / {f['counts'][k].get('REJECT',0)}"
    lines.append(f"| {f['fold']} | {count('train')} | {count('validation')} | {count('evaluation')} | {', '.join(f['companies']['evaluation'])} |")
lines+=['''
Only 11–15 validation KEEP examples exist per fold. Thus 100%, >=99%, and >=98% validation recall all allow zero validation misses and select the same score boundary. This resolution limit is explicit; it is not evidence that population recall is 99%. Every evaluated row receives an out-of-sample score, but shared fold models and employer clustering mean the rows are not independent trials.

Cache: the fixed 1,503-posting snapshot was previously production-holdout-excluded. Assign each posting to the fold whose training AND validation exclude its employer. Thus no training-set score contributes to reported cache rejection. Also report the 1,156 cache postings outside all reviewed references. No full-data model is fitted or evaluated on its training examples. This cross-fitted workload estimate does not measure a single final deployment model.

The rule baseline was already designed using these provisional references; its comparison favors the rules and is not independent validation of them. On the original 166-row development set it had 100% recall, 33.73% rejection, and 13.84% full-cache rejection. On the corrected, expanded 347-row evaluation below its rejection is different because the reference mix is different.

## Validation-selected operating points

Score is the binary softmax KEEP probability; reject strictly below threshold, equality stays KEEP. For each fold and target choose the largest permissible validation KEEP-score boundary, then freeze it before outer evaluation. No threshold is adjusted to rescue outer errors. The fixed threshold grid is also reported below.

| Candidate | Validation target | Evaluation KEEP recall | False rejects / 150 | Evaluation rejection | Reject precision | Full cache rejection | Unreviewed cache rejection |
|---|---:|---:|---:|---:|---:|---:|---:|''']
b=r['baseline']
lines.append(f"| Electrical-safe | — | {pct(b['evaluation']['keepRecall'])} | {b['evaluation']['falseRejects']} | {pct(b['evaluation']['rejectionRate'])} | {pct(b['evaluation']['rejectPrecision'])} | {pct(b['cache']['rejected']/1503)} ({b['cache']['rejected']}) | {pct(b['unreviewedCache']['rejected']/1156)} ({b['unreviewedCache']['rejected']}) |")
for c in r['candidates']:
    for o in c['operatingPoints']:
        e=o['evaluation']
        lines.append(f"| {c['model']} / {c['view']} | {pct(o['validationTargetRecall'])} | {pct(e['keepRecall'])} | {e['falseRejects']} | {pct(e['rejectionRate'])} | {pct(e['rejectPrecision'])} | {pct(o['cacheRejected']/1503)} ({o['cacheRejected']}) | {pct(o['unreviewedRejected']/1156)} ({o['unreviewedRejected']}) |")
lines+=['''
## Calibration and generalization

No temperature scaling or probabilistic confidence claim. Brier score, cross entropy and ten-bin calibration error below are on employer-out predictions; the full reliability bins are in JSON. Thresholds can vary sharply between folds because tiny validation sets and unseen-employer shift change score distributions.

| Candidate | Fold 0 / 1 / 2 thresholds | Brier | Cross entropy | ECE (10 bins) |
|---|---|---:|---:|---:|''']
for c in r['candidates']:
    cal=c['calibration'];t=c['operatingPoints'][0]['thresholds']
    lines.append(f"| {c['model']} / {c['view']} | {' / '.join(f'{t[str(i)]:.6f}' for i in range(3))} | {cal['brier']:.4f} | {cal['crossEntropy']:.4f} | {cal['ece10Bins']:.4f} |")
for c in r['candidates']:
    o=c['operatingPoints'][0]
    lines.extend([f"\n### {c['model']} / {c['view']}: employer and novelty slices",'', '| Employer | KEEP n | KEEP recall | False rejects | Rejection |','|---|---:|---:|---:|---:|'])
    for name,p in o['employers'].items():
        e=p['model'];lines.append(f"| {name} | {e['keeps']} | {pct(e['keepRecall'])} | {e['falseRejects']} | {pct(e['rejectionRate'])} |")
    for name,p in o['noveltySlices'].items():
        e=p['model'];lines.append(f"\n{name}: {e['n']} rows, {e['keeps']} KEEP; recall {pct(e['keepRecall'])}, {e['falseRejects']} false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.")
    lines.append('\nRepresentative false rejects (full list in results.json):\n')
    for e in o['falseRejects'][:10]:lines.append(f"* **{e['title']}** ({e['company']}), score {e['score']:.4f}: {e['reason']}")
    if not o['falseRejects']:lines.append('* No false rejects in these 150 employer-held-out KEEP examples.')
    lines.append('\nRepresentative correctly rejected reviewed occupations:\n')
    for e in o['obviousRejects'][:6]:lines.append(f"* {e['title']} ({e['company']}), score {e['score']:.4f}.")
    lines.append('\nSynthetic KEEP recall by fold (30 KEEP each, not independent repeated evidence): '+', '.join(f"fold {x['fold']}: {pct(x['metrics']['keepRecall'])}, {x['metrics']['falseRejects']} misses" for x in o['synthetic'])+'.')
lines+=['''
## Employer names and body controls

Post-training diagnostics do not change model or threshold. Mask literal company metadata names and space-separated forms with “the employer,” preserving all other case/text. This is a limited perturbation: subsidiaries, products and employer boilerplate can remain. A separate casefold-only control diagnoses confounding in the original runner's casefold-plus-mask probe. Do not interpret masking alone as proof of no employer shortcut. Original scores replay within 1e-5.

| Candidate | Case-preserving company-mask flips / 347 | KEEP false rejects after mask | Mean absolute score change | Body-tail flips / 347 | KEEP false rejects with body tail |
|---|---:|---:|---:|---:|---:|''']
for c,diag in zip(r['candidates'],d):
    masked=diag['modes']['company_mask_preserve_case'];tail=c['operatingPoints'][0]['diagnostics']['body_tail']
    lines.append(f"| {c['model']} / {c['view']} | {masked['decisionChanges']} | {masked['evaluation']['falseRejects']} | {masked['meanAbsoluteScoreChange']:.4f} | {tail['decisionChanges']} | {tail['evaluation']['falseRejects']} |")
lines+=['''
Body-tail is a deliberate distribution shift and can contain benefits boilerplate rather than duties. Its result diagnoses input sensitivity, not an alternative selected model. See the interpretation for whether title-only suffices and how body context changes errors.

## Inference performance

Clean subprocess, one selected fold-2 checkpoint per candidate, 40 deterministically selected evaluation postings, GPU batch 16 and CPU four threads. Includes fresh tokenization and one forward pass per posting; excludes load, warm-up and training. RSS is process high-water memory including tokenizer/runtime; GPU allocated and reserved exclude some CUDA driver/context memory. The full-cache throughput in predictions.json uses all employer-out cache rows, whereas this clean benchmark uses the same selected evaluation IDs across models/views. Training memory is recorded separately.

| Candidate | Parameters | GPU batched ms/job | GPU jobs/s | GPU single median / p95 ms | GPU allocated / reserved MiB | CPU median / p95 ms | CPU sequential jobs/s | Process RSS high-water MiB |
|---|---:|---:|---:|---:|---:|---:|---:|---:|''']
for c,diag in zip(r['candidates'],d):
    b=diag['benchmark']
    lines.append(f"| {c['model']} / {c['view']} | {c['checkpoints'][0]['parameters']:,} | {b['gpuBatchMsPerPosting']:.2f} | {b['gpuThroughput']:.1f} | {b['gpuSingleMedianMs']:.2f} / {b['gpuSingleP95Ms']:.2f} | {b['peakGpuAllocatedMiB']:.1f} / {b['peakGpuReservedMiB']:.1f} | {b['cpuSingleMedianMs']:.2f} / {b['cpuSingleP95Ms']:.2f} | {b['cpuSequentialPerSecond']:.1f} | {b['processRssHighWaterAfterCpuMiB']:.1f} |")
lines+=['''
No Qwen or other generative model was run; no direct generative speed ratio is claimed. The small encoders perform one bounded forward pass. BERT-Tiny is cheap enough to be plausible operationally; classification safety, rather than its runtime, blocks replacement. Rule and encoder benchmark input mixtures differ, so avoid direct speed ratios.

## Exact trained checkpoint manifest

Checkpoints were archived and SHA-verified on curiosity at `/home/codex/jsm-lab/experiments/cheap-reject-supervised-v1-20260905/<tag>/model.safetensors`. `checkpoint-manifest.json` records retained paths; raw prediction metadata preserves original temporary paths. Full weight files are not added to the repository and are not deployed. The pinned script/data/dependency protocol allows retraining; these hashes identify the actual measured models.

| Tag | Best epoch | Training seconds | Trained safetensors SHA-256 |
|---|---:|---:|---|''']
for c in r['candidates']:
    for f in c['checkpoints']:
        lines.append(f"| {c['model']}-{c['view']}-fold{f['fold']} | {f['selectedEpoch']} | {f['training']['seconds']:.1f} | `{f['checkpointSha256']}` |")
lines+=['''
## Descriptive upper bound at zero, one and three false rejects

These points use OUTER evaluation labels to describe the ranking's best global threshold at the requested recall. They are not validation-selected recommendations, independently tested operating points, or evidence that a deployable calibrated threshold has been found. No training is repeated and no production holdout is involved.

| Candidate | Target recall | Retrospective threshold | Actual recall | False rejects | Evaluation rejection | Full cache rejection |
|---|---:|---:|---:|---:|---:|---:|''']
for c in r['candidates']:
    for f in c['optimisticEvaluationFrontier']:
        e=f['evaluation'];lines.append(f"| {c['model']} / {c['view']} | {pct(f['targetRecall'])} | {f['threshold']:.8f} | {pct(e['keepRecall'])} | {e['falseRejects']} | {pct(e['rejectionRate'])} | {pct(f['cacheRejected']/1503)} |")
lines+=['''
## Additional per-fold calibration oracle

Global thresholds above pool scores from three differently calibrated heads. To avoid mistaking calibration failure for ranking failure, a separate post-hoc oracle independently picks each fold's threshold using evaluation labels and allocates the total permitted false rejects across folds to maximize cache rejection. This is a generous upper bound for these fold score rankings. It uses evaluation labels AND cache counts, and is emphatically not deployable validation evidence.

| Candidate | Target recall | Actual false rejects | Evaluation rejection | Cache rejection |
|---|---:|---:|---:|---:|''']
for f in json.loads((ROOT/'calibration-frontier.json').read_text()):
    lines.append(f"| {f['model']} / {f['view']} | {pct(f['targetRecall'])} | {f['falseRejects']} | {pct(f['evaluationRejected']/347)} | {pct(f['cacheRejected']/1503)} |")
lines+=['''
The maximum at 98% is DeBERTa title-only, 204/1503 (13.57%), with three false rejects, versus rules 208/1503 (13.84%) with no known reviewed false rejects. A four-posting rejection difference is not meaningful evidence of superiority either way; the calibration failures and KEEP losses are the material issue. At >=99%, the best oracle rejects only 163/1503 (10.84%), with one false reject.

## Complete fixed threshold sweep

All metrics below use only employer-out evaluation predictions. This curve is descriptive, not a second tuning opportunity. Precision is for the purposive reviewed reference, not the unlabeled cache. Timings depend on model/view, not the trivial threshold operation.

| Candidate | KEEP-score threshold | KEEP recall | False rejects / 150 | Evaluation rejection | Reject precision | Full cache rejection | Unreviewed cache rejection |
|---|---:|---:|---:|---:|---:|---:|---:|''']
for c in r['candidates']:
    for f in c['sweep']:
        e=f['evaluation'];lines.append(f"| {c['model']} / {c['view']} | {f['threshold']} | {pct(e['keepRecall'])} | {e['falseRejects']} | {pct(e['rejectionRate'])} | {pct(e['rejectPrecision'])} | {pct(f['cacheRejected']/1503)} | {pct(f['unreviewedRejected']/1156)} |")
lines+=['''
## Additional human labels and confidence

For a fixed classifier and threshold, under an ideal independent sample with zero KEEP misses, the one-sided 95% binomial lower recall bound is `0.05**(1/n)`. Demonstrating at least 99% requires 299 independent KEEP examples with zero misses; 98% requires 149. With 150 KEEP and zero misses the bound is only 98.02%, before accounting for employer clustering, provisional labels, repeated model comparisons and selection bias. Actual observed misses require more examples or a better operating point.

A practical next labeling budget, if the results warrant it, is 1,000–2,000 additional human-adjudicated development examples across many more employers and adjacent technical/negative occupation families, plus a separate frozen evaluation set containing at least 300 confirmed KEEP and several hundred REJECT. Those counts are a planning recommendation, not a measured learning curve or guarantee. Maintain train/validation/test employer and title-family boundaries, duplicate checks, adjudication reasons and random prospective cache sampling. Use active sampling for training coverage only; do not use its raw rejection shares as population estimates. Do not reuse the blinded production holdout for this development cycle.

## Files and validation

Added offline files reside under `Tests/cheap_reject_evaluation/supervised_v1/`, plus this report. Original rule code, labels and application files remain unchanged. `README.md` gives replay commands; `validation-results.md` records tests and checks; `interpretation.md` provides the recommendation. No checkpoint is deployed, no production discard behavior is enabled, and no commit is created.
''']
(ROOT/'supervised-cheap-reject-experiment.md').write_bytes(('\n'.join(lines)+'\n').encode())
