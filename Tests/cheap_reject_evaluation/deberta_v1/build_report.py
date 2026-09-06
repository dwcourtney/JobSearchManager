"""Render the recorded experiment, including every threshold and diagnostic."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
m = json.loads((ROOT/'metrics-extended.json').read_text())
frontiers = json.loads((ROOT/'frontier.json').read_text())
names = {'title':'Title', 'title_body_head':'Title + body head', 'title_body_head_tail':'Title + body head/tail'}
def pct(x):
    return '—' if x is None else f'{100*x:.2f}%'

lines = ['''# DeBERTa first-layer cheap-reject experiment

Recommendation: **keep RegEx for now**, still in shadow/audit mode. This off-the-shelf encoder and binary hypothesis formulation do not replace the electrical-safe rules at the required recall. No production changes, new occupational regex rules, fine-tuning, Qwen, generative inference, holdout tuning, commits, or second-stage work were performed.

## Model and design

Model: [`cross-encoder/nli-deberta-v3-small`](https://huggingface.co/cross-encoder/nli-deberta-v3-small/tree/fa2804872c3b4bd748f38c0185cc85775361e735), revision `fa2804872c3b4bd748f38c0185cc85775361e735`. Safetensors SHA-256: `ebc79588dd73ccfb6a3f6078519cfbf512c5305384c5ea1845bc71cd32216e86`. The model card describes SNLI/MultiNLI training and Apache-2.0 licensing. This is an NLI-trained six-layer DeBERTa-v3 encoder, 141,897,219 parameters including its large embedding table, not a generative model. Merely loading an unfine-tuned base encoder would not provide an NLI classification head.

Inference ran on curiosity, GTX 1070 8 GB, FP32, PyTorch 2.5.1+cu121, Transformers 4.46.3, Python 3.12.3, four CPU threads. The exact dependency lock, protocols, inputs, predictions, scripts, and results are in `Tests/cheap_reject_evaluation/deberta_v1/`. Weights are fetched at an immutable revision and verified. No posting text was sent to a model API.

The two fixed NLI hypotheses are:

* KEEP: “This job involves software development, computer systems, information technology, automation, or related technical engineering and support.”
* REJECT: “This job is unrelated to software development, computer systems, information technology, automation, and related technical engineering and support.”

KEEP retains plausible software, .NET/C#, full stack/backend, DevOps/platform/cloud, systems/integration, AI/ML, automation, infrastructure/admin and ambiguous adjacent engineering roles. REJECT requires unrelated primary duties. Employer, product, office-software use, seniority, clearance, location and final desirability are not sufficient rejection evidence. The hypothesis is an approximate natural-language representation of this target; its adequacy is a limitation of this experiment.

Inputs: title only; title plus beginning of body; title plus body beginning/end. Reserve both hypotheses and special tokens within 512 tokens, preserve the entire title, then fill the remaining body budget deterministically. Head/tail divides the budget equally, giving an odd extra token to the head. An overlong title fails open to KEEP. There is no occupation-specific parsing. 1,501/1,503 real bodies require truncation in both body views; all 60 short synthetic examples fit. No title overflows. Head/tail omits middle duties and can expose benefits/boilerplate instead, so this is not a full-document test.

Paired score = softmax over the two hypotheses' entailment logits, KEEP component. Reject only when score is strictly below the threshold. These scores are **not calibrated suitability probabilities**. Separately, an absolute NLI gate rejects only if negative entailment meets its threshold and positive entailment is at most 0.1. Labels 0/1/2 are contradiction/entailment/neutral and were verified against the checkpoint and sanity examples.

The original threshold grid ended at 0.5. Scores saturated above it, so a supplemental development sweep through 0.9999 was explicitly recorded using the same frozen predictions. No hypothesis changes or repeat inference were made. The final 72 configurations comprise 19 paired thresholds and five absolute gates across three views. A further label-informed frontier checks whether grid gaps conceal a useful operating point; it is an optimistic bound, not independently validated performance.

## References and limitations

Reuse the frozen 166-row development set (85 KEEP / 81 REJECT), 196-row stratified audit (74/122), 41-row targeted delta audit (1/40), 60 synthetic contrasts (30/30), and 1,503 previously holdout-excluded cache postings. No new holdout access. Audits overlap development; deduplicating and giving audited labels precedence yields 347 real reviewed rows (150 KEEP / 197 REJECT). These provisional editorial labels and biased audit samples are not independent human ground truth or population accuracy estimates.

One previously documented disagreement is preserved: Principal Specialist, General Finance is REJECT in frozen development but KEEP in the audit because of SQL, configuration and system-support duties. Corrected development is separately reported as 86 KEEP / 80 REJECT. Threshold eligibility requires the recall target on original development, corrected development, and the deduplicated review. Synthetic metrics are separate and expose additional failures; they were not folded into threshold selection.

Real-cache rejection is an exact count on this historical snapshot, not an estimate of correct rejection or guaranteed future LLM savings. Zero misses among 150 reviewed positives does not establish 99% population recall (even an independent random sample would need substantially more evidence). Audit strata also favor testing the existing rules. No causal employer/body ablation or extensive checkpoint/hypothesis search was performed.

## Main comparison

| Method | Dev KEEP recall | Dev false rejects | Dev rejection | Reviewed KEEP recall (FN/150) | Cache rejection |
|---|---:|---:|---:|---:|---:|
| Electrical-safe | 100% | 0/85 | 33.73% | 100% (0) | 13.84% (208/1503) |''']
for view, threshold in [('title',.5),('title',.9),('title_body_head',.9),('title_body_head',.95),('title_body_head_tail',.9)]:
    c = next(c for c in m['configurations'] if c['view']==view and c['mode']=='paired' and c['threshold']==threshold)
    d,u=c['datasets']['development'],c['datasets']['reviewedUnique']
    lines.append(f"| {names[view]} < {threshold} | {pct(d['keepRecall'])} | {d['falseRejects']}/85 | {pct(d['rejectionRate'])} | {pct(u['keepRecall'])} ({u['falseRejects']}) | {pct(c['cache']['rejectionRate'])} ({c['cache']['rejected']}/1503) |")
lines += ['''
The best discrete setting meeting both recall objectives is title + body head at 0.9: 61 cache rejects (4.06%), no false rejects among 150 reviewed KEEP, only 18 of 197 reviewed REJECT removed. It also falsely rejects the synthetic **Systems Integration Engineer** (score 0.8414): 29/30 synthetic KEEP survive. Electrical-safe retains all 30. Thus even the best discrete setting is not ready for production discard.

Every absolute NLI gate rejects **zero cache jobs**. There are no genuinely high-confidence cache rejects by the tested negative-entailment criterion. Calling thresholded paired-score decisions “high-confidence” would be misleading.

## Optimistic threshold frontier

For each recall target, sort KEEP scores in each real reference and take the most restrictive maximal permissible threshold (equality stays KEEP). This searches all feasible paired thresholds, not just the grid, using labels. It is a development upper bound for this checkpoint/input/hypothesis formulation, not a recommended deployment threshold.

| View | Required recall | Largest threshold | Dev recall (FN) | Corrected dev recall (FN) | Reviewed recall (FN) | Cache rejection |
|---|---:|---:|---:|---:|---:|---:|''']
for f in frontiers:
    ds=f['datasets']
    cells=[f"{pct(ds[k]['keepRecall'])} ({ds[k]['falseRejects']})" for k in ['development','developmentCorrected','reviewedUnique']]
    lines.append(f"| {names[f['view']]} | {pct(f['targetRecall'])} | {f['threshold']:.9f} | {' | '.join(cells)} | {pct(f['cacheRejectionRate'])} ({f['cacheRejected']}) |")
lines += ['''
Even the optimistic best body-head frontier removes only 8.85% at 98% reviewed recall and 7.05% at 99%. At 98%, false rejects include General Finance, Field Technician 4 and Senior Transmission Line Designer. At 99%, the transmission designer is still lost. These do not match the rule baseline. The no-miss discrete setting would save 61 downstream inferences per 1,503 postings, versus 208 under the current shadow rule; the optimistic 98% frontier saves 133 with known losses. These are counterfactual workload counts, not enabled behavior.

## Error analysis

Title-only at 0.9 loses six reviewed KEEP, including Mechanical Engineer, Principal Mechanical Producibility Engineer, General Finance and a structural-processing engineering manager whose duties involve automation/robotics. Body context rescues some adjacent roles, but it also protects unrelated occupations.

At body-head 0.95, which reaches 14.70% cache rejection, the six reviewed false rejects are:''']
c = next(c for c in m['configurations'] if c['view']=='title_body_head' and c['mode']=='paired' and c['threshold']==.95)
for r in c['falseRejects']:
    lines.append(f"* **{r['title']}** — KEEP score {r['keepScore']:.4f}. {r['labelReason']}")
lines += ['''
Head/tail at 0.9 loses 24 reviewed KEEP, including technical integration, AI solutions architecture, SAP configuration, help desk and field engineering. Recovering end-of-body text is not a safe general solution.

Examples of correctly rejected reviewed occupations at body-head 0.9 (not high absolute NLI confidence):''']
c = next(c for c in m['configurations'] if c['view']=='title_body_head' and c['mode']=='paired' and c['threshold']==.9)
for r in c['highScoreRejectExamples'][:6]:
    lines.append(f"* {r['title']}: KEEP score {r['keepScore']:.4f}; negative entailment {r['rejectEntailment']:.6f}.")
lines += ['''
These demonstrate some occupation-general discrimination without regex families, but insufficient coverage. The following fixed examples were inherited from the preceding duty audit. Relative scores shifting toward KEEP with body text are consistent with employer/product and generic business-language protection; this comparison does not prove which phrase caused the result.

| Audited title | Label | Title score | Body head score | Body head/tail score |
|---|---|---:|---:|---:|''']
for r in m['diagnostics']:
    cells=[f"{r['scores'][v]['keepScore']:.4f}" for v in names]
    lines.append(f"| {r['title'].replace('|','/')} | {r['label']} | {' | '.join(cells)} |")
lines += ['''
Principal Accountant changes from 0.4307 title-only to 0.9692 with body head; Recruiter reaches 0.9956. Finance, procurement, sales training, marketing and archaeology roles remain protected despite unrelated duties. Technical employer/product language and generic software/systems/process-development/network mentions are not reliably separated from hands-on technical work. Adjacent technical sales, platform/driver work, data-center engineering and sales-operations automation often survive appropriately, but the false rejects above prevent a safe replacement claim. The JSON provides counts by audited leakage family at every threshold; those purposive family shares must not be interpreted as population prevalence.

## Runtime and memory

Batch of eight postings (16 NLI pairs), 1,563 total including fixtures. Timings include tokenization, padding, transfers and both hypotheses; exclude weight download, startup and warm-up. Single-posting latency uses a deterministic 40-row sample. The initial startup/download took about 12.43 seconds. No Qwen inference was run, so there is no fabricated direct speed ratio.

| View | Amortized ms/posting | Batch postings/s | Single median / p95 ms | Peak CUDA allocated / reserved MiB | Peak process RSS MiB |
|---|---:|---:|---:|---:|---:|''']
for view in names:
    c=next(c for c in m['configurations'] if c['view']==view)
    t,mem=c['timing'],c['memory']
    lines.append(f"| {names[view]} | {t['amortizedMsPerPosting']:.2f} | {t['postingsPerSecond']:.2f} | {t['singleMedianMs']:.2f} / {t['singleP95Ms']:.2f} | {mem['peakCudaAllocatedMiB']:.1f} / {mem['peakCudaReservedMiB']:.1f} | {mem['peakProcessRssMiB']:.1f} |")
b=m['baseline']['latencyOnScoringHost']
lines += [f"\nElectrical-safe on the same host's CPU and 166-row development benchmark: median {b['medianUs']:.2f} microseconds, p95 {b['p95Us']:.2f} microseconds, batch {b['batchPerSecond']:.1f} postings/s. Different benchmark lengths/input mixtures limit direct ratios. CUDA allocated/reserved figures exclude some driver/context memory; process GPU usage was approximately 2.3 GB. The encoder is bounded and small compared with a generative model, but the body configuration is materially more expensive than these rules and achieves less rejection.", '''
## Complete sweep

Latency and memory are per input view as above and identical for all thresholds (postprocessing only). Every row below has corresponding audit, delta, corrected-development, synthetic metrics, all reviewed false rejects, top correct-reject examples and leakage-family counts in `metrics-extended.json`. Reject precision here uses the deduplicated reviewed reference, not unlabeled cache accuracy.

| View | Decision | Threshold | Dev recall | Dev FN | Dev rejection | Reviewed recall | Reviewed FN | Reviewed reject precision | Cache rejection |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|''']
for c in m['configurations']:
    d,u=c['datasets']['development'],c['datasets']['reviewedUnique']
    lines.append(f"| {names[c['view']]} | {c['mode']} | {c['threshold']} | {pct(d['keepRecall'])} | {d['falseRejects']} | {pct(d['rejectionRate'])} | {pct(u['keepRecall'])} | {u['falseRejects']} | {pct(u['rejectPrecision'])} | {pct(c['cache']['rejectionRate'])} |")
lines += ['''
## Validation and scope

Validation includes exact checkpoint SHA, label-free input and frozen artifact fingerprints, canonical eligible-cache hash, prediction coverage/finite probabilities, all 4,689 posting/view inputs preserving full titles within 512 tokens, overlong-title fail-open, tokenizer API and logit parity, NLI entailment/contradiction/neutral sanity examples, threshold direction/equality, neutral-gate behavior, metric arithmetic, frozen-score replay and frontier replay. Test and source-validation results are recorded in the accompanying validation-results file.

All added files are evaluation artifacts under `Tests/cheap_reject_evaluation/deberta_v1/` and this report. The existing rule source, labels, production application and dependencies are unchanged. There is no basis from this experiment to recommend adding this encoder alongside the rules: that adds GPU cost and retains rule maintenance without demonstrated safety/coverage benefit. A different classifier or training experiment would be separate authorized work; this result only assesses this pinned off-the-shelf checkpoint and formulation. Stop here; do not enable discard behavior or begin the second stage.
''']
(ROOT/'deberta-cheap-reject-experiment.md').write_text('\n'.join(lines), encoding='utf-8')
