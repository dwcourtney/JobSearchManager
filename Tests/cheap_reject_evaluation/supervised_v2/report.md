# Supervised cheap triage: 3,198-posting corpus

Offline experiment only. No production changes, new triage rules, generative inference, training on AMBIGUOUS, production holdout access, deployment, or commits. See interpretation.md for the recommendation and error analysis.

## Data and protocol

Source: machine_corpus_v1/machine-labeled.jsonl. All 3,198 frozen Codex decisions retained: 2,121 KEEP, 548 REJECT, 529 AMBIGUOUS. Only 2,669 binary rows are eligible for fitting/calibration/evaluation: 1,159 described (914 KEEP / 245 REJECT), 1,510 title-only (1,207 KEEP / 303 REJECT). The 1,014 high and 1,655 medium labels exactly comprise this binary subset; all 529 low labels are AMBIGUOUS. Thus design B (high/medium) and C (all binary) are exactly identical. No artificial extra B run is claimed.

A/description: train only described rows. B=C/all: train all binary rows. D/weighted: train all binary rows with quality multiplier described/high=1, described/medium=0.75, title-only/medium=0.25. Every regime also uses train-only inverse-frequency class weights; combined weights normalize to mean one. All regimes share unweighted binary validation and identical evaluation sets to isolate training treatment. A therefore means description-only training, not description-only calibration; described evaluation is reported separately.

Three outer employer-held-out folds, with different employers for inner validation. Purge connected corpus lexical title-family, duplicate-cluster, exact-title and identical nonempty-body components across partitions. These lexical groups are not guaranteed semantic occupation families. Missing-body deduplication remains limited. Every binary corpus row is evaluated once, never on a training model. All corpus rows including AMBIGUOUS belonging to the outer employers supply evaluation-side purge groups.

| Fold | Evaluation employers | Train total / described | Validation KEEP | Evaluation KEEP / REJECT |
|---|---|---:|---:|---:|
| 0 | leidos | 700 / 693 | 392 | 1152 / 289 |
| 1 | boeing, servicenow, amentum, nxp-semiconductors | 1595 / 555 | 327 | 459 / 133 |
| 2 | nvidia, parsons, rtx, aecom, northrop-grumman, kbr | 1830 / 429 | 42 | 510 / 126 |

Models and revisions:
- deberta-small: `cross-encoder/nli-deberta-v3-small@fa2804872c3b4bd748f38c0185cc85775361e735`, 4 epochs, learning rate 2e-05. NLI encoder and pooler reused; three-way NLI classifier replaced with a seeded random binary KEEP/REJECT head. All parameters fine-tuned.
- bert-tiny: `google/bert_uncased_L-2_H-128_A-2@30b0a37ccaaa32f332884b96992754e246e48c5f`, 8 epochs, learning rate 0.0001. Pretrained encoder with seeded random binary sequence classifier; all parameters fine-tuned.

FP32 on curiosity GTX 1070; seed 20260905 + fold; AdamW weight decay 0.01, microbatch 4, accumulation 4, clipping 1.0, linear schedule with 10% warmup. Fresh binary head and all encoder parameters fine-tuned. Lowest validation cross entropy selects checkpoint. Exactly 36 fits; epochs/hyperparameters fixed before outer results.

Title preserved first, then beginning of body within 256 tokens including prefixes/special tokens. Missing body is empty; no imputation. Overlong titles fail open during inference and abort fitting. No occupational extraction. Body truncation can omit the same duties that Codex used during adjudication.

Labels are provisional machine judgments, not independent human truth. Title-only labels are weak; the 281-row human review queue has not been adjudicated by humans. Confidence buckets are not calibrated probabilities. Employer concentration and label ambiguity limit inference; these data cannot certify population 98–99% recall.

## Baseline on the same subsets

| Subset | n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---:|---:|---:|---:|---:|
| combined | 2669 | 99.58% | 9 | 1.57% | 78.57% |
| description | 1159 | 99.02% | 9 | 3.62% | 78.57% |
| title_only | 1510 | 100.00% | 0 | 0.00% | — |
| confidence_high | 1014 | 99.36% | 5 | 3.55% | 86.11% |
| confidence_medium | 1655 | 99.70% | 4 | 0.36% | 33.33% |

Original 1,503-posting cache: 13.84% rejection. New 3,198 corpus including AMBIGUOUS: 1.63%. Rule timing on Windows: 0.0979 ms/posting. Hardware differs from encoder GPU timing.

## Validation-selected operating points

KEEP probability is the score. Reject strictly below threshold; ties survive. Per fold, choose the largest validation KEEP-score boundary allowing floor((1-target)*KEEP_n) validation misses. Freeze it before outer evaluation. Actual outer recall may miss its target. Fold 2 has only 42 validation KEEP examples: both 99% and 98% allow zero validation misses. Validation also selects checkpoints, so it is not untouched evaluation.

| Model / view / training | Validation target | Outer KEEP recall | False rejects | Outer rejection | Reject precision | Old cache rejection | New corpus rejection |
|---|---:|---:|---:|---:|---:|---:|---:|
| bert-tiny / title / all | 100.00% | 98.63% | 29 | 6.14% | 82.32% | 9.31% | 8.19% |
| bert-tiny / title / all | 99.00% | 96.84% | 67 | 11.32% | 77.81% | 13.24% | 15.73% |
| bert-tiny / title / all | 98.00% | 93.59% | 136 | 16.90% | 69.84% | 15.64% | 22.64% |
| bert-tiny / title / description | 100.00% | 99.06% | 20 | 4.53% | 83.47% | 7.19% | 5.75% |
| bert-tiny / title / description | 99.00% | 97.78% | 47 | 8.32% | 78.83% | 8.72% | 11.48% |
| bert-tiny / title / description | 98.00% | 95.33% | 99 | 13.41% | 72.35% | 11.84% | 18.23% |
| bert-tiny / title / weighted | 100.00% | 98.77% | 26 | 5.70% | 82.89% | 10.45% | 7.00% |
| bert-tiny / title / weighted | 99.00% | 97.36% | 56 | 9.52% | 77.95% | 13.11% | 13.57% |
| bert-tiny / title / weighted | 98.00% | 94.11% | 125 | 15.92% | 70.59% | 16.50% | 21.45% |
| bert-tiny / title_body / all | 100.00% | 98.35% | 35 | 7.42% | 82.32% | 14.50% | 8.04% |
| bert-tiny / title_body / all | 99.00% | 97.50% | 53 | 9.48% | 79.05% | 17.96% | 10.48% |
| bert-tiny / title_body / all | 98.00% | 95.80% | 89 | 12.96% | 74.28% | 21.62% | 14.38% |
| bert-tiny / title_body / description | 100.00% | 98.02% | 42 | 8.47% | 81.42% | 15.50% | 9.32% |
| bert-tiny / title_body / description | 99.00% | 95.80% | 89 | 14.24% | 76.58% | 21.62% | 16.32% |
| bert-tiny / title_body / description | 98.00% | 93.12% | 146 | 18.66% | 70.68% | 23.09% | 23.01% |
| bert-tiny / title_body / weighted | 100.00% | 98.16% | 39 | 7.53% | 80.60% | 13.91% | 8.47% |
| bert-tiny / title_body / weighted | 99.00% | 96.51% | 74 | 10.83% | 74.39% | 18.63% | 12.41% |
| bert-tiny / title_body / weighted | 98.00% | 95.71% | 91 | 13.83% | 75.34% | 20.29% | 15.57% |
| deberta-small / title / all | 100.00% | 98.44% | 33 | 5.99% | 79.38% | 5.66% | 8.04% |
| deberta-small / title / all | 99.00% | 96.18% | 81 | 11.46% | 73.53% | 9.78% | 15.35% |
| deberta-small / title / all | 98.00% | 94.53% | 116 | 14.72% | 70.48% | 11.44% | 19.42% |
| deberta-small / title / description | 100.00% | 97.55% | 52 | 4.83% | 59.69% | 2.26% | 6.91% |
| deberta-small / title / description | 99.00% | 95.76% | 90 | 8.39% | 59.82% | 5.32% | 11.60% |
| deberta-small / title / description | 98.00% | 93.68% | 134 | 12.03% | 58.26% | 6.92% | 16.76% |
| deberta-small / title / weighted | 100.00% | 98.11% | 40 | 5.88% | 74.52% | 4.39% | 8.13% |
| deberta-small / title / weighted | 99.00% | 96.13% | 82 | 10.79% | 71.53% | 6.92% | 14.92% |
| deberta-small / title / weighted | 98.00% | 94.39% | 119 | 14.57% | 69.41% | 9.45% | 19.48% |
| deberta-small / title_body / all | 100.00% | 99.39% | 13 | 4.68% | 89.60% | 8.25% | 5.82% |
| deberta-small / title_body / all | 99.00% | 97.74% | 48 | 7.53% | 76.12% | 13.57% | 9.01% |
| deberta-small / title_body / all | 98.00% | 96.84% | 67 | 9.78% | 74.33% | 14.57% | 11.32% |
| deberta-small / title_body / description | 100.00% | 99.62% | 8 | 1.46% | 79.49% | 4.13% | 1.53% |
| deberta-small / title_body / description | 99.00% | 98.07% | 41 | 5.58% | 72.48% | 7.72% | 6.10% |
| deberta-small / title_body / description | 98.00% | 96.28% | 79 | 7.83% | 62.20% | 7.92% | 8.63% |
| deberta-small / title_body / weighted | 100.00% | 99.25% | 16 | 4.65% | 87.10% | 6.92% | 5.53% |
| deberta-small / title_body / weighted | 99.00% | 96.94% | 65 | 8.09% | 69.91% | 13.44% | 9.72% |
| deberta-small / title_body / weighted | 98.00% | 94.96% | 107 | 10.49% | 61.79% | 14.97% | 12.01% |

