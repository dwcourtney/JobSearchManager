# CI/CD improvement round: REJECT

Round `2026-09-08-round-001` targets only `technical.cicd`. These are offline review artifacts. The candidate is not production authority and must not be deployed.

| Reference | Rules | TP / FP / FN / TN | Precision | Recall | F1 | AP |
|---|---|---|---:|---:|---:|---:|
| Discovery, 500 | Baseline | 37 / 0 / 9 / 454 | 100.00% | 80.43% | 89.16% | 82.23% |
| Discovery, 500 | Candidate | 46 / 1 / 0 / 453 | 97.87% | 100.00% | 98.92% | 98.98% |
| Fresh validation, 374 | Baseline | 25 / 0 / 5 / 344 | 100.00% | 83.33% | 90.91% | 84.67% |
| Fresh validation, 374 | Candidate | 28 / 1 / 2 / 343 | 96.55% | 93.33% | 94.92% | 92.26% |

Validation F1 gained 4.01 percentage points, below the frozen 5-point threshold. Precision lost 3.45 points, exceeding the frozen 3-point limit. The candidate also broadens “automated deployment” into physical infrastructure/model optimization contexts. It is rejected despite improved recall and AP. No post-validation tuning occurred.

The 874 discovery/validation postings were sampled without ID or normalized-description overlap with each other or the supplied global-500 exposure. A later historical audit found 61 discovery and 40 validation descriptions in the older 200-posting holdout, which was read only after candidate sealing. The identity-filtered 334-record validation sensitivity check excludes those 40: baseline/candidate F1 is 92.00%/96.30%, precision 100.00%/96.30%, AP 86.38%/94.68%. It also fails the frozen F1-gain and precision-loss gates. The original results and rejection remain immutable; this subset is not a new labeling run. Future sampling should supply this older holdout as known exposure too.

Discovery had one A/B disagreement resolved by a third blinded pass; validation had none. References are AI-generated, not human ground truth. Requested model: `gpt-6-astra`, Codex CLI 0.153.4; backend model revision is not exposed. One malformed input-identity response was rejected and retained before retry.

All 84 other concepts retain exact presence/evidence/accepted-rule parity across all replay datasets. Current-cache replay changes 21 CI/CD decisions. The actual Jobs caller preserves scores in 27,420 comparisons with CI/CD neutral. Full per-posting changes, compact metrics, raw PR points, candidate rationale and frozen acceptance decision are included here. Unlabeled historical cases remain unscored.

Full public-posting samples, raw labeling sessions, references, baseline/candidate predictions, reproducibility sources and provenance are preserved outside Git on curiosity:
`/home/codex/jsm-research/concept-improvement/technical.cicd/2026-09-08-round-001.tar.gz`.

These compact manifests are for review. Future fresh sampling must use the complete external round history, not this compact directory. This round's discovery and validation are permanently exposed history and must never become fresh validation again.

The reusable command and manual labeling/import instructions are in `tools/concept-evaluation/CONCEPT-IMPROVEMENT.md`. Production rules, taxonomy, engine, evaluation metric/score policy, Job Fit and existing UI work remain unchanged by this candidate experiment.
