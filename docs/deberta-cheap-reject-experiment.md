# DeBERTa first-layer cheap-reject experiment

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
| Electrical-safe | 100% | 0/85 | 33.73% | 100% (0) | 13.84% (208/1503) |
| Title < 0.5 | 98.82% | 1/85 | 3.61% | 98.67% (2) | 2.20% (33/1503) |
| Title < 0.9 | 96.47% | 3/85 | 22.29% | 96.00% (6) | 15.24% (229/1503) |
| Title + body head < 0.9 | 100.00% | 0/85 | 4.82% | 100.00% (0) | 4.06% (61/1503) |
| Title + body head < 0.95 | 97.65% | 2/85 | 17.47% | 96.00% (6) | 14.70% (221/1503) |
| Title + body head/tail < 0.9 | 89.41% | 9/85 | 28.92% | 84.00% (24) | 22.42% (337/1503) |

The best discrete setting meeting both recall objectives is title + body head at 0.9: 61 cache rejects (4.06%), no false rejects among 150 reviewed KEEP, only 18 of 197 reviewed REJECT removed. It also falsely rejects the synthetic **Systems Integration Engineer** (score 0.8414): 29/30 synthetic KEEP survive. Electrical-safe retains all 30. Thus even the best discrete setting is not ready for production discard.

Every absolute NLI gate rejects **zero cache jobs**. There are no genuinely high-confidence cache rejects by the tested negative-entailment criterion. Calling thresholded paired-score decisions “high-confidence” would be misleading.

## Optimistic threshold frontier

For each recall target, sort KEEP scores in each real reference and take the most restrictive maximal permissible threshold (equality stays KEEP). This searches all feasible paired thresholds, not just the grid, using labels. It is a development upper bound for this checkpoint/input/hypothesis formulation, not a recommended deployment threshold.

| View | Required recall | Largest threshold | Dev recall (FN) | Corrected dev recall (FN) | Reviewed recall (FN) | Cache rejection |
|---|---:|---:|---:|---:|---:|---:|
| Title | 98.00% | 0.501015484 | 98.82% (1) | 98.84% (1) | 98.67% (2) | 2.20% (33) |
| Title | 99.00% | 0.177104414 | 100.00% (0) | 100.00% (0) | 100.00% (0) | 0.40% (6) |
| Title + body head | 98.00% | 0.929839849 | 100.00% (0) | 98.84% (1) | 98.00% (3) | 8.85% (133) |
| Title + body head | 99.00% | 0.918623090 | 100.00% (0) | 100.00% (0) | 99.33% (1) | 7.05% (106) |
| Title + body head/tail | 98.00% | 0.806363463 | 98.82% (1) | 98.84% (1) | 98.00% (3) | 4.06% (61) |
| Title + body head/tail | 99.00% | 0.724117279 | 100.00% (0) | 100.00% (0) | 100.00% (0) | 1.26% (19) |

Even the optimistic best body-head frontier removes only 8.85% at 98% reviewed recall and 7.05% at 99%. At 98%, false rejects include General Finance, Field Technician 4 and Senior Transmission Line Designer. At 99%, the transmission designer is still lost. These do not match the rule baseline. The no-miss discrete setting would save 61 downstream inferences per 1,503 postings, versus 208 under the current shadow rule; the optimistic 98% frontier saves 133 with known losses. These are counterfactual workload counts, not enabled behavior.

## Error analysis

Title-only at 0.9 loses six reviewed KEEP, including Mechanical Engineer, Principal Mechanical Producibility Engineer, General Finance and a structural-processing engineering manager whose duties involve automation/robotics. Body context rescues some adjacent roles, but it also protects unrelated occupations.

At body-head 0.95, which reaches 14.70% cache rejection, the six reviewed false rejects are:
* **Principal Specialist, General Finance** — KEEP score 0.9186. Explicit SQL reporting, financial-system module/configuration maintenance, advanced systems support and troubleshooting: KEEP. Earlier dev REJECT label is too coarse.
* **Northrop Grumman (SkillBridge) Java Software Engineer Level 1 or 2** — KEEP score 0.9387. Software, systems, data/AI, automation, integration, technical security or infrastructure engineering duties.
* **Ground Penetrating Radar Specialist (Antarctica)** — KEEP score 0.9298. Conservative KEEP: radar data processing plus computer/electronic equipment troubleshooting; remote location alone is not occupational rejection.
* **Business Readiness Director - Process and Systems Transformation (PST) (Program Management)** — KEEP score 0.9356. SAP system transformation/deployment leadership; technical program ambiguity stays KEEP.
* **Senior Transmission Line Designer** — KEEP score 0.9010. Adjacent electrical transmission design engineering; conservative KEEP.
* **Field Technician 4** — KEEP score 0.9241. Field Technician title hides mechanical field engineering, root-cause analysis, custom tooling design and control-system analysis; ambiguous specialist engineering stays KEEP.