Old cache scores are cross-fitted by employer: each posting is scored only by the fold excluding its employer from training and validation. The prior 347 labeled references in that cache are evaluation-only stress cases, never fitting/calibration data. Their old occupational definitions differ from the new corpus, so comparisons are diagnostic. Cache rejection is not validated safe workload reduction. New corpus workload includes AMBIGUOUS, whose rejected fraction must be audited. No final full-data model was fitted.

## Per-candidate slices and errors

### bert-tiny / title / all

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 98.63% | 29 | 6.14% | 82.32% |
| 100.00% | description | 914 | 97.92% | 19 | 7.33% | 77.65% |
| 100.00% | title_only | 1207 | 99.17% | 10 | 5.23% | 87.34% |
| 100.00% | confidence_high | 781 | 98.34% | 13 | 7.40% | 82.67% |
| 100.00% | confidence_medium | 1340 | 98.81% | 16 | 5.38% | 82.02% |
| 99.00% | combined | 2121 | 96.84% | 67 | 11.32% | 77.81% |
| 99.00% | description | 914 | 96.28% | 34 | 11.22% | 73.85% |
| 99.00% | title_only | 1207 | 97.27% | 33 | 11.39% | 80.81% |
| 99.00% | confidence_high | 781 | 97.18% | 22 | 11.24% | 80.70% |
| 99.00% | confidence_medium | 1340 | 96.64% | 45 | 11.36% | 76.06% |
| 98.00% | combined | 2121 | 93.59% | 136 | 16.90% | 69.84% |
| 98.00% | description | 914 | 93.87% | 56 | 14.75% | 67.25% |
| 98.00% | title_only | 1207 | 93.37% | 80 | 18.54% | 71.43% |
| 98.00% | confidence_high | 781 | 95.26% | 37 | 14.60% | 75.00% |
| 98.00% | confidence_medium | 1340 | 92.61% | 99 | 18.31% | 67.33% |

99%-target fold thresholds: {'0': 0.07387106120586395, '1': 0.478274941444397, '2': 0.2621702551841736}. Outer Brier: 0.1115; cross entropy: 0.3750. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 38.00%. Prior 347-reference KEEP recall: 96.67%.

False rejects at the validation 99% target:

- Principal Customer Success Manager ( Armis/Veza) (servicenow; medium; described): score 0.01102 < 0.47827. Technical architecture, product/program leadership or technical solution work.
- Sr Customer Success Manager (Armis/Veza) (servicenow; medium; described): score 0.01140 < 0.47827. Technical architecture, product/program leadership or technical solution work.
- Boeing Summer 2027 Internship Program (Paid) – Data Analytics Intern (boeing; medium; title-only): score 0.01234 < 0.47827. Title indicates data ai work; duties cannot be verified because the description is unavailable.
- Advisory Solution Consultant - State and Local (servicenow; medium; described): score 0.01375 < 0.47827. Technical architecture, product/program leadership or technical solution work.
- Senior Project Management Specialist, Cost Account Management Integrated Product Team Lead (boeing; medium; described): score 0.01534 < 0.47827. Technical architecture, product/program leadership or technical solution work.
- Boeing Summer 2027 Internship Program (Paid) - Facilities Engineering (boeing; medium; title-only): score 0.01582 < 0.47827. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Boeing Summer 2027 Internship Program (Paid) – Information Digital Technology & Security (IDT&S) (boeing; medium; title-only): score 0.01729 < 0.47827. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Mid-Level or Experienced Electronics Programs Unit IPT Lead (Project Management Specialist) (boeing; medium; title-only): score 0.01739 < 0.47827. Title indicates technical leadership work; duties cannot be verified because the description is unavailable.
- Boeing Summer 2027 Internship Program (Paid) - Quality Engineering Intern (boeing; medium; described): score 0.02396 < 0.47827. Plausibly adjacent engineering, controls, electronics, integration or technical analysis.
- Global Director, Global Technology Partnerships (servicenow; high; described): score 0.02406 < 0.47827. Technical architecture, product/program leadership or technical solution work.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Global Account Manager – Industrial Strategic Accounts (nxp-semiconductors): score 0.01049; label confidence high.
- Supply Base Management Spec (Supply Base Mgmt) (boeing): score 0.01051; label confidence medium.
- Americas Mass Market US Central Region Leader (nxp-semiconductors): score 0.01052; label confidence high.
- Supply Chain Analyst Level 2-3 - B-52J CERP (boeing): score 0.01054; label confidence medium.
- Procurement Analyst (Process&Supply Base Mgmt) (boeing): score 0.01065; label confidence medium.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 2 false rejects, 99.54% recall.
- infrastructure-support: 379 KEEP, 21 false rejects, 94.46% recall.
- cybersecurity: 176 KEEP, 10 false rejects, 94.32% recall.
- engineering: 760 KEEP, 9 false rejects, 98.82% recall.
- data-ai: 105 KEEP, 2 false rejects, 98.10% recall.
- technical-leadership: 262 KEEP, 23 false rejects, 91.22% recall.
- company_masked: 2 decision flips; mean absolute score shift 0.0004; combined recall 96.75%.
- body_tail: 0 decision flips; mean absolute score shift 0.0000; combined recall 96.84%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 0.266 ms/posting, 3757.7 postings/second. Single-post median by fold: [3.03, 3.07, 3.24]. Maximum training allocated GPU MiB: 160.7.
- Clean-process benchmark, 40 outer rows: GPU 0.313 ms/posting / 3191.6 per second; batch-one median 2.69 ms; peak allocated/reserved 51.0/54.0 MiB. CPU four-thread median 1.22 ms / 815.7 per second; process high-water RSS after CPU 895.5 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.011021, 0 false rejects, 0.94% evaluation rejection, 1.00% cache rejection.
- 99.00% empirical recall constraint: threshold 0.065442, 21 false rejects, 4.91% evaluation rejection, 7.05% cache rejection.
- 98.00% empirical recall constraint: threshold 0.069429, 42 false rejects, 7.38% evaluation rejection, 7.85% cache rejection.

### bert-tiny / title / description

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 99.06% | 20 | 4.53% | 83.47% |
| 100.00% | description | 914 | 98.36% | 15 | 6.13% | 78.87% |
| 100.00% | title_only | 1207 | 99.59% | 5 | 3.31% | 90.00% |
| 100.00% | confidence_high | 781 | 98.46% | 12 | 6.31% | 81.25% |
| 100.00% | confidence_medium | 1340 | 99.40% | 8 | 3.44% | 85.96% |
| 99.00% | combined | 2121 | 97.78% | 47 | 8.32% | 78.83% |
| 99.00% | description | 914 | 97.37% | 24 | 8.46% | 75.51% |
| 99.00% | title_only | 1207 | 98.09% | 23 | 8.21% | 81.45% |
| 99.00% | confidence_high | 781 | 97.57% | 19 | 8.78% | 78.65% |
| 99.00% | confidence_medium | 1340 | 97.91% | 28 | 8.04% | 78.95% |
| 98.00% | combined | 2121 | 95.33% | 99 | 13.41% | 72.35% |
| 98.00% | description | 914 | 95.51% | 41 | 12.08% | 70.71% |
| 98.00% | title_only | 1207 | 95.19% | 58 | 14.44% | 73.39% |
| 98.00% | confidence_high | 781 | 96.16% | 30 | 12.33% | 76.00% |
| 98.00% | confidence_medium | 1340 | 94.85% | 69 | 14.08% | 70.39% |

99%-target fold thresholds: {'0': 0.0773819163441658, '1': 0.10956071317195892, '2': 0.23940928280353546}. Outer Brier: 0.1150; cross entropy: 0.3711. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 27.41%. Prior 347-reference KEEP recall: 98.67%.

False rejects at the validation 99% target:

