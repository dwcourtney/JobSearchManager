# Task-specific compact cheap-reject experiment

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
|---|---:|---:|---:|---|
| 0 | 117 / 119 | 15 / 19 | 18 / 59 | aecom, kbr, northrop-grumman, servicenow |
| 1 | 89 / 54 | 11 / 29 | 50 / 114 | amentum, leidos, nxp-semiconductors, parsons, rtx |
| 2 | 54 / 153 | 14 / 20 | 82 / 24 | boeing, nvidia |

Only 11–15 validation KEEP examples exist per fold. Thus 100%, >=99%, and >=98% validation recall all allow zero validation misses and select the same score boundary. This resolution limit is explicit; it is not evidence that population recall is 99%. Every evaluated row receives an out-of-sample score, but shared fold models and employer clustering mean the rows are not independent trials.

Cache: the fixed 1,503-posting snapshot was previously production-holdout-excluded. Assign each posting to the fold whose training AND validation exclude its employer. Thus no training-set score contributes to reported cache rejection. Also report the 1,156 cache postings outside all reviewed references. No full-data model is fitted or evaluated on its training examples. This cross-fitted workload estimate does not measure a single final deployment model.

The rule baseline was already designed using these provisional references; its comparison favors the rules and is not independent validation of them. On the original 166-row development set it had 100% recall, 33.73% rejection, and 13.84% full-cache rejection. On the corrected, expanded 347-row evaluation below its rejection is different because the reference mix is different.

## Validation-selected operating points

Score is the binary softmax KEEP probability; reject strictly below threshold, equality stays KEEP. For each fold and target choose the largest permissible validation KEEP-score boundary, then freeze it before outer evaluation. No threshold is adjusted to rescue outer errors. The fixed threshold grid is also reported below.

| Candidate | Validation target | Evaluation KEEP recall | False rejects / 150 | Evaluation rejection | Reject precision | Full cache rejection | Unreviewed cache rejection |
|---|---:|---:|---:|---:|---:|---:|---:|
| Electrical-safe | — | 100.00% | 0 | 46.69% | 100.00% | 13.84% (208) | 3.98% (46) |
| deberta-small / title | 100.00% | 93.33% | 10 | 26.80% | 89.25% | 20.16% (303) | 18.17% (210) |
| deberta-small / title | 99.00% | 93.33% | 10 | 26.80% | 89.25% | 20.16% (303) | 18.17% (210) |
| deberta-small / title | 98.00% | 93.33% | 10 | 26.80% | 89.25% | 20.16% (303) | 18.17% (210) |
| deberta-small / title_body | 100.00% | 98.00% | 3 | 8.07% | 89.29% | 6.52% (98) | 6.06% (70) |
| deberta-small / title_body | 99.00% | 98.00% | 3 | 8.07% | 89.29% | 6.52% (98) | 6.06% (70) |
| deberta-small / title_body | 98.00% | 98.00% | 3 | 8.07% | 89.29% | 6.52% (98) | 6.06% (70) |
| bert-tiny / title | 100.00% | 96.67% | 5 | 10.66% | 86.49% | 9.12% (137) | 8.65% (100) |
| bert-tiny / title | 99.00% | 96.67% | 5 | 10.66% | 86.49% | 9.12% (137) | 8.65% (100) |
| bert-tiny / title | 98.00% | 96.67% | 5 | 10.66% | 86.49% | 9.12% (137) | 8.65% (100) |
| bert-tiny / title_body | 100.00% | 92.00% | 12 | 29.97% | 88.46% | 23.29% (350) | 21.28% (246) |
| bert-tiny / title_body | 99.00% | 92.00% | 12 | 29.97% | 88.46% | 23.29% (350) | 21.28% (246) |
| bert-tiny / title_body | 98.00% | 92.00% | 12 | 29.97% | 88.46% | 23.29% (350) | 21.28% (246) |

## Calibration and generalization

No temperature scaling or probabilistic confidence claim. Brier score, cross entropy and ten-bin calibration error below are on employer-out predictions; the full reliability bins are in JSON. Thresholds can vary sharply between folds because tiny validation sets and unseen-employer shift change score distributions.

