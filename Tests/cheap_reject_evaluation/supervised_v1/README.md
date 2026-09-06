# Supervised compact cheap-reject experiment v1

Offline research only. Nothing is imported by production JSM. No new RegEx rules, generative inference/labeling, production holdout tuning, or commits. See `docs/supervised-cheap-reject-experiment.md` for results and limitations.

## Frozen data and protocol

`corrected.jsonl` merges three existing provisional editorial reference files with audited labels taking precedence. `splits.json` records their exact SHA-256 values, the one known correction, every partition ID, and employer assignments. Original annotations remain unchanged. Every real reviewed row is evaluated exactly once by a model trained and validated on other employers. Identical normalized titles are purged across partitions; this is not a broad occupational-family-disjoint evaluation.

`protocol.json` was fixed before training. Four candidates share the same three nested employer splits: DeBERTa-v3-small and two-layer BERT, each title-only and title + body. DeBERTa starts from the previously tested NLI checkpoint's encoder/pooler with a fresh two-class head; BERT starts from Google's pretrained tiny encoder with a fresh head. All parameters are fine-tuned. Immutable upstream revisions and resulting weight hashes are recorded. Checkpoint selection uses validation cross entropy only. Operating thresholds use validation KEEP scores only. Evaluation labels are read by the separate scoring script after all training completes.

The `optimisticEvaluationFrontier` results are clearly separate post-hoc upper bounds on frozen outer-fold scores, not selected or independently validated deployment thresholds. No refit on all labels is performed, so all reported cache scores exclude the posting's employer from both training and validation. The unreviewed-cache subset additionally excludes all 347 reviewed records.

## Reproduce on curiosity

Use Linux Python 3.12 and the pinned `requirements-lock.txt` from this directory. The source checkout stays on Windows; no Windows Docker/WSL. Copy this directory and its parent reference files to the Linux host. The pool archive is the same previously production-holdout-excluded snapshot supplied in `../deberta_v1/eligible.json.gz`.

```sh
python3 -m venv /tmp/jsm-supervised-venv
/tmp/jsm-supervised-venv/bin/pip install --extra-index-url https://download.pytorch.org/whl/cu121 -r requirements-lock.txt
gzip -dc ../deberta_v1/eligible.json.gz > /tmp/jsm-supervised-eligible.json
/tmp/jsm-supervised-venv/bin/python prepare.py --evaluation-root .. --output /tmp/jsm-supervised-replay
cp protocol.json train.py /tmp/jsm-supervised-replay/
/tmp/jsm-supervised-venv/bin/python train.py --root /tmp/jsm-supervised-replay --pool /tmp/jsm-supervised-eligible.json --fixtures ../fixtures.jsonl --output /tmp/jsm-supervised-replay/output --cache /tmp/jsm-supervised-model-cache
```

Model downloads and runtime measurements vary; the corpus, splits, seeds, tokenizer budget, optimization and dependency versions are explicit. The runner uses FP32 and deterministic algorithms on the GTX 1070. Reproducing on another GPU/driver can change floating-point results. Outputs go to a new directory; preserve the recorded artifacts.

The 12 trained safetensors checkpoints are archived and hash-verified on curiosity under `/home/codex/jsm-lab/experiments/cheap-reject-supervised-v1-20260905/<tag>/`. `checkpoint-manifest.json` records these retained paths. Prediction files preserve the original temporary training paths for provenance. These are experiment artifacts, not deployment models. They total several GB and are not added to the source repository. Retraining is reproducible from the pinned upstream models and bundled labels.

Score frozen predictions without training or a GPU (NumPy/scikit-learn required):

```sh
python score.py --root . --pool /tmp/jsm-supervised-eligible.json --evaluation-root .. --output /tmp/jsm-supervised-replayed-results.json
python -m unittest discover -s . -p 'test_*.py' -v
```

`diagnose.py` runs one trained checkpoint per invocation in a clean process, using `--root /tmp/jsm-supervised-20260905 --tag <tag>`. It checks the binary label mapping and title fail-open, compares original, case-preserving company masking, and casefold-only control inputs. For fold 2 it also measures fresh-process GPU/CPU inference. The primary inference benchmark includes tokenization and one forward pass per posting; cold start and weight loading are excluded. Process RSS is a high-water mark, not an isolated CPU tensor allocation.

`predictions.json` preserves all validation and employer-out cache scores, separate synthetic scores, epoch validation losses, trained checkpoint SHA-256s, original diagnostics and resource measurements. `diagnostics.json` contains the subsequent case-preserving controls and clean-process benchmarks. `results.json` contains the complete fixed threshold sweep, validation-selected points, per-employer and lexical novelty slices, calibration, errors, cache counts, and explicitly labeled retrospective upper bounds. Historical labels are provisional and previously inspected; these results do not certify production recall.

Additional post-hoc analysis: `calibration_frontier.py` produces `calibration-frontier.json`, an oracle that can independently choose each fold's threshold using evaluation labels and cache counts. This generously checks ranking potential despite cross-fold calibration differences. It is not a validated or recommended operating point. `analyze_diagnostics.py` regenerates `diagnostic-results.json`; `build_report.py` regenerates the detailed report locally. No training or holdout access is involved in these analyses.