- Supply Chain Risk Management Specialist (leidos; high; described): score 0.06383 < 0.07738. Cybersecurity or technical information-security work.
- Manager Endpoint Management (leidos; high; described): score 0.06434 < 0.07738. Technical architecture, product/program leadership or technical solution work.
- Business Analyst (leidos; high; described): score 0.06540 < 0.07738. Technical computing, infrastructure, networks, systems or application support.
- Knowledge Manager (SharePoint) - Senior (leidos; medium; title-only): score 0.06553 < 0.07738. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- NOC / SOC Shift Lead (2nd Shift) (leidos; high; described): score 0.06608 < 0.07738. Technical computing, infrastructure, networks, systems or application support.
- ICAM Program Manager (leidos; medium; title-only): score 0.06721 < 0.07738. Title indicates technical leadership work; duties cannot be verified because the description is unavailable.
- Cyber Operations Support Specialist (SharePoint) (leidos; medium; title-only): score 0.06725 < 0.07738. Title indicates cybersecurity work; duties cannot be verified because the description is unavailable.
- Enterprise Help Desk (EHD) Tier 1 (leidos; medium; title-only): score 0.06751 < 0.07738. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Senior Program Manager (leidos; high; described): score 0.06764 < 0.07738. Technical architecture, product/program leadership or technical solution work.
- Calibrations Manager (leidos; medium; title-only): score 0.06905 < 0.07738. Title indicates engineering work; duties cannot be verified because the description is unavailable.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Contracts Director (leidos): score 0.06319; label confidence medium.
- Master Production Scheduler (leidos): score 0.06320; label confidence medium.
- CNC Machinist 2nd shift (leidos): score 0.06321; label confidence medium.
- CNC Machinist 1st shift (leidos): score 0.06370; label confidence medium.
- Pashto & Dari (leidos): score 0.06398; label confidence medium.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 1 false rejects, 99.77% recall.
- infrastructure-support: 379 KEEP, 19 false rejects, 94.99% recall.
- cybersecurity: 176 KEEP, 8 false rejects, 95.45% recall.
- engineering: 760 KEEP, 7 false rejects, 99.08% recall.
- data-ai: 105 KEEP, 2 false rejects, 98.10% recall.
- technical-leadership: 262 KEEP, 10 false rejects, 96.18% recall.
- company_masked: 4 decision flips; mean absolute score shift 0.0003; combined recall 97.60%.
- body_tail: 0 decision flips; mean absolute score shift 0.0000; combined recall 97.78%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 0.263 ms/posting, 3807.7 postings/second. Single-post median by fold: [3.01, 3.01, 3.04]. Maximum training allocated GPU MiB: 624.9.
- Clean-process benchmark, 40 outer rows: GPU 0.325 ms/posting / 3078.1 per second; batch-one median 2.74 ms; peak allocated/reserved 51.0/54.0 MiB. CPU four-thread median 1.24 ms / 778.0 per second; process high-water RSS after CPU 895.9 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.063826, 0 false rejects, 0.22% evaluation rejection, 0.13% cache rejection.
- 99.00% empirical recall constraint: threshold 0.071951, 21 false rejects, 3.48% evaluation rejection, 1.20% cache rejection.
- 98.00% empirical recall constraint: threshold 0.082960, 42 false rejects, 6.29% evaluation rejection, 1.93% cache rejection.

### bert-tiny / title / weighted

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 98.77% | 26 | 5.70% | 82.89% |
| 100.00% | description | 914 | 97.81% | 20 | 7.85% | 78.02% |
| 100.00% | title_only | 1207 | 99.50% | 6 | 4.04% | 90.16% |
| 100.00% | confidence_high | 781 | 98.34% | 13 | 7.89% | 83.75% |
| 100.00% | confidence_medium | 1340 | 99.03% | 13 | 4.35% | 81.94% |
| 99.00% | combined | 2121 | 97.36% | 56 | 9.52% | 77.95% |
| 99.00% | description | 914 | 96.28% | 34 | 10.96% | 73.23% |
| 99.00% | title_only | 1207 | 98.18% | 22 | 8.41% | 82.68% |
| 99.00% | confidence_high | 781 | 97.06% | 23 | 11.05% | 79.46% |
| 99.00% | confidence_medium | 1340 | 97.54% | 33 | 8.58% | 76.76% |
| 98.00% | combined | 2121 | 94.11% | 125 | 15.92% | 70.59% |
| 98.00% | description | 914 | 93.87% | 56 | 14.75% | 67.25% |
| 98.00% | title_only | 1207 | 94.28% | 69 | 16.82% | 72.83% |
| 98.00% | confidence_high | 781 | 95.01% | 39 | 14.79% | 74.00% |
| 98.00% | confidence_medium | 1340 | 93.58% | 86 | 16.62% | 68.73% |

99%-target fold thresholds: {'0': 0.06651930510997772, '1': 0.04209744557738304, '2': 0.2928527891635895}. Outer Brier: 0.1152; cross entropy: 0.3855. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 34.03%. Prior 347-reference KEEP recall: 97.33%.

False rejects at the validation 99% target:

- Principal Customer Success Manager ( Armis/Veza) (servicenow; medium; described): score 0.01368 < 0.04210. Technical architecture, product/program leadership or technical solution work.
- Sr Customer Success Manager (Armis/Veza) (servicenow; medium; described): score 0.01425 < 0.04210. Technical architecture, product/program leadership or technical solution work.
- Boeing Summer 2027 Internship Program (Paid) – Data Analytics Intern (boeing; medium; title-only): score 0.01484 < 0.04210. Title indicates data ai work; duties cannot be verified because the description is unavailable.
- Senior Project Management Specialist, Cost Account Management Integrated Product Team Lead (boeing; medium; described): score 0.01554 < 0.04210. Technical architecture, product/program leadership or technical solution work.
- Advisory Solution Consultant - State and Local (servicenow; medium; described): score 0.01663 < 0.04210. Technical architecture, product/program leadership or technical solution work.
- Mid-Level or Experienced Electronics Programs Unit IPT Lead (Project Management Specialist) (boeing; medium; title-only): score 0.01677 < 0.04210. Title indicates technical leadership work; duties cannot be verified because the description is unavailable.
- Boeing Summer 2027 Internship Program (Paid) - Facilities Engineering (boeing; medium; title-only): score 0.02111 < 0.04210. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Global Director, Global Technology Partnerships (servicenow; high; described): score 0.02260 < 0.04210. Technical architecture, product/program leadership or technical solution work.
- Cloud Application Deployment and Migration Specialist (Mid-Level, Senior or Lead) **Sign on Bonus Potential** (boeing; high; described): score 0.02483 < 0.04210. Technical computing, infrastructure, networks, systems or application support.
- Project Management Specialist, Space Programs (boeing; medium; described): score 0.02801 < 0.04210. Technical architecture, product/program leadership or technical solution work.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Global Account Manager – Industrial Strategic Accounts (nxp-semiconductors): score 0.01280; label confidence high.
- Procurement Analyst (Supply Chain Planning) (boeing): score 0.01302; label confidence medium.
- Senior Manager, Americas Benefits (servicenow): score 0.01304; label confidence high.
- Americas Mass Market US Central Region Leader (nxp-semiconductors): score 0.01308; label confidence high.
- Supply Base Management Spec (Supply Base Mgmt) (boeing): score 0.01309; label confidence medium.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 2 false rejects, 99.54% recall.
- infrastructure-support: 379 KEEP, 16 false rejects, 95.78% recall.
- cybersecurity: 176 KEEP, 7 false rejects, 96.02% recall.
- engineering: 760 KEEP, 9 false rejects, 98.82% recall.
- data-ai: 105 KEEP, 2 false rejects, 98.10% recall.
- technical-leadership: 262 KEEP, 20 false rejects, 92.37% recall.
- company_masked: 3 decision flips; mean absolute score shift 0.0004; combined recall 97.22%.
- body_tail: 0 decision flips; mean absolute score shift 0.0000; combined recall 97.36%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 0.264 ms/posting, 3782.6 postings/second. Single-post median by fold: [3.05, 3.01, 3.04]. Maximum training allocated GPU MiB: 160.7.
- Clean-process benchmark, 40 outer rows: GPU 0.312 ms/posting / 3201.4 per second; batch-one median 2.72 ms; peak allocated/reserved 51.0/54.0 MiB. CPU four-thread median 1.58 ms / 622.7 per second; process high-water RSS after CPU 895.9 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.013684, 0 false rejects, 0.75% evaluation rejection, 0.93% cache rejection.
- 99.00% empirical recall constraint: threshold 0.061198, 21 false rejects, 4.50% evaluation rejection, 6.85% cache rejection.
- 98.00% empirical recall constraint: threshold 0.065523, 42 false rejects, 7.46% evaluation rejection, 7.72% cache rejection.

### bert-tiny / title_body / all

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 98.35% | 35 | 7.42% | 82.32% |
| 100.00% | description | 914 | 96.39% | 33 | 13.81% | 79.38% |
| 100.00% | title_only | 1207 | 99.83% | 2 | 2.52% | 94.74% |
| 100.00% | confidence_high | 781 | 97.57% | 19 | 13.61% | 86.23% |
| 100.00% | confidence_medium | 1340 | 98.81% | 16 | 3.63% | 73.33% |
| 99.00% | combined | 2121 | 97.50% | 53 | 9.48% | 79.05% |
| 99.00% | description | 914 | 95.19% | 44 | 16.31% | 76.72% |
| 99.00% | title_only | 1207 | 99.25% | 9 | 4.24% | 85.94% |
| 99.00% | confidence_high | 781 | 96.93% | 24 | 15.88% | 85.09% |
| 99.00% | confidence_medium | 1340 | 97.84% | 29 | 5.56% | 68.48% |
| 98.00% | combined | 2121 | 95.80% | 89 | 12.96% | 74.28% |
| 98.00% | description | 914 | 92.45% | 69 | 19.50% | 69.47% |
| 98.00% | title_only | 1207 | 98.34% | 20 | 7.95% | 83.33% |
| 98.00% | confidence_high | 781 | 94.75% | 41 | 18.64% | 78.31% |
| 98.00% | confidence_medium | 1340 | 96.42% | 48 | 9.49% | 69.43% |

99%-target fold thresholds: {'0': 0.2804417610168457, '1': 0.3240337669849396, '2': 0.19050337374210358}. Outer Brier: 0.0887; cross entropy: 0.3129. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 15.50%. Prior 347-reference KEEP recall: 96.00%.

False rejects at the validation 99% target:

