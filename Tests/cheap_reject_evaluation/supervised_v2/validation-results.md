# Validation results

Decision: KEEP REGEX. Offline experiment complete; no deployment, production changes, rule changes, commits or pushes.

## Executed checks

- PASS: all 36 predetermined GPU fits completed (216 epochs total), with recorded best-validation checkpoints and hashes.
- PASS: all 12 fresh-process CPU/GPU benchmarks, binary label mapping, title preservation/overflow fail-open and input-only encoding contracts.
- PASS: unchanged electrical-safe source hashes and all 4,701 Linux/Windows decisions agree; five-pass same-host timing completed.
- PASS: all 36 checkpoints archived outside /tmp and SHA-256 verified. See checkpoint-manifest.json.
- PASS: 6 new experiment tests with `python -B -X utf8 -m unittest discover -s supervised-v2-work -p 'test_*.py' -v`.
- PASS: 13 corpus preparation/adjudication tests and 23 existing cheap-reject evaluation/rule tests (42 Python tests total, no skips in the final new-experiment run).
- PASS: exact 3,198 input coverage and 2,669 binary evaluation rows, each evaluated once per candidate; AMBIGUOUS excluded from fitting/calibration/binary evaluation.
- PASS: employer, lexical family/component, known duplicate, exact-title and identical nonempty-body separation across corpus partitions; every cache score assigned to its employer-out model.
- PASS: dataset/split/manifest hashes, correct best-validation epoch, unchanged training script/protocol hashes across all fits, zero title overflows, finite bounded scores, exact prediction coverage and preserved checkpoint hashes.
- PASS: deterministic input/split preparation replay; unchanged baseline decisions (runtime measurements naturally differ).
- PASS: byte-identical metric, report and threshold CSV replay from frozen prediction files; Python syntax checks.
- PASS: the reported decision constraint is checked directly: no validation-selected point matches historical cache rejection while reaching 98% KEEP recall in both described and title-only partitions.
- PASS: all six experiment tests also pass from the repository location; all 81 transferred files match staging byte-for-byte; git diff --check and explicit new-file whitespace checks pass. Local main and origin/main tracking refs match (0 ahead / 0 behind).

No .NET, JavaScript application, Docker/container or deployment checks were run: only isolated offline experiment artifacts are added. The existing corpus tests verify exclusion invariants without exposing production holdout text or labels. No Qwen or other generative model was used. Generic tokenizer warnings occur while measuring full text or deliberately testing overflow; model inputs are bounded to 256 tokens and never receive those overlong sequences.

## Evidence limits

Machine-label agreement is not human accuracy. Employer/family clustering reduces effective independence. The 1,886 missing-body records include 376 AMBIGUOUS exclusions; binary title-only evaluation is weak evidence. Eighty binary labels still belong to the pending human-review queue. Input evidence alignment and sensitivity diagnostics are in input-audit.json and results.json. Retrospective frontiers are explicitly evaluation-selected and are not deployable thresholds.

## Exact files added

Only `Tests/cheap_reject_evaluation/supervised_v2/` is added by this training step. No existing repository files are changed. The complete list follows; model weight files remain on curiosity, outside the source checkout.

- `README.md`
- `archive.py`
- `artifact-manifest.json`
- `audit_inputs.py`
- `baseline.json`
- `benchmark.py`
- `benchmark_rules.py`
- `build_report.py`
- `cache.jsonl`
- `checkpoint-manifest.json`
- `completion.log`
- `corpus.jsonl`
- `finish.py`
- `finish_after_training.py`
- `hardware.json`
- `input-audit.json`
- `interpretation.md`
- `output/bert-tiny-title-all-fold0.json`
- `output/bert-tiny-title-all-fold1.json`
- `output/bert-tiny-title-all-fold2-benchmark.json`
- `output/bert-tiny-title-all-fold2.json`
- `output/bert-tiny-title-description-fold0.json`
- `output/bert-tiny-title-description-fold1.json`
- `output/bert-tiny-title-description-fold2-benchmark.json`
- `output/bert-tiny-title-description-fold2.json`
- `output/bert-tiny-title-weighted-fold0.json`
- `output/bert-tiny-title-weighted-fold1.json`
- `output/bert-tiny-title-weighted-fold2-benchmark.json`
- `output/bert-tiny-title-weighted-fold2.json`
- `output/bert-tiny-title_body-all-fold0.json`
- `output/bert-tiny-title_body-all-fold1.json`
- `output/bert-tiny-title_body-all-fold2-benchmark.json`
- `output/bert-tiny-title_body-all-fold2.json`
- `output/bert-tiny-title_body-description-fold0.json`
- `output/bert-tiny-title_body-description-fold1.json`
- `output/bert-tiny-title_body-description-fold2-benchmark.json`
- `output/bert-tiny-title_body-description-fold2.json`
- `output/bert-tiny-title_body-weighted-fold0.json`
- `output/bert-tiny-title_body-weighted-fold1.json`
- `output/bert-tiny-title_body-weighted-fold2-benchmark.json`
- `output/bert-tiny-title_body-weighted-fold2.json`
- `output/deberta-small-title-all-fold0.json`
- `output/deberta-small-title-all-fold1.json`
- `output/deberta-small-title-all-fold2-benchmark.json`
- `output/deberta-small-title-all-fold2.json`
- `output/deberta-small-title-description-fold0.json`
- `output/deberta-small-title-description-fold1.json`
- `output/deberta-small-title-description-fold2-benchmark.json`
- `output/deberta-small-title-description-fold2.json`
- `output/deberta-small-title-weighted-fold0.json`
- `output/deberta-small-title-weighted-fold1.json`
- `output/deberta-small-title-weighted-fold2-benchmark.json`
- `output/deberta-small-title-weighted-fold2.json`
- `output/deberta-small-title_body-all-fold0.json`
- `output/deberta-small-title_body-all-fold1.json`
- `output/deberta-small-title_body-all-fold2-benchmark.json`
- `output/deberta-small-title_body-all-fold2.json`
- `output/deberta-small-title_body-description-fold0.json`
- `output/deberta-small-title_body-description-fold1.json`
- `output/deberta-small-title_body-description-fold2-benchmark.json`
- `output/deberta-small-title_body-description-fold2.json`
- `output/deberta-small-title_body-weighted-fold0.json`
- `output/deberta-small-title_body-weighted-fold1.json`
- `output/deberta-small-title_body-weighted-fold2-benchmark.json`
- `output/deberta-small-title_body-weighted-fold2.json`
- `output/rules-benchmark.json`
- `prepare.py`
- `protocol.json`
- `purge-audit.json`
- `report.md`
- `requirements-lock.txt`
- `results.json`
- `score.py`
- `source-manifest.json`
- `splits.json`
- `test_experiment.py`
- `threshold-sweep.csv`
- `train.py`
- `training-distribution.json`
- `training.log`
- `validation-results.md`
