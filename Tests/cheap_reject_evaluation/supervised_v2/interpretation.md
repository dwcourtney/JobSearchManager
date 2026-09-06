# Interpretation and recommendation

**Decision: KEEP REGEX.** Keep the existing shadow/audit baseline; this experiment does not justify a production discard classifier. There is useful ranking signal, but no validation-selected point simultaneously matches the old-cache 13.84% rejection rate and maintains even 98% KEEP recall in both the description-present and title-only evaluation partitions.

## Main comparison

The table uses the predeclared most conservative calibration target (zero validation KEEP misses). Actual outer recall is shown, not the nominal validation target. All numbers use provisional Codex labels. B and C are identical because every binary label is high or medium confidence.

| Candidate | Combined recall | Described recall | Title-only recall | False rejects | Binary evaluation rejection | Reject precision | Old cache rejection |
|---|---:|---:|---:|---:|---:|---:|---:|
| electrical-safe | 99.58% | 99.02% | 100.00% | 9 | 1.57% | 78.57% | 13.84% |
| bert-tiny / title / all | 98.63% | 97.92% | 99.17% | 29 | 6.14% | 82.32% | 9.31% |
| bert-tiny / title / description | 99.06% | 98.36% | 99.59% | 20 | 4.53% | 83.47% | 7.19% |
| bert-tiny / title / weighted | 98.77% | 97.81% | 99.50% | 26 | 5.70% | 82.89% | 10.45% |
| bert-tiny / title_body / all | 98.35% | 96.39% | 99.83% | 35 | 7.42% | 82.32% | 14.50% |
| bert-tiny / title_body / description | 98.02% | 95.40% | 100.00% | 42 | 8.47% | 81.42% | 15.50% |
| bert-tiny / title_body / weighted | 98.16% | 96.50% | 99.42% | 39 | 7.53% | 80.60% | 13.91% |
| deberta-small / title / all | 98.44% | 98.58% | 98.34% | 33 | 5.99% | 79.38% | 5.66% |
| deberta-small / title / description | 97.55% | 98.80% | 96.60% | 52 | 4.83% | 59.69% | 2.26% |
| deberta-small / title / weighted | 98.11% | 98.14% | 98.09% | 40 | 5.88% | 74.52% | 4.39% |
| deberta-small / title_body / all | 99.39% | 99.12% | 99.59% | 13 | 4.68% | 89.60% | 8.25% |
| deberta-small / title_body / description | 99.62% | 99.12% | 100.00% | 8 | 1.46% | 79.49% | 4.13% |
| deberta-small / title_body / weighted | 99.25% | 98.69% | 99.67% | 16 | 4.65% | 87.10% | 6.92% |

## Operating points and the high-recall constraint

At >=99% observed combined recall, DeBERTa / title+body / all has the largest old-cache rejection among the validation-selected points: 8.25%, with 13 false rejects, 99.39% combined recall, 99.12% described recall, and 99.59% title-only recall. Its fold thresholds are 0.05054016, 0.28285089 and 0.02489462. They were selected on separate validation employers and are not a single deployable threshold.

At >=98% observed combined recall, BERT-Tiny / title+body / description-only training rejects 15.50% of the old cache, but described KEEP recall is only 95.40% (42 false rejects), while title-only recall is 100%. BERT-Tiny / title+body / all reaches 14.50% cache rejection and 98.35% combined recall, but 33 of its 35 false rejects are described jobs: described recall is 96.39%, title-only recall 99.83%. Its high-confidence KEEP recall is 97.57%. The favorable aggregate is therefore not enough to declare the stage safe.

No validated production threshold is recommended. The nominal 99% and 98% validation targets often fail to preserve those recall levels on new employers. report.md and threshold-sweep.csv show all operating points, not only favorable ones. Results.json also reports retrospective single-global-threshold frontiers over cross-fitted scores. For example, DeBERTa all/title+body can retrospectively reach about 19.83% cache rejection at 98% evaluation recall, but the threshold uses evaluation labels and cannot count as independently validated performance. This demonstrates ranking potential, not readiness.

## What improved, and what still fails

At the conservative point, both all-data body models preserve all 439 software-category KEEP postings. Both also preserve the six fixed legacy probes: Kahua Application Developer, Tier II/III Enterprise Systems Engineer, PC Support Technician, Cybersecurity Risk & Compliance PM, the controls engineer, and the mechanical engineer. This is improvement on those particular previously failing cases, not a matched-protocol causal proof that corpus size alone fixed the problem.

The wider technical boundary still fails. BERT-Tiny all/body rejects Principal Enterprise-Wide Applications Analyst, Sr EWM Configuration Lead, SATCOM IP Network Support Analyst, NOC / SOC Shift Lead, and Project Manager – Application Development. Its infrastructure-support recall is 98.15% (7 misses), cybersecurity 98.86% (2), engineering 98.82% (9), and technical leadership 93.89% (16). DeBERTa all/body retains all infrastructure-support cases but misses COMSEC network-key support, digital-forensics advising, specialized test-equipment integration and technical partner architecture. Some management/quality/partner cases warrant human review; the frozen labels were not changed to excuse model errors.