- Advisory Solution Consultant - State and Local (servicenow; medium; described): score 0.00908 < 0.32403. Technical architecture, product/program leadership or technical solution work.
- Boeing Summer 2027 Internship Program (Paid) – Data Analytics Intern (boeing; medium; title-only): score 0.00963 < 0.32403. Title indicates data ai work; duties cannot be verified because the description is unavailable.
- Implementation Manager - Moveworks (servicenow; medium; described): score 0.00998 < 0.32403. Technical architecture, product/program leadership or technical solution work.
- Principal Customer Success Manager ( Armis/Veza) (servicenow; medium; described): score 0.01042 < 0.32403. Technical architecture, product/program leadership or technical solution work.
- Senior Product Manager - U.S. Public Sector Platforms (servicenow; high; described): score 0.01061 < 0.32403. Technical architecture, product/program leadership or technical solution work.
- Sr Customer Success Manager (Armis/Veza) (servicenow; medium; described): score 0.01092 < 0.32403. Technical architecture, product/program leadership or technical solution work.
- Nondestructive Test (NDT) Technician - UT & RT (boeing; medium; described): score 0.01114 < 0.32403. Plausibly adjacent engineering, controls, electronics, integration or technical analysis.
- Director of Expert Services, ITAM and ITOM (servicenow; medium; described): score 0.01115 < 0.32403. Technical architecture, product/program leadership or technical solution work.
- Boeing Summer 2027 Internship Program (Paid) – Information Digital Technology & Security (IDT&S) (boeing; medium; title-only): score 0.01167 < 0.32403. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Staff Security Incident Commander (servicenow; high; described): score 0.01228 < 0.32403. Cybersecurity or technical information-security work.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Senior Manager, Americas Benefits (servicenow): score 0.00880; label confidence high.
- Associate Procurement Agent (boeing): score 0.00885; label confidence high.
- Boeing Business Internship Program (Paid) Summer 2027 - Human Resources (boeing): score 0.00888; label confidence medium.
- Procurement Agent (Procurement Agent-General) (boeing): score 0.00894; label confidence high.
- Principal Customer Success Executive - Manufacturing Vertical (servicenow): score 0.00898; label confidence high.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 0 false rejects, 100.00% recall.
- infrastructure-support: 379 KEEP, 9 false rejects, 97.63% recall.
- cybersecurity: 176 KEEP, 5 false rejects, 97.16% recall.
- engineering: 760 KEEP, 16 false rejects, 97.89% recall.
- data-ai: 105 KEEP, 2 false rejects, 98.10% recall.
- technical-leadership: 262 KEEP, 21 false rejects, 91.98% recall.
- company_masked: 18 decision flips; mean absolute score shift 0.0065; combined recall 97.17%.
- body_tail: 218 decision flips; mean absolute score shift 0.0672; combined recall 91.37%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 2.258 ms/posting, 442.9 postings/second. Single-post median by fold: [3.14, 3.18, 6.06]. Maximum training allocated GPU MiB: 160.8.
- Clean-process benchmark, 40 outer rows: GPU 2.782 ms/posting / 359.4 per second; batch-one median 5.13 ms; peak allocated/reserved 70.8/96.0 MiB. CPU four-thread median 5.04 ms / 192.5 per second; process high-water RSS after CPU 892.5 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.009078, 0 false rejects, 0.45% evaluation rejection, 0.67% cache rejection.
- 99.00% empirical recall constraint: threshold 0.064395, 21 false rejects, 4.65% evaluation rejection, 8.85% cache rejection.
- 98.00% empirical recall constraint: threshold 0.173748, 42 false rejects, 7.91% evaluation rejection, 16.10% cache rejection.

### bert-tiny / title_body / description

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 98.02% | 42 | 8.47% | 81.42% |
| 100.00% | description | 914 | 95.40% | 42 | 13.98% | 74.07% |
| 100.00% | title_only | 1207 | 100.00% | 0 | 4.24% | 100.00% |
| 100.00% | confidence_high | 781 | 96.03% | 31 | 14.00% | 78.17% |
| 100.00% | confidence_medium | 1340 | 99.18% | 11 | 5.08% | 86.90% |
| 99.00% | combined | 2121 | 95.80% | 89 | 14.24% | 76.58% |
| 99.00% | description | 914 | 93.22% | 62 | 18.64% | 71.30% |
| 99.00% | title_only | 1207 | 97.76% | 27 | 10.86% | 83.54% |
| 99.00% | confidence_high | 781 | 95.01% | 39 | 18.15% | 78.80% |
| 99.00% | confidence_medium | 1340 | 96.27% | 50 | 11.84% | 74.49% |
| 98.00% | combined | 2121 | 93.12% | 146 | 18.66% | 70.68% |
| 98.00% | description | 914 | 92.78% | 66 | 19.76% | 71.18% |
| 98.00% | title_only | 1207 | 93.37% | 80 | 17.81% | 70.26% |
| 98.00% | confidence_high | 781 | 94.75% | 41 | 19.23% | 78.97% |
| 98.00% | confidence_medium | 1340 | 92.16% | 105 | 18.31% | 65.35% |

99%-target fold thresholds: {'0': 0.3550260365009308, '1': 0.8680243492126465, '2': 0.18142428994178772}. Outer Brier: 0.0935; cross entropy: 0.3173. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 26.84%. Prior 347-reference KEEP recall: 90.67%.

False rejects at the validation 99% target:

- Implementation Manager - Moveworks (servicenow; medium; described): score 0.06651 < 0.86802. Technical architecture, product/program leadership or technical solution work.
- Staff Security Incident Commander (servicenow; high; described): score 0.08778 < 0.86802. Cybersecurity or technical information-security work.
- IT Compliance & Assurance Manager (leidos; medium; described): score 0.11101 < 0.35503. Cybersecurity or technical information-security work.
- Regional Service Delivery Manager (leidos; high; described): score 0.11320 < 0.35503. Technical architecture, product/program leadership or technical solution work.
- Senior IT Project Manager (leidos; medium; described): score 0.11415 < 0.35503. Technical architecture, product/program leadership or technical solution work.
- Advisory Solution Consultant - State and Local (servicenow; medium; described): score 0.11994 < 0.86802. Technical architecture, product/program leadership or technical solution work.
- Facilities Operations Manager (leidos; medium; described): score 0.13458 < 0.35503. Plausibly adjacent engineering, controls, electronics, integration or technical analysis.
- Director of Expert Services, ITAM and ITOM (servicenow; medium; described): score 0.14988 < 0.86802. Technical architecture, product/program leadership or technical solution work.
- Principal CIDO IT Program Manager / Sr. Principal CIDO IT Program Manager (northrop-grumman; medium; described): score 0.15003 < 0.18142. Technical architecture, product/program leadership or technical solution work.
- SAP Principal SLP/MDG Systems Expert (rtx; high; described): score 0.15235 < 0.18142. Explicit software development or software engineering work.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Associate Procurement Agent (boeing): score 0.05894; label confidence high.
- Procurement Agent (Procurement Agent-General) (boeing): score 0.05924; label confidence high.
- Senior Manager, Americas Benefits (servicenow): score 0.06150; label confidence high.
- Sr Strategic Sourcing Manager (servicenow): score 0.06213; label confidence high.
- Sr Deal Desk Manager (servicenow): score 0.06378; label confidence high.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 2 false rejects, 99.54% recall.
- infrastructure-support: 379 KEEP, 21 false rejects, 94.46% recall.
- cybersecurity: 176 KEEP, 6 false rejects, 96.59% recall.
- engineering: 760 KEEP, 21 false rejects, 97.24% recall.
- data-ai: 105 KEEP, 2 false rejects, 98.10% recall.
- technical-leadership: 262 KEEP, 37 false rejects, 85.88% recall.
- company_masked: 16 decision flips; mean absolute score shift 0.0040; combined recall 95.66%.
- body_tail: 251 decision flips; mean absolute score shift 0.0931; combined recall 87.41%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 2.265 ms/posting, 441.5 postings/second. Single-post median by fold: [3.13, 3.2, 6.12]. Maximum training allocated GPU MiB: 160.8.
- Clean-process benchmark, 40 outer rows: GPU 2.823 ms/posting / 354.2 per second; batch-one median 5.15 ms; peak allocated/reserved 70.8/96.0 MiB. CPU four-thread median 5.29 ms / 183.4 per second; process high-water RSS after CPU 892.5 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.066515, 0 false rejects, 0.26% evaluation rejection, 1.00% cache rejection.
- 99.00% empirical recall constraint: threshold 0.158514, 21 false rejects, 4.23% evaluation rejection, 11.04% cache rejection.
- 98.00% empirical recall constraint: threshold 0.187105, 42 false rejects, 7.01% evaluation rejection, 17.56% cache rejection.

### bert-tiny / title_body / weighted

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 98.16% | 39 | 7.53% | 80.60% |
| 100.00% | description | 914 | 96.50% | 32 | 12.77% | 78.38% |
| 100.00% | title_only | 1207 | 99.42% | 7 | 3.51% | 86.79% |
| 100.00% | confidence_high | 781 | 97.70% | 18 | 12.82% | 86.15% |
| 100.00% | confidence_medium | 1340 | 98.43% | 21 | 4.29% | 70.42% |
| 99.00% | combined | 2121 | 96.51% | 74 | 10.83% | 74.39% |
| 99.00% | description | 914 | 94.42% | 51 | 16.48% | 73.30% |
| 99.00% | title_only | 1207 | 98.09% | 23 | 6.49% | 76.53% |
| 99.00% | confidence_high | 781 | 96.29% | 29 | 16.27% | 82.42% |
| 99.00% | confidence_medium | 1340 | 96.64% | 45 | 7.49% | 63.71% |
| 98.00% | combined | 2121 | 95.71% | 91 | 13.83% | 75.34% |
| 98.00% | description | 914 | 93.11% | 63 | 18.03% | 69.86% |
| 98.00% | title_only | 1207 | 97.68% | 28 | 10.60% | 82.50% |
| 98.00% | confidence_high | 781 | 95.01% | 39 | 17.65% | 78.21% |
| 98.00% | confidence_medium | 1340 | 96.12% | 52 | 11.48% | 72.63% |