| Candidate | Fold 0 / 1 / 2 thresholds | Brier | Cross entropy | ECE (10 bins) |
|---|---|---:|---:|---:|
| deberta-small / title | 0.094379 / 0.476107 / 0.230340 | 0.2443 | 0.7115 | 0.1548 |
| deberta-small / title_body | 0.100866 / 0.303739 / 0.209259 | 0.1910 | 0.5602 | 0.1131 |
| bert-tiny / title | 0.288945 / 0.356664 / 0.266506 | 0.2037 | 0.5924 | 0.1499 |
| bert-tiny / title_body | 0.132470 / 0.395031 / 0.283071 | 0.1732 | 0.5315 | 0.1560 |

### deberta-small / title: employer and novelty slices

| Employer | KEEP n | KEEP recall | False rejects | Rejection |
|---|---:|---:|---:|---:|
| aecom | 4 | 100.00% | 0 | 42.31% |
| amentum | 1 | 100.00% | 0 | 0.00% |
| boeing | 17 | 100.00% | 0 | 0.00% |
| kbr | 3 | 100.00% | 0 | 9.09% |
| leidos | 22 | 86.36% | 3 | 31.82% |
| northrop-grumman | 5 | 100.00% | 0 | 28.57% |
| nvidia | 65 | 98.46% | 1 | 1.49% |
| nxp-semiconductors | 2 | 0.00% | 2 | 100.00% |
| parsons | 12 | 83.33% | 2 | 51.19% |
| rtx | 13 | 84.62% | 2 | 31.25% |
| servicenow | 6 | 100.00% | 0 | 26.92% |

lowSimilarityBelow035: 76 rows, 28 KEEP; recall 89.29%, 3 false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.

higherSimilarity: 271 rows, 122 KEEP; recall 94.26%, 7 false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.

Representative false rejects (full list in results.json):

* **Substation Protection and Controls Engineer - (South)** (leidos), score 0.3382: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.
* **Senior FAE - Microprocessors (MPU) Home Based,  Must live in EASTERN US** (nxp-semiconductors), score 0.3608: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.
* **Kahua Application Developer** (parsons), score 0.4077: Software, systems, data/AI, automation, integration, technical security or infrastructure engineering duties.
* **Ground Penetrating Radar Specialist (Antarctica)** (leidos), score 0.3363: Conservative KEEP: radar data processing plus computer/electronic equipment troubleshooting; remote location alone is not occupational rejection.
* **Sr. SAP Business Analyst: Opportunity to Cash O2C & Aftermarket (REMOTE)** (rtx), score 0.4650: SAP functional configuration, integration, data mapping and testing.
* **Tier II and III Enterprise Systems Engineer Opportunities Supporting TSA TOMCAT** (leidos), score 0.4057: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.
* **Developer Relations Manager - Omniverse** (nvidia), score 0.2280: Software, systems, data/AI, automation, integration, technical security or infrastructure engineering duties.
* **Field Applications Engineer (FAE) - MPU and DNPU** (nxp-semiconductors), score 0.3915: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.
* **Targeting Systems Analyst - TS/SCI** (parsons), score 0.3510: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.
* **Plan to Sustain SAP Digital Coordinator (Remote)** (rtx), score 0.3428: SAP integration, functional specification, testing and deployment coordination.

Representative correctly rejected reviewed occupations:

* Senior Civil Engineer (aecom), score 0.0594.
* Senior Civil Engineer (aecom), score 0.0594.
* Senior Civil Engineer (aecom), score 0.0594.
* Supply Chain Procurement Specialist – Level 2 (northrop-grumman), score 0.0648.
* Sr Marketing Specialist, ABX (Armis) (servicenow), score 0.0649.
* Mid-level Archaeologist (aecom), score 0.0665.

Synthetic KEEP recall by fold (30 KEEP each, not independent repeated evidence): fold 0: 100.00%, 0 misses, fold 1: 86.67%, 4 misses, fold 2: 100.00%, 0 misses.

### deberta-small / title_body: employer and novelty slices

| Employer | KEEP n | KEEP recall | False rejects | Rejection |
|---|---:|---:|---:|---:|
| aecom | 4 | 75.00% | 1 | 19.23% |
| amentum | 1 | 100.00% | 0 | 50.00% |
| boeing | 17 | 100.00% | 0 | 0.00% |
| kbr | 3 | 100.00% | 0 | 0.00% |
| leidos | 22 | 100.00% | 0 | 9.09% |
| northrop-grumman | 5 | 100.00% | 0 | 7.14% |
| nvidia | 65 | 100.00% | 0 | 0.00% |
| nxp-semiconductors | 2 | 100.00% | 0 | 0.00% |
| parsons | 12 | 83.33% | 2 | 19.05% |
| rtx | 13 | 100.00% | 0 | 3.12% |
| servicenow | 6 | 100.00% | 0 | 0.00% |