Examples of correctly rejected unrelated work include procurement agents, benefits management, human-resources internships, assembly/lamination operators, and plant maintenance. These decisions do not require adding occupational regex families. The unresolved issue is conservative generalization, not an inability to learn any negative occupations.

## Data quality, inputs and generalization

- Description-only training is not sufficient for a replacement: the safer DeBERTa point rejects only 4.13% of the old cache. BERT-Tiny description-only body training gets volume by losing too many described KEEP jobs.
- Weak title-only examples are not uniformly harmful. For DeBERTa body inputs, adding them increases conservative cache rejection from 4.13% to 8.25%, while false rejects rise from 8 to 13. However, evaluating on weak title-only labels makes BERT-Tiny body aggregate recall look safer than the described subset.
- The prescribed weighting does not improve the main body frontier: DeBERTa weighted/body has 16 false rejects and 6.92% cache rejection versus 13 and 8.25% for all/body. BERT-Tiny weighted/body has 39 false rejects and 13.91% cache rejection versus 35 and 14.50% for all/body.
- Body text helps DeBERTa relative to title-only at conservative thresholds. It raises BERT-Tiny rejection volume but worsens described recall. This is an input/capacity interaction, not evidence that body text is intrinsically harmful.
- Every evaluated employer is absent from its model fitting and validation. Corpus lexical title-family/duplicate components are also disjoint, but they are not a guarantee of unseen broad occupational families. There are only 11 employers, strong employer imbalance, and one calibration fold has just 42 KEEP labels.
- 80 binary rows remain in the pending human-review queue. Description-present and high-confidence do not mean human verified. The existing 281-case queue remains unresolved.

The 256-token body-prefix policy truncates 1,158/1,159 described binary inputs. All recorded adjudication evidence is outside the prefix in 322 DeBERTa and 344 BERT-Tiny cases (27.78% and 29.68%). Evidence overlap does not prove the remaining text sufficient or insufficient. Literal employer masking changes 10 DeBERTa and 18 BERT-Tiny decisions at the conservative all/body points; it increases false rejects from 13 to 19 and 35 to 42 respectively. Mean absolute score changes are about 0.010 and 0.0065. Employer names are not the sole explanation, but this does not rule out broader boilerplate/product/style reliance.

Replacing the body beginning with its end without retraining drops described recall to 87.09% for DeBERTa and 82.17% for BERT-Tiny. This is a sensitivity diagnostic under input-distribution shift, not a tested improvement or proof of a particular cause. A new input design would need a new frozen experiment.

At conservative all/body thresholds, DeBERTa rejects 61/529 AMBIGUOUS postings and BERT-Tiny rejects 59/529. These are unverified risks, not confirmed safe workload savings. The old-cache rates likewise measure decisions, not human-validated safe rejection. The new corpus excludes earlier evaluation examples and has many missing bodies; its rule rejection rate of 1.57% cannot be treated as interchangeable with the old-cache 13.84%. The rules themselves now have nine disagreements with the broader provisional KEEP labels.

On the 1,156 previously unaudited old-cache rows, conservative DeBERTa all/body rejects 7.44%, BERT-Tiny all/body 13.32%, and the rules 3.98%. This shows useful rejection potential outside the old rule-development references. These rows lack reference labels, so it cannot establish safe workload reduction; the full-cache and same-labeled-subset comparisons remain separate.

## Inference cost

Measured on curiosity, GTX 1070 and i7-4770K, tokenization included. Separate fresh processes remove training optimizer state. The body benchmark uses 40 fold-2 evaluation rows, mostly described; full cross-fit stream timings and every candidate are in report.md.

| Method | Batched ms/posting | Throughput/s | GPU allocated MiB | CPU single-post median |
|---|---:|---:|---:|---:|
| electrical-safe (CPU) | 0.091 | 10,949 | — | — |
| BERT-Tiny title/body all (GPU) | 2.782 | 359.4 | 70.8 | 5.04 ms |
| DeBERTa title/body all (GPU) | 13.385 | 74.7 | 1,030.1 | 152.41 ms |

BERT-Tiny is computationally inexpensive, though still slower than rules. No Qwen benchmark or generative inference was run, so no measured Qwen speedup ratio is claimed. The decision blocker is recall and evidence quality, not merely inference speed.

## What would justify another experiment

Review the pending human cases and collect independent, employer-diverse technical KEEP evaluation labels, preserving described versus title-only strata. Align a deterministic input policy with substantive duties before fitting again. Do not simply add more weak title labels and treat aggregate recall as evidence of safety. More labels, input representation, calibration coverage and task ambiguity are all plausible limitations; this single-seed, two-model experiment does not isolate a capacity ceiling or estimate a learning curve. Do not use the blinded production holdout to tune these choices.

No production behavior, rule changes, deployments or commits were made. Stop here.