99%-target fold thresholds: {'0': 0.25774553418159485, '1': 0.9882297515869141, '2': 0.4087577760219574}. Outer Brier: 0.0991; cross entropy: 0.3548. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 20.42%. Prior 347-reference KEEP recall: 94.00%.

False rejects at the validation 99% target:

- Implementation Manager - Moveworks (servicenow; medium; described): score 0.00811 < 0.98823. Technical architecture, product/program leadership or technical solution work.
- Boeing Summer 2027 Internship Program (Paid) – Data Analytics Intern (boeing; medium; title-only): score 0.00822 < 0.98823. Title indicates data ai work; duties cannot be verified because the description is unavailable.
- Advisory Solution Consultant - State and Local (servicenow; medium; described): score 0.00866 < 0.98823. Technical architecture, product/program leadership or technical solution work.
- Staff Security Incident Commander (servicenow; high; described): score 0.01090 < 0.98823. Cybersecurity or technical information-security work.
- Nondestructive Test (NDT) Technician - UT & RT (boeing; medium; described): score 0.01214 < 0.98823. Plausibly adjacent engineering, controls, electronics, integration or technical analysis.
- Director of Expert Services, ITAM and ITOM (servicenow; medium; described): score 0.01230 < 0.98823. Technical architecture, product/program leadership or technical solution work.
- Principal Customer Success Manager ( Armis/Veza) (servicenow; medium; described): score 0.01261 < 0.98823. Technical architecture, product/program leadership or technical solution work.
- Senior Artificial Intelligence Program Manager (boeing; medium; title-only): score 0.01559 < 0.98823. Title indicates technical leadership work; duties cannot be verified because the description is unavailable.
- Boeing Summer 2027 Internship Program (Paid) - Facilities Engineering (boeing; medium; title-only): score 0.01631 < 0.98823. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Sr Customer Success Manager (Armis/Veza) (servicenow; medium; described): score 0.01817 < 0.98823. Technical architecture, product/program leadership or technical solution work.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Associate Procurement Agent (boeing): score 0.00740; label confidence high.
- Procurement Agent (Procurement Agent-General) (boeing): score 0.00741; label confidence high.
- Senior Manager, Americas Benefits (servicenow): score 0.00742; label confidence high.
- Sr Strategic Sourcing Manager (servicenow): score 0.00744; label confidence high.
- Senior Manager, Influencer Marketing & Amplification Lead (servicenow): score 0.00759; label confidence high.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 0 false rejects, 100.00% recall.
- infrastructure-support: 379 KEEP, 12 false rejects, 96.83% recall.
- cybersecurity: 176 KEEP, 5 false rejects, 97.16% recall.
- engineering: 760 KEEP, 17 false rejects, 97.76% recall.
- data-ai: 105 KEEP, 2 false rejects, 98.10% recall.
- technical-leadership: 262 KEEP, 38 false rejects, 85.50% recall.
- company_masked: 11 decision flips; mean absolute score shift 0.0038; combined recall 96.18%.
- body_tail: 282 decision flips; mean absolute score shift 0.0631; combined recall 87.46%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 2.261 ms/posting, 442.3 postings/second. Single-post median by fold: [3.13, 3.17, 6.07]. Maximum training allocated GPU MiB: 160.8.
- Clean-process benchmark, 40 outer rows: GPU 2.775 ms/posting / 360.4 per second; batch-one median 5.13 ms; peak allocated/reserved 70.8/96.0 MiB. CPU four-thread median 5.09 ms / 191.1 per second; process high-water RSS after CPU 892.5 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.008109, 0 false rejects, 1.16% evaluation rejection, 1.60% cache rejection.
- 99.00% empirical recall constraint: threshold 0.147813, 21 false rejects, 4.42% evaluation rejection, 6.72% cache rejection.
- 98.00% empirical recall constraint: threshold 0.274911, 42 false rejects, 7.16% evaluation rejection, 9.12% cache rejection.

### deberta-small / title / all

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 98.44% | 33 | 5.99% | 79.38% |
| 100.00% | description | 914 | 98.58% | 13 | 5.09% | 77.97% |
| 100.00% | title_only | 1207 | 98.34% | 20 | 6.69% | 80.20% |
| 100.00% | confidence_high | 781 | 98.59% | 11 | 5.62% | 80.70% |
| 100.00% | confidence_medium | 1340 | 98.36% | 22 | 6.22% | 78.64% |
| 99.00% | combined | 2121 | 96.18% | 81 | 11.46% | 73.53% |
| 99.00% | description | 914 | 96.06% | 36 | 9.58% | 67.57% |
| 99.00% | title_only | 1207 | 96.27% | 45 | 12.91% | 76.92% |
| 99.00% | confidence_high | 781 | 96.67% | 26 | 9.86% | 74.00% |
| 99.00% | confidence_medium | 1340 | 95.90% | 55 | 12.45% | 73.30% |
| 98.00% | combined | 2121 | 94.53% | 116 | 14.72% | 70.48% |
| 98.00% | description | 914 | 94.86% | 47 | 11.22% | 63.85% |
| 98.00% | title_only | 1207 | 94.28% | 69 | 17.42% | 73.76% |
| 98.00% | confidence_high | 781 | 95.77% | 33 | 11.24% | 71.05% |
| 98.00% | confidence_medium | 1340 | 93.81% | 83 | 16.86% | 70.25% |

99%-target fold thresholds: {'0': 0.16215381026268005, '1': 0.5291102528572083, '2': 0.0259811170399189}. Outer Brier: 0.1094; cross entropy: 0.3740. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 34.97%. Prior 347-reference KEEP recall: 97.33%.

False rejects at the validation 99% target:

- Principal Network Evaluator (NE-4) (leidos; medium; title-only): score 0.04418 < 0.16215. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Senior Program Manager (leidos; high; described): score 0.04496 < 0.16215. Technical architecture, product/program leadership or technical solution work.
- Regional Service Delivery Manager (leidos; high; described): score 0.04533 < 0.16215. Technical architecture, product/program leadership or technical solution work.
- Sr. Systems Administrator (leidos; medium; title-only): score 0.04695 < 0.16215. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Knowledge Manager (SharePoint) - Senior (leidos; medium; title-only): score 0.05040 < 0.16215. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Application Developer (SWE-2) (leidos; medium; title-only): score 0.05081 < 0.16215. Title indicates software work; duties cannot be verified because the description is unavailable.
- IA Lead (leidos; medium; title-only): score 0.05129 < 0.16215. Title indicates cybersecurity work; duties cannot be verified because the description is unavailable.
- Power Platform Principal (Part Time) (leidos; high; described): score 0.05169 < 0.16215. Explicit software development or software engineering work.
- ICAM Program Manager (leidos; medium; title-only): score 0.05181 < 0.16215. Title indicates technical leadership work; duties cannot be verified because the description is unavailable.
- Sr. Network Administrator (leidos; medium; title-only): score 0.05437 < 0.16215. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Procurement Specialist (Buyer) (rtx): score 0.02203; label confidence high.
- Permitting Coordinator (parsons): score 0.02207; label confidence high.
- Permitting / Regulatory Compliance Specialist (parsons): score 0.02296; label confidence high.
- Military Family Life Counselor (MFLC), West of Mississippi Region (kbr): score 0.02306; label confidence high.
- RFI / Submittal Coordinator (parsons): score 0.02477; label confidence high.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 6 false rejects, 98.63% recall.
- infrastructure-support: 379 KEEP, 33 false rejects, 91.29% recall.
- cybersecurity: 176 KEEP, 7 false rejects, 96.02% recall.
- engineering: 760 KEEP, 5 false rejects, 99.34% recall.
- data-ai: 105 KEEP, 2 false rejects, 98.10% recall.
- technical-leadership: 262 KEEP, 28 false rejects, 89.31% recall.
- company_masked: 7 decision flips; mean absolute score shift 0.0035; combined recall 95.95%.
- body_tail: 0 decision flips; mean absolute score shift 0.0000; combined recall 96.18%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 1.388 ms/posting, 720.4 postings/second. Single-post median by fold: [15.09, 15.18, 14.98]. Maximum training allocated GPU MiB: 2992.4.
- Clean-process benchmark, 40 outer rows: GPU 1.464 ms/posting / 683.2 per second; batch-one median 14.17 ms; peak allocated/reserved 641.7/670.0 MiB. CPU four-thread median 45.97 ms / 21.7 per second; process high-water RSS after CPU 1600.7 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.031039, 0 false rejects, 0.37% evaluation rejection, 1.33% cache rejection.
- 99.00% empirical recall constraint: threshold 0.057867, 21 false rejects, 4.42% evaluation rejection, 5.12% cache rejection.
- 98.00% empirical recall constraint: threshold 0.078713, 42 false rejects, 7.19% evaluation rejection, 7.05% cache rejection.