Head/tail at 0.9 loses 24 reviewed KEEP, including technical integration, AI solutions architecture, SAP configuration, help desk and field engineering. Recovering end-of-body text is not a safe general solution.

Examples of correctly rejected reviewed occupations at body-head 0.9 (not high absolute NLI confidence):
* Senior Procurement Agent: KEEP score 0.6871; negative entailment 0.019599.
* Senior Civil Engineer: KEEP score 0.8075; negative entailment 0.011187.
* Senior Structural Engineer: KEEP score 0.6557; negative entailment 0.006872.
* Tax Incentive Analyst: KEEP score 0.8266; negative entailment 0.002547.
* Roadway Engineer: KEEP score 0.8479; negative entailment 0.001111.
* Drainage Engineer II: KEEP score 0.8908; negative entailment 0.000685.

These demonstrate some occupation-general discrimination without regex families, but insufficient coverage. The following fixed examples were inherited from the preceding duty audit. Relative scores shifting toward KEEP with body text are consistent with employer/product and generic business-language protection; this comparison does not prove which phrase caused the result.

| Audited title | Label | Title score | Body head score | Body head/tail score |
|---|---|---:|---:|---:|
| Senior Talent Acquisition Specialist – National Security & Operations | REJECT | 0.9382 | 0.9956 | 0.9819 |
| Field Service Technician I | KEEP | 0.9564 | 0.9972 | 0.9122 |
| Principal Accountant (remote) | REJECT | 0.4307 | 0.9692 | 0.9537 |
| Service Center Procurement & Business Process Analyst | REJECT | 0.9460 | 0.9915 | 0.8916 |
| Boeing Summer 2027 Internship Program (PAID) – Finance (Evergreen) | REJECT | 0.7382 | 0.9507 | 0.8054 |
| Manager, Program Finance & Control Cost Analyst (Remote) | REJECT | 0.7400 | 0.9689 | 0.8624 |
| Special Operations Psychological/Mental Health Technician (JSOC, Fort Bragg, NC) | REJECT | 0.7850 | 0.8694 | 0.8340 |
| Sales Operations Analyst | KEEP | 0.9285 | 0.9992 | 0.9592 |
| Mid-level Archaeology Technician (on-call) | REJECT | 0.1609 | 0.8597 | 0.8815 |
| Sr Dir, Sales Training (Armis/Veza) | REJECT | 0.9035 | 0.9635 | 0.9774 |
| Drainage Engineer I | REJECT | 0.8792 | 0.9346 | 0.9102 |
| Senior GTS Configuration Lead (Implementation & Maintenance) | KEEP | 0.9951 | 0.9619 | 0.8405 |
| Design Project Manager, Data Centers - Remote (U.S.) | KEEP | 0.9785 | 0.9934 | 0.9570 |
| Senior Engineering Manager, Structural Processing (Remote) | KEEP | 0.7423 | 0.9864 | 0.8829 |
| Senior Account Manager, Global AI Factory Technical Sales and Strategy | KEEP | 0.9803 | 0.9997 | 0.9757 |
| Federal Physical AI Business Development Lead | KEEP | 0.9826 | 0.9996 | 0.9945 |
| Senior Systems Software Engineer, CUDA Driver - Multi-Node and Memory Model | KEEP | 0.9989 | 0.9997 | 0.9962 |
| Product Marketing Manager, Quantum Computing Platform | REJECT | 0.9703 | 0.9384 | 0.9865 |
| Procurement Agent - Millennium Space Systems | REJECT | 0.9160 | 0.9765 | 0.6851 |
| Field Technician 4 | KEEP | 0.9478 | 0.9241 | 0.8465 |
| Principal Specialist, General Finance | KEEP | 0.6802 | 0.9186 | 0.8474 |

Principal Accountant changes from 0.4307 title-only to 0.9692 with body head; Recruiter reaches 0.9956. Finance, procurement, sales training, marketing and archaeology roles remain protected despite unrelated duties. Technical employer/product language and generic software/systems/process-development/network mentions are not reliably separated from hands-on technical work. Adjacent technical sales, platform/driver work, data-center engineering and sales-operations automation often survive appropriately, but the false rejects above prevent a safe replacement claim. The JSON provides counts by audited leakage family at every threshold; those purposive family shares must not be interpreted as population prevalence.