lowSimilarityBelow035: 76 rows, 28 KEEP; recall 100.00%, 0 false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.

higherSimilarity: 271 rows, 122 KEEP; recall 97.54%, 3 false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.

Representative false rejects (full list in results.json):

* **Design Project Manager, Data Centers - Remote (U.S.)** (aecom), score 0.0899: Ambiguous data-center electrical/mechanical design leadership; conservative KEEP.
* **Senior Mechanical Engineer - Hybrid Washington, DC** (parsons), score 0.3023: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.
* **Cybersecurity Risk & Compliance PM** (parsons), score 0.2815: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.

Representative correctly rejected reviewed occupations:

* Principal Public Relations Representative (19792) (northrop-grumman), score 0.0808.
* Civil Engineer (aecom), score 0.0868.
* Mid-level Archaeologist (aecom), score 0.0906.
* Senior Project Manager - Environmental Remediation - Oil and Gas (aecom), score 0.0975.
* Senior Civil Engineer (aecom), score 0.0996.
* Drainage Engineer II (parsons), score 0.2519.

Synthetic KEEP recall by fold (30 KEEP each, not independent repeated evidence): fold 0: 96.67%, 1 misses, fold 1: 100.00%, 0 misses, fold 2: 100.00%, 0 misses.

### bert-tiny / title: employer and novelty slices

| Employer | KEEP n | KEEP recall | False rejects | Rejection |
|---|---:|---:|---:|---:|
| aecom | 4 | 50.00% | 2 | 50.00% |
| amentum | 1 | 100.00% | 0 | 0.00% |
| boeing | 17 | 100.00% | 0 | 0.00% |
| kbr | 3 | 100.00% | 0 | 18.18% |
| leidos | 22 | 95.45% | 1 | 6.82% |
| northrop-grumman | 5 | 80.00% | 1 | 57.14% |
| nvidia | 65 | 100.00% | 0 | 0.00% |
| nxp-semiconductors | 2 | 100.00% | 0 | 0.00% |
| parsons | 12 | 100.00% | 0 | 3.57% |
| rtx | 13 | 92.31% | 1 | 9.38% |
| servicenow | 6 | 100.00% | 0 | 19.23% |

lowSimilarityBelow035: 76 rows, 28 KEEP; recall 96.43%, 1 false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.

higherSimilarity: 271 rows, 122 KEEP; recall 96.72%, 4 false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.

Representative false rejects (full list in results.json):

* **MES Systems Analyst - PLM Process Planning (Remote)** (rtx), score 0.3528: Software, systems, data/AI, automation, integration, technical security or infrastructure engineering duties.
* **Ground Penetrating Radar Specialist (Antarctica)** (leidos), score 0.3397: Conservative KEEP: radar data processing plus computer/electronic equipment troubleshooting; remote location alone is not occupational rejection.
* **Design Project Manager, Data Centers - Remote (U.S.)** (aecom), score 0.2391: Ambiguous data-center electrical/mechanical design leadership; conservative KEEP.
* **Field Technician 4** (northrop-grumman), score 0.2840: Field Technician title hides mechanical field engineering, root-cause analysis, custom tooling design and control-system analysis; ambiguous specialist engineering stays KEEP.
* **Roadway Lighting Engineers and Designers** (aecom), score 0.2283: Conservative KEEP: roadway lighting is adjacent electrical design engineering, not sufficient grounds for cheap rejection.

Representative correctly rejected reviewed occupations:

* Capture Manager (aecom), score 0.1412.
* Civil Engineer (aecom), score 0.1468.
* Pursuit Lead, Elevate  (servicenow), score 0.1683.
* Assistant Construction Project Manager II- AECOM Hunt (aecom), score 0.1695.
* Supply Chain Procurement Specialist (northrop-grumman), score 0.1759.
* Supply Chain Procurement Specialist – Level 2 (northrop-grumman), score 0.1795.

Synthetic KEEP recall by fold (30 KEEP each, not independent repeated evidence): fold 0: 90.00%, 3 misses, fold 1: 100.00%, 0 misses, fold 2: 96.67%, 1 misses.