### deberta-small / title / description

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 97.55% | 52 | 4.83% | 59.69% |
| 100.00% | description | 914 | 98.80% | 11 | 3.11% | 69.44% |
| 100.00% | title_only | 1207 | 96.60% | 41 | 6.16% | 55.91% |
| 100.00% | confidence_high | 781 | 98.72% | 10 | 3.45% | 71.43% |
| 100.00% | confidence_medium | 1340 | 96.87% | 42 | 5.68% | 55.32% |
| 99.00% | combined | 2121 | 95.76% | 90 | 8.39% | 59.82% |
| 99.00% | description | 914 | 97.16% | 26 | 5.95% | 62.32% |
| 99.00% | title_only | 1207 | 94.70% | 64 | 10.26% | 58.71% |
| 99.00% | confidence_high | 781 | 97.57% | 19 | 6.11% | 69.35% |
| 99.00% | confidence_medium | 1340 | 94.70% | 71 | 9.79% | 56.17% |
| 98.00% | combined | 2121 | 93.68% | 134 | 12.03% | 58.26% |
| 98.00% | description | 914 | 95.51% | 41 | 8.37% | 57.73% |
| 98.00% | title_only | 1207 | 92.29% | 93 | 14.83% | 58.48% |
| 98.00% | confidence_high | 781 | 96.29% | 29 | 8.38% | 65.88% |
| 98.00% | confidence_medium | 1340 | 92.16% | 105 | 14.26% | 55.51% |

99%-target fold thresholds: {'0': 0.13796374201774597, '1': 0.06665711104869843, '2': 0.21668744087219238}. Outer Brier: 0.1452; cross entropy: 0.4593. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 27.79%. Prior 347-reference KEEP recall: 98.00%.

False rejects at the validation 99% target:

- Advisory Solution Consultant, SLED Southwest (Armis) (servicenow; high; described): score 0.02613 < 0.06666. Technical architecture, product/program leadership or technical solution work.
- Principal Customer Success Manager ( Armis/Veza) (servicenow; medium; described): score 0.03039 < 0.06666. Technical architecture, product/program leadership or technical solution work.
- Advisory Solution Consultant, SLED (Armis) (servicenow; high; described): score 0.03263 < 0.06666. Technical architecture, product/program leadership or technical solution work.
- Sr Advisory Solution Consultant, Federal (Armis/Veza) - Washington DC Area (servicenow; high; described): score 0.03286 < 0.06666. Technical architecture, product/program leadership or technical solution work.
- Director of Expert Services, ITAM and ITOM (servicenow; medium; described): score 0.03411 < 0.06666. Technical architecture, product/program leadership or technical solution work.
- Advisory Solution Consultant - State and Local (servicenow; medium; described): score 0.05355 < 0.06666. Technical architecture, product/program leadership or technical solution work.
- Senior Advisory Solution Consultant- Moveworks (Energy, Transportation & Core Services) (servicenow; medium; described): score 0.05938 < 0.06666. Technical architecture, product/program leadership or technical solution work.
- Regional Service Delivery Manager (leidos; high; described): score 0.06740 < 0.13796. Technical architecture, product/program leadership or technical solution work.
- Sr. Systems Administrator (leidos; medium; title-only): score 0.06754 < 0.13796. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Senior Program Manager (leidos; high; described): score 0.06946 < 0.13796. Technical architecture, product/program leadership or technical solution work.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Senior Manager, Americas Benefits (servicenow): score 0.02467; label confidence high.
- Associate Procurement Agent (boeing): score 0.02521; label confidence high.
- Procurement Agent 2 (boeing): score 0.02622; label confidence medium.
- Lead Client Director - Strategic Retail - Arkansas (servicenow): score 0.02732; label confidence high.
- Experienced Supply Chain Recovery (boeing): score 0.02786; label confidence high.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 2 false rejects, 99.54% recall.
- infrastructure-support: 379 KEEP, 46 false rejects, 87.86% recall.
- cybersecurity: 176 KEEP, 10 false rejects, 94.32% recall.
- engineering: 760 KEEP, 3 false rejects, 99.61% recall.
- data-ai: 105 KEEP, 6 false rejects, 94.29% recall.
- technical-leadership: 262 KEEP, 23 false rejects, 91.22% recall.
- company_masked: 3 decision flips; mean absolute score shift 0.0029; combined recall 95.76%.
- body_tail: 0 decision flips; mean absolute score shift 0.0000; combined recall 95.76%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 1.382 ms/posting, 723.6 postings/second. Single-post median by fold: [15.47, 15.48, 15.19]. Maximum training allocated GPU MiB: 2990.0.
- Clean-process benchmark, 40 outer rows: GPU 1.462 ms/posting / 684.2 per second; batch-one median 14.77 ms; peak allocated/reserved 641.7/670.0 MiB. CPU four-thread median 45.94 ms / 21.6 per second; process high-water RSS after CPU 1602.3 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.026127, 0 false rejects, 0.07% evaluation rejection, 0.13% cache rejection.
- 99.00% empirical recall constraint: threshold 0.073716, 21 false rejects, 2.44% evaluation rejection, 3.46% cache rejection.
- 98.00% empirical recall constraint: threshold 0.083467, 42 false rejects, 4.05% evaluation rejection, 3.93% cache rejection.

### deberta-small / title / weighted

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 98.11% | 40 | 5.88% | 74.52% |
| 100.00% | description | 914 | 98.14% | 17 | 4.66% | 68.52% |
| 100.00% | title_only | 1207 | 98.09% | 23 | 6.82% | 77.67% |
| 100.00% | confidence_high | 781 | 98.34% | 13 | 4.93% | 74.00% |
| 100.00% | confidence_medium | 1340 | 97.99% | 27 | 6.47% | 74.77% |
| 99.00% | combined | 2121 | 96.13% | 82 | 10.79% | 71.53% |
| 99.00% | description | 914 | 96.50% | 32 | 8.02% | 65.59% |
| 99.00% | title_only | 1207 | 95.86% | 50 | 12.91% | 74.36% |
| 99.00% | confidence_high | 781 | 96.80% | 25 | 8.38% | 70.59% |
| 99.00% | confidence_medium | 1340 | 95.75% | 57 | 12.27% | 71.92% |
| 98.00% | combined | 2121 | 94.39% | 119 | 14.57% | 69.41% |
| 98.00% | description | 914 | 95.30% | 43 | 10.01% | 62.93% |
| 98.00% | title_only | 1207 | 93.70% | 76 | 18.08% | 72.16% |
| 98.00% | confidence_high | 781 | 96.03% | 31 | 10.06% | 69.61% |
| 98.00% | confidence_medium | 1340 | 93.43% | 88 | 17.34% | 69.34% |

99%-target fold thresholds: {'0': 0.15926575660705566, '1': 0.26536643505096436, '2': 0.0179892685264349}. Outer Brier: 0.1090; cross entropy: 0.3757. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 35.73%. Prior 347-reference KEEP recall: 98.00%.

False rejects at the validation 99% target:

- Principal Network Evaluator (NE-4) (leidos; medium; title-only): score 0.03957 < 0.15927. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Senior Program Manager (leidos; high; described): score 0.03999 < 0.15927. Technical architecture, product/program leadership or technical solution work.
- Knowledge Manager (SharePoint) - Senior (leidos; medium; title-only): score 0.04244 < 0.15927. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Regional Service Delivery Manager (leidos; high; described): score 0.04248 < 0.15927. Technical architecture, product/program leadership or technical solution work.
- Sr. Systems Administrator (leidos; medium; title-only): score 0.04364 < 0.15927. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- ICAM Program Manager (leidos; medium; title-only): score 0.04523 < 0.15927. Title indicates technical leadership work; duties cannot be verified because the description is unavailable.
- Power Platform Principal (Part Time) (leidos; high; described): score 0.04648 < 0.15927. Explicit software development or software engineering work.
- Application Developer (SWE-2) (leidos; medium; title-only): score 0.04701 < 0.15927. Title indicates software work; duties cannot be verified because the description is unavailable.
- IA Lead (leidos; medium; title-only): score 0.04832 < 0.15927. Title indicates cybersecurity work; duties cannot be verified because the description is unavailable.
- Sr. Network Administrator (leidos; medium; title-only): score 0.04948 < 0.15927. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Permitting Coordinator (parsons): score 0.01570; label confidence high.
- Military Family Life Counselor (MFLC), West of Mississippi Region (kbr): score 0.01609; label confidence high.
- Procurement Specialist (Buyer) (rtx): score 0.01637; label confidence high.
- RFI / Submittal Coordinator (parsons): score 0.01693; label confidence high.
- Procurement Agent (Procurement Admin) (boeing): score 0.03315; label confidence medium.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 6 false rejects, 98.63% recall.
- infrastructure-support: 379 KEEP, 42 false rejects, 88.92% recall.
- cybersecurity: 176 KEEP, 8 false rejects, 95.45% recall.
- engineering: 760 KEEP, 5 false rejects, 99.34% recall.
- data-ai: 105 KEEP, 3 false rejects, 97.14% recall.
- technical-leadership: 262 KEEP, 18 false rejects, 93.13% recall.
- company_masked: 8 decision flips; mean absolute score shift 0.0034; combined recall 95.95%.
- body_tail: 0 decision flips; mean absolute score shift 0.0000; combined recall 96.13%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 1.395 ms/posting, 716.9 postings/second. Single-post median by fold: [15.02, 15.86, 15.12]. Maximum training allocated GPU MiB: 2990.5.
- Clean-process benchmark, 40 outer rows: GPU 1.401 ms/posting / 713.8 per second; batch-one median 13.97 ms; peak allocated/reserved 641.7/670.0 MiB. CPU four-thread median 46.30 ms / 20.2 per second; process high-water RSS after CPU 1601.3 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.023481, 0 false rejects, 0.41% evaluation rejection, 1.53% cache rejection.
- 99.00% empirical recall constraint: threshold 0.050843, 21 false rejects, 4.12% evaluation rejection, 5.39% cache rejection.
- 98.00% empirical recall constraint: threshold 0.068017, 42 false rejects, 6.59% evaluation rejection, 6.59% cache rejection.