## Runtime and memory

Batch of eight postings (16 NLI pairs), 1,563 total including fixtures. Timings include tokenization, padding, transfers and both hypotheses; exclude weight download, startup and warm-up. Single-posting latency uses a deterministic 40-row sample. The initial startup/download took about 12.43 seconds. No Qwen inference was run, so there is no fabricated direct speed ratio.

| View | Amortized ms/posting | Batch postings/s | Single median / p95 ms | Peak CUDA allocated / reserved MiB | Peak process RSS MiB |
|---|---:|---:|---:|---:|---:|
| Title | 3.92 | 254.98 | 14.89 / 16.09 | 681.7 / 724.0 | 1321.6 |
| Title + body head | 62.00 | 16.13 | 61.51 / 62.86 | 1838.7 / 2198.0 | 1321.6 |
| Title + body head/tail | 62.28 | 16.06 | 61.60 / 62.85 | 1838.7 / 2198.0 | 1321.6 |

Electrical-safe on the same host's CPU and 166-row development benchmark: median 33.34 microseconds, p95 1297.17 microseconds, batch 2320.0 postings/s. Different benchmark lengths/input mixtures limit direct ratios. CUDA allocated/reserved figures exclude some driver/context memory; process GPU usage was approximately 2.3 GB. The encoder is bounded and small compared with a generative model, but the body configuration is materially more expensive than these rules and achieves less rejection.

## Complete sweep

Latency and memory are per input view as above and identical for all thresholds (postprocessing only). Every row below has corresponding audit, delta, corrected-development, synthetic metrics, all reviewed false rejects, top correct-reject examples and leakage-family counts in `metrics-extended.json`. Reject precision here uses the deduplicated reviewed reference, not unlabeled cache accuracy.