### bert-tiny / title_body: employer and novelty slices

| Employer | KEEP n | KEEP recall | False rejects | Rejection |
|---|---:|---:|---:|---:|
| aecom | 4 | 100.00% | 0 | 7.69% |
| amentum | 1 | 100.00% | 0 | 50.00% |
| boeing | 17 | 100.00% | 0 | 17.95% |
| kbr | 3 | 100.00% | 0 | 18.18% |
| leidos | 22 | 68.18% | 7 | 63.64% |
| northrop-grumman | 5 | 100.00% | 0 | 35.71% |
| nvidia | 65 | 100.00% | 0 | 0.00% |
| nxp-semiconductors | 2 | 100.00% | 0 | 0.00% |
| parsons | 12 | 83.33% | 2 | 45.24% |
| rtx | 13 | 76.92% | 3 | 56.25% |
| servicenow | 6 | 100.00% | 0 | 11.54% |

lowSimilarityBelow035: 76 rows, 28 KEEP; recall 82.14%, 5 false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.

higherSimilarity: 271 rows, 122 KEEP; recall 94.26%, 7 false rejects. Train-only title character-TF-IDF nearest similarity below 0.35 is a lexical novelty diagnostic, not a formal title-family split.

Representative false rejects (full list in results.json):

* **PC Support Technician** (leidos), score 0.3716: Explicit PC service desk troubleshooting for an IT program.
* **Director - Product Cybersecurity Supply Chain Risk Management** (rtx), score 0.3894: Software, systems, data/AI, automation, integration, technical security or infrastructure engineering duties.
* **Ground Penetrating Radar Specialist (Antarctica)** (leidos), score 0.2900: Conservative KEEP: radar data processing plus computer/electronic equipment troubleshooting; remote location alone is not occupational rejection.
* **Field Service Technician I** (leidos), score 0.2905: Ambiguous field systems installation/testing: conservative KEEP despite weak Office-software regex match.
* **Tier II and III Enterprise Systems Engineer Opportunities Supporting TSA TOMCAT** (leidos), score 0.3926: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.
* **Senior Transmission Line Designer** (leidos), score 0.3534: Adjacent electrical transmission design engineering; conservative KEEP.
* **Senior Engineering Manager, Structural Processing (Remote)** (rtx), score 0.3575: Manufacturing automation engineering, controls and robotics integration.
* **Cybersecurity Risk & Compliance PM** (parsons), score 0.3950: Clear technical/engineering title or adjacent technical role; KEEP under the conservative occupational boundary.
* **Principal Specialist, General Finance** (rtx), score 0.2825: Explicit SQL reporting, financial-system module/configuration maintenance, advanced systems support and troubleshooting: KEEP. Earlier dev REJECT label is too coarse.
* **Principal Program Manager - Rail and Transit** (parsons), score 0.2857: Rail equipment maintenance oversight and technical reviews; uncertain adjacent systems duties stay KEEP.

Representative correctly rejected reviewed occupations:

* Senior Project Manager - Environmental Remediation - Oil and Gas (aecom), score 0.1019.
* Talent Acquisition Business Partner (northrop-grumman), score 0.1048.
* Supply Chain Procurement Specialist – Level 2 (northrop-grumman), score 0.1070.
* Procurement Specialist (Level 3 or 4) - Category Management (northrop-grumman), score 0.1090.
* Senior Principal Tax Accountant (northrop-grumman), score 0.1097.
* Senior Civil Engineer (aecom), score 0.1099.

Synthetic KEEP recall by fold (30 KEEP each, not independent repeated evidence): fold 0: 100.00%, 0 misses, fold 1: 100.00%, 0 misses, fold 2: 100.00%, 0 misses.

## Employer names and body controls

Post-training diagnostics do not change model or threshold. Mask literal company metadata names and space-separated forms with “the employer,” preserving all other case/text. This is a limited perturbation: subsidiaries, products and employer boilerplate can remain. A separate casefold-only control diagnoses confounding in the original runner's casefold-plus-mask probe. Do not interpret masking alone as proof of no employer shortcut. Original scores replay within 1e-5.