### deberta-small / title_body / all

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 99.39% | 13 | 4.68% | 89.60% |
| 100.00% | description | 914 | 99.12% | 8 | 5.44% | 87.30% |
| 100.00% | title_only | 1207 | 99.59% | 5 | 4.11% | 91.94% |
| 100.00% | confidence_high | 781 | 99.62% | 3 | 5.72% | 94.83% |
| 100.00% | confidence_medium | 1340 | 99.25% | 10 | 4.05% | 85.07% |
| 99.00% | combined | 2121 | 97.74% | 48 | 7.53% | 76.12% |
| 99.00% | description | 914 | 95.73% | 39 | 10.96% | 69.29% |
| 99.00% | title_only | 1207 | 99.25% | 9 | 4.90% | 87.84% |
| 99.00% | confidence_high | 781 | 97.44% | 20 | 10.65% | 81.48% |
| 99.00% | confidence_medium | 1340 | 97.91% | 28 | 5.62% | 69.89% |
| 98.00% | combined | 2121 | 96.84% | 67 | 9.78% | 74.33% |
| 98.00% | description | 914 | 94.64% | 49 | 12.17% | 65.25% |
| 98.00% | title_only | 1207 | 98.51% | 18 | 7.95% | 85.00% |
| 98.00% | confidence_high | 781 | 96.41% | 28 | 11.83% | 76.67% |
| 98.00% | confidence_medium | 1340 | 97.09% | 39 | 8.52% | 72.34% |

99%-target fold thresholds: {'0': 0.31995078921318054, '1': 0.8189460635185242, '2': 0.02489461936056614}. Outer Brier: 0.1100; cross entropy: 0.3558. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 16.45%. Prior 347-reference KEEP recall: 96.00%.

False rejects at the validation 99% target:

- Senior Low Observables (LO) RCS Analyst (boeing; medium; title-only): score 0.01020 < 0.81895. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- ABAD COMSEC Custodian**This position is located at Ramstein AFB, Germany (International Assignment) **NO REMOTE WORK** (parsons; high; described): score 0.02433 < 0.02489. Cybersecurity or technical information-security work.
- Lab Test Program Integrator (Experienced or Lead) (boeing; medium; title-only): score 0.03508 < 0.81895. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Implementation Manager - Moveworks (servicenow; medium; described): score 0.04316 < 0.81895. Technical architecture, product/program leadership or technical solution work.
- Mid-Level or Experienced Electronics Programs Unit IPT Lead (Project Management Specialist) (boeing; medium; title-only): score 0.05516 < 0.81895. Title indicates technical leadership work; duties cannot be verified because the description is unavailable.
- Regional Service Delivery Manager (leidos; high; described): score 0.06169 < 0.31995. Technical architecture, product/program leadership or technical solution work.
- Partner Technology Architect (servicenow; medium; described): score 0.06489 < 0.81895. Technical architecture, product/program leadership or technical solution work.
- Quality Systems Specialist (Associate or Experienced)) (boeing; medium; described): score 0.07499 < 0.81895. Plausibly adjacent engineering, controls, electronics, integration or technical analysis.
- ICITAP Forensic Digital Evidence Advisor (amentum; high; described): score 0.07696 < 0.81895. Digital forensics of phones/computers, operating systems and evidence-extraction tools.
- Electrical Engineer, Sr. (leidos; high; described): score 0.09323 < 0.31995. Plausibly adjacent engineering, controls, electronics, integration or technical analysis.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Nurse Practitioner (boeing): score 0.00772; label confidence medium.
- Experienced Facilities Plant Maintenance Specialist (boeing): score 0.00773; label confidence medium.
- Silk Screener Decorative Panels B - 79805 (boeing): score 0.00813; label confidence medium.
- Assembler Power Plant B - 91104 (boeing): score 0.00815; label confidence medium.
- Mid-Level Disability Management Specialist (boeing): score 0.00819; label confidence medium.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 0 false rejects, 100.00% recall.
- infrastructure-support: 379 KEEP, 2 false rejects, 99.47% recall.
- cybersecurity: 176 KEEP, 3 false rejects, 98.30% recall.
- engineering: 760 KEEP, 18 false rejects, 97.63% recall.
- data-ai: 105 KEEP, 0 false rejects, 100.00% recall.
- technical-leadership: 262 KEEP, 25 false rejects, 90.46% recall.
- company_masked: 9 decision flips; mean absolute score shift 0.0100; combined recall 97.50%.
- body_tail: 381 decision flips; mean absolute score shift 0.1880; combined recall 82.60%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 12.639 ms/posting, 79.1 postings/second. Single-post median by fold: [15.24, 16.61, 18.54]. Maximum training allocated GPU MiB: 3367.5.
- Clean-process benchmark, 40 outer rows: GPU 13.385 ms/posting / 74.7 per second; batch-one median 16.92 ms; peak allocated/reserved 1030.1/1112.0 MiB. CPU four-thread median 152.41 ms / 6.2 per second; process high-water RSS after CPU 1629.8 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.010196, 0 false rejects, 0.97% evaluation rejection, 0.00% cache rejection.
- 99.00% empirical recall constraint: threshold 0.047913, 21 false rejects, 5.96% evaluation rejection, 11.98% cache rejection.
- 98.00% empirical recall constraint: threshold 0.089499, 42 false rejects, 8.69% evaluation rejection, 19.83% cache rejection.

### deberta-small / title_body / description

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 99.62% | 8 | 1.46% | 79.49% |
| 100.00% | description | 914 | 99.12% | 8 | 3.36% | 79.49% |
| 100.00% | title_only | 1207 | 100.00% | 0 | 0.00% | — |
| 100.00% | confidence_high | 781 | 99.23% | 6 | 3.65% | 83.78% |
| 100.00% | confidence_medium | 1340 | 99.85% | 2 | 0.12% | 0.00% |
| 99.00% | combined | 2121 | 98.07% | 41 | 5.58% | 72.48% |
| 99.00% | description | 914 | 98.36% | 15 | 6.30% | 79.45% |
| 99.00% | title_only | 1207 | 97.85% | 26 | 5.03% | 65.79% |
| 99.00% | confidence_high | 781 | 98.98% | 8 | 6.51% | 87.88% |
| 99.00% | confidence_medium | 1340 | 97.54% | 33 | 5.02% | 60.24% |
| 98.00% | combined | 2121 | 96.28% | 79 | 7.83% | 62.20% |
| 98.00% | description | 914 | 98.36% | 15 | 6.47% | 80.00% |
| 98.00% | title_only | 1207 | 94.70% | 64 | 8.87% | 52.24% |
| 98.00% | confidence_high | 781 | 98.98% | 8 | 6.71% | 88.24% |
| 98.00% | confidence_medium | 1340 | 94.70% | 71 | 8.52% | 49.65% |

99%-target fold thresholds: {'0': 0.11619981378316879, '1': 0.6705407500267029, '2': 0.21190012991428375}. Outer Brier: 0.2662; cross entropy: 0.7397. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 8.70%. Prior 347-reference KEEP recall: 99.33%.

False rejects at the validation 99% target:

- Regional Service Delivery Manager (leidos; high; described): score 0.02972 < 0.11620. Technical architecture, product/program leadership or technical solution work.
- ServiceNow Software Engineer (leidos; medium; described): score 0.05804 < 0.11620. Explicit ServiceNow software-engineering title, but the available description is boilerplate rather than duties.
- Implementation Manager - Moveworks (servicenow; medium; described): score 0.05987 < 0.67054. Technical architecture, product/program leadership or technical solution work.
- Partner Technology Architect (servicenow; medium; described): score 0.08305 < 0.67054. Technical architecture, product/program leadership or technical solution work.
- Systems Administrator - Vulnerability Mitigation (leidos; medium; title-only): score 0.08559 < 0.11620. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.
- Manager of Transmission Line Engineering (leidos; medium; title-only): score 0.09328 < 0.11620. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Hypersonics Test lead (leidos; medium; title-only): score 0.10137 < 0.11620. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Network Impact Assessment (NIA) Technician (leidos; high; described): score 0.10247 < 0.11620. Technical computing, infrastructure, networks, systems or application support.
- Software Architecture Lead; w/ DoD Secret (leidos; medium; title-only): score 0.10387 < 0.11620. Title indicates software work; duties cannot be verified because the description is unavailable.
- Network Operations Analytics Engineer (leidos; medium; title-only): score 0.10690 < 0.11620. Title indicates infrastructure support work; duties cannot be verified because the description is unavailable.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Director, Compensation (leidos): score 0.02242; label confidence high.
- Material Procurement / Supply Chain Analyst (leidos): score 0.02313; label confidence high.
- HR Assistant (leidos): score 0.02751; label confidence high.
- Bilingual Customer Service Representative (ABQ) (leidos): score 0.02840; label confidence high.
- Antarctic Administrative Coordinator (Alternate) (leidos): score 0.03150; label confidence high.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 4 false rejects, 99.09% recall.
- infrastructure-support: 379 KEEP, 5 false rejects, 98.68% recall.
- cybersecurity: 176 KEEP, 2 false rejects, 98.86% recall.
- engineering: 760 KEEP, 17 false rejects, 97.76% recall.
- data-ai: 105 KEEP, 0 false rejects, 100.00% recall.
- technical-leadership: 262 KEEP, 13 false rejects, 95.04% recall.
- company_masked: 8 decision flips; mean absolute score shift 0.0073; combined recall 97.97%.
- body_tail: 456 decision flips; mean absolute score shift 0.1959; combined recall 79.54%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 12.665 ms/posting, 79.0 postings/second. Single-post median by fold: [15.17, 15.38, 18.99]. Maximum training allocated GPU MiB: 3368.5.
- Clean-process benchmark, 40 outer rows: GPU 13.449 ms/posting / 74.4 per second; batch-one median 17.03 ms; peak allocated/reserved 1030.1/1112.0 MiB. CPU four-thread median 153.55 ms / 6.4 per second; process high-water RSS after CPU 1632.7 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.029722, 0 false rejects, 0.15% evaluation rejection, 0.13% cache rejection.
- 99.00% empirical recall constraint: threshold 0.125517, 19 false rejects, 3.07% evaluation rejection, 4.59% cache rejection.
- 98.00% empirical recall constraint: threshold 0.145455, 42 false rejects, 4.57% evaluation rejection, 5.19% cache rejection.