| View | Decision | Threshold | Dev recall | Dev FN | Dev rejection | Reviewed recall | Reviewed FN | Reviewed reject precision | Cache rejection |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Title | paired | 0.01 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title | paired | 0.025 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title | paired | 0.05 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title | paired | 0.1 | 100.00% | 0 | 0.60% | 100.00% | 0 | 100.00% | 0.07% |
| Title | paired | 0.2 | 98.82% | 1 | 1.20% | 99.33% | 1 | 66.67% | 0.53% |
| Title | paired | 0.3 | 98.82% | 1 | 1.20% | 99.33% | 1 | 80.00% | 0.93% |
| Title | paired | 0.4 | 98.82% | 1 | 2.41% | 99.33% | 1 | 87.50% | 1.40% |
| Title | paired | 0.5 | 98.82% | 1 | 3.61% | 98.67% | 2 | 83.33% | 2.20% |
| Title | paired | 0.6 | 97.65% | 2 | 4.82% | 98.00% | 3 | 82.35% | 3.39% |
| Title | paired | 0.7 | 96.47% | 3 | 8.43% | 96.67% | 5 | 83.33% | 5.12% |
| Title | paired | 0.8 | 96.47% | 3 | 16.87% | 96.00% | 6 | 88.24% | 8.18% |
| Title | paired | 0.9 | 96.47% | 3 | 22.29% | 96.00% | 6 | 92.50% | 15.24% |
| Title | paired | 0.95 | 92.94% | 6 | 38.55% | 87.33% | 19 | 88.34% | 35.80% |
| Title | paired | 0.975 | 78.82% | 18 | 57.23% | 72.00% | 42 | 81.33% | 56.22% |
| Title | paired | 0.99 | 58.82% | 35 | 69.28% | 49.33% | 76 | 71.96% | 71.79% |
| Title | paired | 0.995 | 38.82% | 52 | 79.52% | 33.33% | 100 | 66.10% | 81.04% |
| Title | paired | 0.999 | 4.71% | 81 | 97.59% | 4.00% | 144 | 57.77% | 97.67% |
| Title | paired | 0.9995 | 0.00% | 85 | 100.00% | 0.00% | 150 | 56.77% | 100.00% |
| Title | paired | 0.9999 | 0.00% | 85 | 100.00% | 0.00% | 150 | 56.77% | 100.00% |
| Title | entailment-gate | 0.7 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title | entailment-gate | 0.8 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title | entailment-gate | 0.9 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title | entailment-gate | 0.95 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title | entailment-gate | 0.99 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.01 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.025 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.05 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.1 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.2 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.3 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.4 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.5 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | paired | 0.6 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.13% |
| Title + body head | paired | 0.7 | 100.00% | 0 | 0.60% | 100.00% | 0 | 100.00% | 0.40% |
| Title + body head | paired | 0.8 | 100.00% | 0 | 2.41% | 100.00% | 0 | 100.00% | 0.93% |
| Title + body head | paired | 0.9 | 100.00% | 0 | 4.82% | 100.00% | 0 | 100.00% | 4.06% |
| Title + body head | paired | 0.95 | 97.65% | 2 | 17.47% | 96.00% | 6 | 91.04% | 14.70% |
| Title + body head | paired | 0.975 | 91.76% | 7 | 34.34% | 87.33% | 19 | 86.13% | 29.87% |
| Title + body head | paired | 0.99 | 82.35% | 15 | 49.40% | 78.00% | 33 | 82.81% | 43.05% |
| Title + body head | paired | 0.995 | 74.12% | 22 | 56.02% | 69.33% | 46 | 79.28% | 49.97% |
| Title + body head | paired | 0.999 | 52.94% | 40 | 72.29% | 46.00% | 81 | 70.55% | 68.13% |
| Title + body head | paired | 0.9995 | 24.71% | 64 | 87.35% | 22.00% | 117 | 62.50% | 85.96% |
| Title + body head | paired | 0.9999 | 0.00% | 85 | 100.00% | 0.00% | 150 | 56.77% | 100.00% |
| Title + body head | entailment-gate | 0.7 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | entailment-gate | 0.8 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | entailment-gate | 0.9 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | entailment-gate | 0.95 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head | entailment-gate | 0.99 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | paired | 0.01 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | paired | 0.025 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | paired | 0.05 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | paired | 0.1 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | paired | 0.2 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | paired | 0.3 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | paired | 0.4 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | paired | 0.5 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.07% |
| Title + body head/tail | paired | 0.6 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.13% |
| Title + body head/tail | paired | 0.7 | 100.00% | 0 | 0.60% | 100.00% | 0 | 100.00% | 0.80% |
| Title + body head/tail | paired | 0.8 | 98.82% | 1 | 6.63% | 98.00% | 3 | 82.35% | 3.59% |
| Title + body head/tail | paired | 0.9 | 89.41% | 9 | 28.92% | 84.00% | 24 | 75.00% | 22.42% |
| Title + body head/tail | paired | 0.95 | 70.59% | 25 | 53.01% | 64.67% | 53 | 74.27% | 48.90% |
| Title + body head/tail | paired | 0.975 | 54.12% | 39 | 69.88% | 50.67% | 74 | 72.08% | 66.60% |
| Title + body head/tail | paired | 0.99 | 30.59% | 59 | 84.34% | 27.33% | 109 | 64.38% | 80.90% |
| Title + body head/tail | paired | 0.995 | 18.82% | 69 | 90.36% | 17.33% | 124 | 61.37% | 89.22% |
| Title + body head/tail | paired | 0.999 | 1.18% | 84 | 99.40% | 1.33% | 148 | 57.10% | 99.80% |
| Title + body head/tail | paired | 0.9995 | 0.00% | 85 | 100.00% | 0.00% | 150 | 56.77% | 100.00% |
| Title + body head/tail | paired | 0.9999 | 0.00% | 85 | 100.00% | 0.00% | 150 | 56.77% | 100.00% |
| Title + body head/tail | entailment-gate | 0.7 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | entailment-gate | 0.8 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | entailment-gate | 0.9 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | entailment-gate | 0.95 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |
| Title + body head/tail | entailment-gate | 0.99 | 100.00% | 0 | 0.00% | 100.00% | 0 | — | 0.00% |

## Validation and scope

Validation includes exact checkpoint SHA, label-free input and frozen artifact fingerprints, canonical eligible-cache hash, prediction coverage/finite probabilities, all 4,689 posting/view inputs preserving full titles within 512 tokens, overlong-title fail-open, tokenizer API and logit parity, NLI entailment/contradiction/neutral sanity examples, threshold direction/equality, neutral-gate behavior, metric arithmetic, frozen-score replay and frontier replay. Test and source-validation results are recorded in the accompanying validation-results file.

All added files are evaluation artifacts under `Tests/cheap_reject_evaluation/deberta_v1/` and this report. The existing rule source, labels, production application and dependencies are unchanged. There is no basis from this experiment to recommend adding this encoder alongside the rules: that adds GPU cost and retains rule maintenance without demonstrated safety/coverage benefit. A different classifier or training experiment would be separate authorized work; this result only assesses this pinned off-the-shelf checkpoint and formulation. Stop here; do not enable discard behavior or begin the second stage.