| Candidate | Case-preserving company-mask flips / 347 | KEEP false rejects after mask | Mean absolute score change | Body-tail flips / 347 | KEEP false rejects with body tail |
|---|---:|---:|---:|---:|---:|
| deberta-small / title | 2 | 11 | 0.0014 | 0 | 10 |
| deberta-small / title_body | 6 | 3 | 0.0210 | 31 | 0 |
| bert-tiny / title | 1 | 5 | 0.0003 | 0 | 5 |
| bert-tiny / title_body | 26 | 17 | 0.0497 | 149 | 70 |

Body-tail is a deliberate distribution shift and can contain benefits boilerplate rather than duties. Its result diagnoses input sensitivity, not an alternative selected model. See the interpretation for whether title-only suffices and how body context changes errors.

## Inference performance

Clean subprocess, one selected fold-2 checkpoint per candidate, 40 deterministically selected evaluation postings, GPU batch 16 and CPU four threads. Includes fresh tokenization and one forward pass per posting; excludes load, warm-up and training. RSS is process high-water memory including tokenizer/runtime; GPU allocated and reserved exclude some CUDA driver/context memory. The full-cache throughput in predictions.json uses all employer-out cache rows, whereas this clean benchmark uses the same selected evaluation IDs across models/views. Training memory is recorded separately.

| Candidate | Parameters | GPU batched ms/job | GPU jobs/s | GPU single median / p95 ms | GPU allocated / reserved MiB | CPU median / p95 ms | CPU sequential jobs/s | Process RSS high-water MiB |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| deberta-small / title | 141,896,450 | 1.47 | 679.0 | 14.39 / 14.88 | 642.6 / 670.0 | 45.87 / 47.67 | 21.6 | 1572.5 |
| deberta-small / title_body | 141,896,450 | 21.26 | 47.0 | 24.55 / 25.87 | 1386.6 / 1488.0 | 279.24 / 357.37 | 3.4 | 1636.3 |
| bert-tiny / title | 4,386,178 | 0.30 | 3345.1 | 2.73 / 2.84 | 51.1 / 54.0 | 1.23 / 1.37 | 799.6 | 866.9 |
| bert-tiny / title_body | 4,386,178 | 2.79 | 359.0 | 4.80 / 6.96 | 81.8 / 98.0 | 5.93 / 7.71 | 160.9 | 866.2 |

No Qwen or other generative model was run; no direct generative speed ratio is claimed. The small encoders perform one bounded forward pass. BERT-Tiny is cheap enough to be plausible operationally; classification safety, rather than its runtime, blocks replacement. Rule and encoder benchmark input mixtures differ, so avoid direct speed ratios.

## Exact trained checkpoint manifest

Checkpoints were archived and SHA-verified on curiosity at `/home/codex/jsm-lab/experiments/cheap-reject-supervised-v1-20260905/<tag>/model.safetensors`. `checkpoint-manifest.json` records retained paths; raw prediction metadata preserves original temporary paths. Full weight files are not added to the repository and are not deployed. The pinned script/data/dependency protocol allows retraining; these hashes identify the actual measured models.

| Tag | Best epoch | Training seconds | Trained safetensors SHA-256 |
|---|---:|---:|---|
| deberta-small-title-fold0 | 6 | 43.5 | `4fb293c7b751b3cbe8d76a946be417d58334e2d6416839e6887bd26ef9eb4a70` |
| deberta-small-title-fold1 | 2 | 25.2 | `d3c78cb0e5b81c18d7a9bf4b1dee5d98e0bed939bba9db1601f619e1e46102ca` |
| deberta-small-title-fold2 | 2 | 35.0 | `7ea5b5bd930c24a1c4bf0f6e4877482f938cd0c91a59ee9b59aa1ed19d20873b` |
| deberta-small-title_body-fold0 | 3 | 313.9 | `23d8c9cd430abbdbd07dafe1a041d798bf1652277694a5ac9e1bc85a217a22c5` |
| deberta-small-title_body-fold1 | 2 | 193.2 | `07fdc404f275ea49ed60a34ddb034bc16d527b984f06ab9c3001167ce92f5b51` |
| deberta-small-title_body-fold2 | 7 | 280.7 | `a8dc8250744a9fc8100273ae6a9b67316e4ca94c5b5fd8d3d21a08f03ab2b7ae` |
| bert-tiny-title-fold0 | 12 | 19.9 | `eacb621d0bfc1daad2aa4d1c00627d55978b9d0ece2d3a1fba4fb48728316278` |
| bert-tiny-title-fold1 | 8 | 12.3 | `fd4b8977af4dfb032736b636ecbb2d7867f577c5d1d4195b5711504d7ce14e7c` |
| bert-tiny-title-fold2 | 12 | 18.1 | `8543d3c8dbb478727884676f8465d2593c02b8bada210e0353aba8f1939ee5f9` |
| bert-tiny-title_body-fold0 | 11 | 18.9 | `2608bfc8af9ad1fbba5fe295e8856608eaec3e85efe7d09fd20090fdd24d26ed` |
| bert-tiny-title_body-fold1 | 7 | 11.3 | `209e2516a8e67c8781e87e22db78384d7759b4a51c40558880ab3b3f1a94d2a9` |
| bert-tiny-title_body-fold2 | 11 | 17.7 | `a351a6d1f7c30cc7e019194adfcea157b8f91b9aae69eb9f5ae6e84fc7558f11` |