### deberta-small / title_body / weighted

| Validation target | Subset | KEEP n | KEEP recall | False rejects | Rejection | Reject precision |
|---|---|---:|---:|---:|---:|---:|
| 100.00% | combined | 2121 | 99.25% | 16 | 4.65% | 87.10% |
| 100.00% | description | 914 | 98.69% | 12 | 5.26% | 80.33% |
| 100.00% | title_only | 1207 | 99.67% | 4 | 4.17% | 93.65% |
| 100.00% | confidence_high | 781 | 99.10% | 7 | 5.42% | 87.27% |
| 100.00% | confidence_medium | 1340 | 99.33% | 9 | 4.17% | 86.96% |
| 99.00% | combined | 2121 | 96.94% | 65 | 8.09% | 69.91% |
| 99.00% | description | 914 | 95.95% | 37 | 10.27% | 68.91% |
| 99.00% | title_only | 1207 | 97.68% | 28 | 6.42% | 71.13% |
| 99.00% | confidence_high | 781 | 97.44% | 20 | 9.96% | 80.20% |
| 99.00% | confidence_medium | 1340 | 96.64% | 45 | 6.95% | 60.87% |
| 98.00% | combined | 2121 | 94.96% | 107 | 10.49% | 61.79% |
| 98.00% | description | 914 | 94.20% | 53 | 12.42% | 63.19% |
| 98.00% | title_only | 1207 | 95.53% | 54 | 9.01% | 60.29% |
| 98.00% | confidence_high | 781 | 96.41% | 28 | 11.64% | 76.27% |
| 98.00% | confidence_medium | 1340 | 94.10% | 79 | 9.79% | 51.23% |

99%-target fold thresholds: {'0': 0.18006721138954163, '1': 0.9357327222824097, '2': 0.38041573762893677}. Outer Brier: 0.1337; cross entropy: 0.4283. These probabilities are not calibrated confidence.
AMBIGUOUS rejection: 17.96%. Prior 347-reference KEEP recall: 94.67%.

False rejects at the validation 99% target:

- Partner Technical Architect (servicenow; high; described): score 0.01199 < 0.93573. Technical architecture, product/program leadership or technical solution work.
- Implementation Manager - Moveworks (servicenow; medium; described): score 0.01259 < 0.93573. Technical architecture, product/program leadership or technical solution work.
- Flight Test Lead (boeing; medium; title-only): score 0.01453 < 0.93573. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Director of Expert Services, ITAM and ITOM (servicenow; medium; described): score 0.01611 < 0.93573. Technical architecture, product/program leadership or technical solution work.
- Partner Technology Architect (servicenow; medium; described): score 0.01663 < 0.93573. Technical architecture, product/program leadership or technical solution work.
- Senior Low Observables (LO) RCS Analyst (boeing; medium; title-only): score 0.01899 < 0.93573. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Quality Systems Specialist (Associate or Experienced)) (boeing; medium; described): score 0.01993 < 0.93573. Plausibly adjacent engineering, controls, electronics, integration or technical analysis.
- ICITAP Forensic Digital Evidence Advisor (amentum; high; described): score 0.02068 < 0.93573. Digital forensics of phones/computers, operating systems and evidence-extraction tools.
- Senior Specialized Test Equipment (STE) Integration Lead (boeing; medium; title-only): score 0.02426 < 0.93573. Title indicates engineering work; duties cannot be verified because the description is unavailable.
- Regional Service Delivery Manager (leidos; high; described): score 0.03131 < 0.18007. Technical architecture, product/program leadership or technical solution work.

Lowest-score correctly rejected binary examples (model scores are not calibrated confidence):

- Sr Pricing Manager (servicenow): score 0.01121; label confidence high.
- Sr Mgr, Customer Success Mgmt (servicenow): score 0.01171; label confidence high.
- Aircraft Assembler Production Test Mechanic (boeing): score 0.01236; label confidence medium.
- Experienced Facilities Plant Maintenance Specialist (boeing): score 0.01253; label confidence medium.
- Distribution Business Manager (nxp-semiconductors): score 0.01268; label confidence high.

Core technical KEEP category results at 99%-validation target:

- software: 439 KEEP, 1 false rejects, 99.77% recall.
- infrastructure-support: 379 KEEP, 6 false rejects, 98.42% recall.
- cybersecurity: 176 KEEP, 3 false rejects, 98.30% recall.
- engineering: 760 KEEP, 24 false rejects, 96.84% recall.
- data-ai: 105 KEEP, 2 false rejects, 98.10% recall.
- technical-leadership: 262 KEEP, 29 false rejects, 88.93% recall.
- company_masked: 13 decision flips; mean absolute score shift 0.0078; combined recall 96.51%.
- body_tail: 185 decision flips; mean absolute score shift 0.1682; combined recall 91.04%.

Timing across employer folds (tokenization included):

- Full cross-fit stream: 12.680 ms/posting, 78.9 postings/second. Single-post median by fold: [15.21, 15.45, 19.01]. Maximum training allocated GPU MiB: 3368.2.
- Clean-process benchmark, 40 outer rows: GPU 13.370 ms/posting / 74.8 per second; batch-one median 16.88 ms; peak allocated/reserved 1030.1/1112.0 MiB. CPU four-thread median 157.71 ms / 6.3 per second; process high-water RSS after CPU 1631.1 MiB.

Retrospective combined ranking bounds, NOT usable deployment thresholds:

- 100.00% empirical recall constraint: threshold 0.011994, 0 false rejects, 0.07% evaluation rejection, 0.13% cache rejection.
- 99.00% empirical recall constraint: threshold 0.092189, 21 false rejects, 5.40% evaluation rejection, 8.25% cache rejection.
- 98.00% empirical recall constraint: threshold 0.260174, 42 false rejects, 6.74% evaluation rejection, 10.31% cache rejection.

## Full sweep and reproducibility

threshold-sweep.csv includes the complete fixed grid with all evidence/confidence slices and original-cache rejection. results.json additionally contains per-employer/category metrics, validation-selected points and retrospective slice-specific frontiers. These retrospective frontiers are evaluation-selected and cannot establish generalization.

See README.md for reproduction commands, validation-results.md for checks and file list, and interpretation.md for the final recommendation.

## Previously failing technical probes

All six are fixed legacy KEEP references, excluded from fitting and calibration. These are diagnostic cases, not a separately calibrated benchmark.

Validation target: 100.00%

| Candidate | Controls engineer | Kahua developer | PC support | Enterprise systems | Mechanical engineer | Cybersecurity PM |
|---|---|---|---|---|---|---|
| bert-tiny / title / all | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| bert-tiny / title / description | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| bert-tiny / title / weighted | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| bert-tiny / title_body / all | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| bert-tiny / title_body / description | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| bert-tiny / title_body / weighted | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title / all | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title / description | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title / weighted | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title_body / all | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title_body / description | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title_body / weighted | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |

Validation target: 99.00%

| Candidate | Controls engineer | Kahua developer | PC support | Enterprise systems | Mechanical engineer | Cybersecurity PM |
|---|---|---|---|---|---|---|
| bert-tiny / title / all | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| bert-tiny / title / description | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| bert-tiny / title / weighted | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| bert-tiny / title_body / all | KEEP | KEEP | REJECT | KEEP | KEEP | KEEP |
| bert-tiny / title_body / description | KEEP | KEEP | REJECT | KEEP | KEEP | KEEP |
| bert-tiny / title_body / weighted | KEEP | KEEP | REJECT | KEEP | KEEP | KEEP |
| deberta-small / title / all | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title / description | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title / weighted | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title_body / all | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title_body / description | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| deberta-small / title_body / weighted | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |

## Input evidence alignment

- deberta-small: 1158/1159 described binary inputs truncated; all recorded adjudication spans outside the body prefix for 322 rows (27.78%). This measures overlap with recorded evidence, not whether the remaining text is sufficient.
- bert-tiny: 1158/1159 described binary inputs truncated; all recorded adjudication spans outside the body prefix for 344 rows (29.68%). This measures overlap with recorded evidence, not whether the remaining text is sufficient.

## Same-host rule timing

Unchanged electrical-safe rules on curiosity: 0.0913 ms/posting, 10948.8 postings/second (median of five full passes). Every decision matches the Windows frozen baseline; both source hashes match. Compare with the four-thread CPU encoder benchmarks above; GPU throughput is a separate hardware path.
