# Validation results — supervised cheap reject v1

Completed on 2026-09-05. Training and all encoder inference ran on curiosity; Windows was used for source preparation and deterministic result analysis.

* Twelve fixed training runs completed successfully. Each selected epoch is the minimum validation cross-entropy checkpoint from the predeclared schedule. No evaluation-label-based retraining or hyperparameter changes.
* Eight new offline unit tests PASS: threshold equality/recall boundary, known label correction, original and corrected dataset hashes, nested split isolation, complete prediction provenance, all 1,503 cache postings excluded from their model's training/validation employers, validation-only operating thresholds, and retained checkpoint manifest.
* Existing cheap-reject evaluation tests: 23/23 PASS.
* `scripts/validate-source.ps1`: PASS, including JavaScript syntax/UI and repository architecture checks.
* Frozen-score replay on Windows and curiosity: all candidate metrics, examples, calibration, slices, thresholds and choices match exactly. Host identity and newly measured rule CPU timing are excluded from equality.
* All original diagnostic predictions replay within 1e-5 from saved checkpoints in separate processes. Each diagnostic run checks the binary REJECT/KEEP label mapping, complete-title bounded input encoding, and overlong-title fail-open. All twelve runs completed.
* Every actual input encoded by training, prediction and diagnostic scripts is asserted within 384 tokens with full title retained. No real title overflows occurred. The full-body tokenizer warning concerns pre-truncation text, not an oversized model input.
* All twelve measured checkpoints were copied to `/home/codex/jsm-lab/experiments/cheap-reject-supervised-v1-20260905` and SHA-256-verified against the measured weights. `checkpoint-manifest.json` records their retained paths.
* Python syntax, new-text trailing whitespace and `git diff --check`: PASS. Tracked diff is empty. Local `main` and the recorded `origin/main` ref have zero divergence.

No .NET runtime tests, container builds, live Trivy scans or GitHub CodeQL runs were performed for this offline Python/data/report-only change. The source script's security/CodeQL checks validate repository integration, not live scans. No production application/container dependencies were modified.

No commits, push, production activation, new RegEx rules, generative inference/labeling, production-holdout tuning or second-stage work.

## Exact additions

Under `Tests/cheap_reject_evaluation/supervised_v1/`:

* `README.md`
* `interpretation.md`
* `validation-results.md`
* `protocol.json`
* `requirements-lock.txt`
* `corrected.jsonl`
* `splits.json`
* `prepare.py`
* `train.py`
* `score.py`
* `diagnose.py`
* `finish_on_host.py`
* `archive_checkpoints.py`
* `analyze_diagnostics.py`
* `calibration_frontier.py`
* `build_report.py`
* `test_experiment.py`
* `predictions.json`
* `results.json`
* `diagnostics.json`
* `diagnostic-results.json`
* `calibration-frontier.json`
* `checkpoint-manifest.json`
* `training.log`
* `diagnostics.log`

Also added `docs/supervised-cheap-reject-experiment.md`. All earlier investigation files remain unchanged. Full model weights are archived on curiosity, not added to the repository.