## Descriptive upper bound at zero, one and three false rejects

These points use OUTER evaluation labels to describe the ranking's best global threshold at the requested recall. They are not validation-selected recommendations, independently tested operating points, or evidence that a deployable calibrated threshold has been found. No training is repeated and no production holdout is involved.

| Candidate | Target recall | Retrospective threshold | Actual recall | False rejects | Evaluation rejection | Full cache rejection |
|---|---:|---:|---:|---:|---:|---:|
| deberta-small / title | 100.00% | 0.19478755 | 100.00% | 0 | 9.80% | 7.25% |
| deberta-small / title | 99.00% | 0.22798400 | 99.33% | 1 | 10.09% | 7.98% |
| deberta-small / title | 98.00% | 0.24025470 | 98.00% | 3 | 10.66% | 8.98% |
| deberta-small / title_body | 100.00% | 0.08994531 | 100.00% | 0 | 0.58% | 0.27% |
| deberta-small / title_body | 99.00% | 0.12887961 | 99.33% | 1 | 5.19% | 3.86% |
| deberta-small / title_body | 98.00% | 0.19345538 | 98.00% | 3 | 9.80% | 6.52% |
| bert-tiny / title | 100.00% | 0.22830288 | 100.00% | 0 | 4.03% | 3.59% |
| bert-tiny / title | 99.00% | 0.23906547 | 99.33% | 1 | 5.48% | 4.13% |
| bert-tiny / title | 98.00% | 0.30020046 | 98.00% | 3 | 10.66% | 8.78% |
| bert-tiny / title_body | 100.00% | 0.15072824 | 100.00% | 0 | 4.32% | 3.86% |
| bert-tiny / title_body | 99.00% | 0.18169741 | 99.33% | 1 | 6.05% | 4.79% |
| bert-tiny / title_body | 98.00% | 0.28251386 | 98.00% | 3 | 13.26% | 9.51% |

## Additional per-fold calibration oracle

Global thresholds above pool scores from three differently calibrated heads. To avoid mistaking calibration failure for ranking failure, a separate post-hoc oracle independently picks each fold's threshold using evaluation labels and allocates the total permitted false rejects across folds to maximize cache rejection. This is a generous upper bound for these fold score rankings. It uses evaluation labels AND cache counts, and is emphatically not deployable validation evidence.

| Candidate | Target recall | Actual false rejects | Evaluation rejection | Cache rejection |
|---|---:|---:|---:|---:|
| deberta-small / title | 100.00% | 0 | 11.53% | 8.92% |
| deberta-small / title | 99.00% | 1 | 13.26% | 10.84% |
| deberta-small / title | 98.00% | 3 | 15.85% | 13.57% |
| deberta-small / title_body | 100.00% | 0 | 8.36% | 6.05% |
| deberta-small / title_body | 99.00% | 1 | 12.97% | 9.65% |
| deberta-small / title_body | 98.00% | 3 | 18.16% | 13.04% |
| bert-tiny / title | 100.00% | 0 | 6.05% | 6.92% |
| bert-tiny / title | 99.00% | 1 | 7.20% | 7.72% |
| bert-tiny / title | 98.00% | 3 | 14.70% | 12.44% |
| bert-tiny / title_body | 100.00% | 0 | 8.93% | 7.65% |
| bert-tiny / title_body | 99.00% | 1 | 10.66% | 8.58% |
| bert-tiny / title_body | 98.00% | 3 | 14.99% | 11.11% |

The maximum at 98% is DeBERTa title-only, 204/1503 (13.57%), with three false rejects, versus rules 208/1503 (13.84%) with no known reviewed false rejects. A four-posting rejection difference is not meaningful evidence of superiority either way; the calibration failures and KEEP losses are the material issue. At >=99%, the best oracle rejects only 163/1503 (10.84%), with one false reject.

## Complete fixed threshold sweep

All metrics below use only employer-out evaluation predictions. This curve is descriptive, not a second tuning opportunity. Precision is for the purposive reviewed reference, not the unlabeled cache. Timings depend on model/view, not the trivial threshold operation.

| Candidate | KEEP-score threshold | KEEP recall | False rejects / 150 | Evaluation rejection | Reject precision | Full cache rejection | Unreviewed cache rejection |
|---|---:|---:|---:|---:|---:|---:|---:|
| deberta-small / title | 0 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title | 0.001 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title | 0.005 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title | 0.01 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title | 0.025 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title | 0.05 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title | 0.1 | 100.00% | 0 | 8.07% | 100.00% | 5.12% | 4.24% |
| deberta-small / title | 0.15 | 100.00% | 0 | 9.80% | 100.00% | 6.85% | 5.97% |
| deberta-small / title | 0.2 | 99.33% | 1 | 10.09% | 97.14% | 7.32% | 6.49% |
| deberta-small / title | 0.3 | 96.00% | 6 | 13.26% | 86.96% | 10.98% | 10.29% |
| deberta-small / title | 0.4 | 89.33% | 16 | 24.50% | 81.18% | 24.02% | 23.88% |
| deberta-small / title | 0.5 | 42.00% | 87 | 60.81% | 58.77% | 59.75% | 59.43% |
| deberta-small / title | 0.6 | 25.33% | 112 | 77.52% | 58.36% | 76.65% | 76.38% |
| deberta-small / title | 0.7 | 19.33% | 121 | 84.73% | 58.84% | 84.90% | 84.95% |
| deberta-small / title | 0.8 | 13.33% | 130 | 90.49% | 58.60% | 90.95% | 91.09% |
| deberta-small / title | 0.9 | 9.33% | 136 | 92.22% | 57.50% | 93.28% | 93.60% |
| deberta-small / title | 0.95 | 7.33% | 139 | 94.52% | 57.62% | 94.81% | 94.90% |
| deberta-small / title | 0.975 | 6.00% | 141 | 95.97% | 57.66% | 96.81% | 97.06% |
| deberta-small / title | 0.99 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title | 0.995 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title | 0.999 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title | 1.0 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title_body | 0 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title_body | 0.001 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title_body | 0.005 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title_body | 0.01 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title_body | 0.025 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title_body | 0.05 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| deberta-small / title_body | 0.1 | 99.33% | 1 | 1.73% | 83.33% | 1.33% | 1.21% |
| deberta-small / title_body | 0.15 | 98.67% | 2 | 6.92% | 91.67% | 4.79% | 4.15% |
| deberta-small / title_body | 0.2 | 97.33% | 4 | 10.09% | 88.57% | 6.79% | 5.80% |
| deberta-small / title_body | 0.3 | 95.33% | 7 | 20.17% | 90.00% | 14.30% | 12.54% |
| deberta-small / title_body | 0.4 | 88.00% | 18 | 34.87% | 85.12% | 28.08% | 26.04% |
| deberta-small / title_body | 0.5 | 76.00% | 36 | 46.97% | 77.91% | 37.52% | 34.69% |
| deberta-small / title_body | 0.6 | 67.33% | 49 | 56.77% | 75.13% | 47.11% | 44.20% |
| deberta-small / title_body | 0.7 | 58.00% | 63 | 66.28% | 72.61% | 57.15% | 54.41% |
| deberta-small / title_body | 0.8 | 47.33% | 79 | 76.37% | 70.19% | 66.93% | 64.10% |
| deberta-small / title_body | 0.9 | 2.67% | 146 | 98.56% | 57.31% | 98.34% | 98.27% |
| deberta-small / title_body | 0.95 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title_body | 0.975 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title_body | 0.99 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title_body | 0.995 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title_body | 0.999 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| deberta-small / title_body | 1.0 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title | 0 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title | 0.001 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title | 0.005 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title | 0.01 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title | 0.025 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title | 0.05 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title | 0.1 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title | 0.15 | 100.00% | 0 | 0.58% | 100.00% | 0.27% | 0.17% |
| bert-tiny / title | 0.2 | 100.00% | 0 | 2.31% | 100.00% | 2.06% | 1.99% |
| bert-tiny / title | 0.3 | 98.00% | 3 | 10.66% | 91.89% | 8.78% | 8.22% |
| bert-tiny / title | 0.4 | 89.33% | 16 | 22.19% | 79.22% | 20.69% | 20.24% |
| bert-tiny / title | 0.5 | 73.33% | 40 | 44.09% | 73.86% | 41.92% | 41.26% |
| bert-tiny / title | 0.6 | 60.67% | 59 | 65.71% | 74.12% | 63.21% | 62.46% |
| bert-tiny / title | 0.7 | 37.33% | 94 | 82.71% | 67.25% | 80.37% | 79.67% |
| bert-tiny / title | 0.8 | 4.00% | 144 | 97.98% | 57.65% | 97.34% | 97.15% |
| bert-tiny / title | 0.9 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title | 0.95 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title | 0.975 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title | 0.99 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title | 0.995 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title | 0.999 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title | 1.0 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title_body | 0 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title_body | 0.001 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title_body | 0.005 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title_body | 0.01 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title_body | 0.025 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title_body | 0.05 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title_body | 0.1 | 100.00% | 0 | 0.00% | — | 0.00% | 0.00% |
| bert-tiny / title_body | 0.15 | 100.00% | 0 | 4.32% | 100.00% | 3.79% | 3.63% |
| bert-tiny / title_body | 0.2 | 98.67% | 2 | 7.20% | 92.00% | 5.46% | 4.93% |
| bert-tiny / title_body | 0.3 | 95.33% | 7 | 17.00% | 88.14% | 12.04% | 10.55% |
| bert-tiny / title_body | 0.4 | 87.33% | 19 | 39.77% | 86.23% | 31.60% | 29.15% |
| bert-tiny / title_body | 0.5 | 81.33% | 28 | 53.60% | 84.95% | 44.24% | 41.44% |
| bert-tiny / title_body | 0.6 | 67.33% | 49 | 63.40% | 77.73% | 53.56% | 50.61% |
| bert-tiny / title_body | 0.7 | 50.67% | 74 | 74.06% | 71.21% | 65.27% | 62.63% |
| bert-tiny / title_body | 0.8 | 4.67% | 143 | 95.97% | 57.06% | 93.95% | 93.34% |
| bert-tiny / title_body | 0.9 | 0.00% | 150 | 100.00% | 56.77% | 99.67% | 99.57% |
| bert-tiny / title_body | 0.95 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title_body | 0.975 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title_body | 0.99 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title_body | 0.995 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title_body | 0.999 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |
| bert-tiny / title_body | 1.0 | 0.00% | 150 | 100.00% | 56.77% | 100.00% | 100.00% |

## Additional human labels and confidence

For a fixed classifier and threshold, under an ideal independent sample with zero KEEP misses, the one-sided 95% binomial lower recall bound is `0.05**(1/n)`. Demonstrating at least 99% requires 299 independent KEEP examples with zero misses; 98% requires 149. With 150 KEEP and zero misses the bound is only 98.02%, before accounting for employer clustering, provisional labels, repeated model comparisons and selection bias. Actual observed misses require more examples or a better operating point.

A practical next labeling budget, if the results warrant it, is 1,000–2,000 additional human-adjudicated development examples across many more employers and adjacent technical/negative occupation families, plus a separate frozen evaluation set containing at least 300 confirmed KEEP and several hundred REJECT. Those counts are a planning recommendation, not a measured learning curve or guarantee. Maintain train/validation/test employer and title-family boundaries, duplicate checks, adjudication reasons and random prospective cache sampling. Use active sampling for training coverage only; do not use its raw rejection shares as population estimates. Do not reuse the blinded production holdout for this development cycle.

## Files and validation

Added offline files reside under `Tests/cheap_reject_evaluation/supervised_v1/`, plus this report. Original rule code, labels and application files remain unchanged. `README.md` gives replay commands; `validation-results.md` records tests and checks; `interpretation.md` provides the recommendation. No checkpoint is deployed, no production discard behavior is enabled, and no commit is created.

