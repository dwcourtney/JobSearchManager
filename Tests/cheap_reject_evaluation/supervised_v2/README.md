# Supervised corpus experiment v2

Offline research only. No production imports, triage-rule changes, Qwen/generative model calls, production holdout, deployment or commits. The user authorized experimental training on the provisional corpus; its original `trainingEligibility` metadata remains unchanged for provenance and does not describe a new production approval.

Read `interpretation.md` for the decision, `report.md` for complete comparison tables, and `validation-results.md` for checks. `results.json` and `threshold-sweep.csv` retain detailed metrics. The experiment produces no deployed classifier.

## Frozen inputs

- `corpus.jsonl`: all 3,198 Codex-adjudicated postings from `../machine_corpus_v1/machine-labeled.jsonl`, with a deterministic split component added. 2,669 KEEP/REJECT rows are eligible for binary training; 529 AMBIGUOUS rows remain separate.
- `cache.jsonl`: previous 1,503-posting, production-holdout-excluded `../deberta_v1/eligible.json.gz`. IDs are namespaced. Labels from `../supervised_v1/corrected.jsonl` are attached solely for legacy stress evaluation; none are used in fitting or calibration.
- `splits.json`: every train/validation/evaluation ID, employer assignments and source hashes. Employer-disjoint and corpus lexical-family/duplicate-component-disjoint. This is not a guarantee of semantic occupational-family independence.
- `baseline.json`: frozen existing electrical-safe rule decisions on exactly the same postings, source code hashes and Windows timing. No rule code is modified.
- `protocol.json`: exact base revisions, fixed hyperparameters, weighting, views and threshold protocol established before training.

## Reproduce

Run preparation in the Windows source checkout using Python and the existing experiment dependencies:

```sh
python -X utf8 prepare.py --evaluation-root .. --output replay
```

Copy the prepared data, protocol and scripts to an isolated experiment directory on `ssh curiosity-codex`. Use its established Python environment (`requirements-lock.txt` records exact versions), GPU, and pinned model cache. Do not configure Windows Docker/WSL. The recorded execution used `/tmp/jsm-supervised-corpus-v2`, `/tmp/jsm-deberta.dxVPsh/venv/bin/python`, and `/tmp/jsm-deberta.dxVPsh/model-cache`.

```sh
python -u train.py --root . --cache /tmp/jsm-deberta.dxVPsh/model-cache
python finish.py
python archive.py --root . --destination /home/codex/jsm-lab/experiments/supervised-corpus-v2-20260905
python score.py --root .
python build_report.py
python -B -m unittest discover -s . -p 'test_*.py' -v
```

Pass an absolute experiment path to `train.py` if running from another directory. Training resumes only from completed per-run JSON records, not interrupted epochs. Use a fresh output directory for a full replay. `finish.py` requires all 36 fits, benchmarks each fold-2 checkpoint in a fresh process, and freezes dependencies. `archive.py` refuses an existing destination. See `checkpoint-manifest.json` for preserved weights; model weights are retained on curiosity rather than copied into the source repository.

Frozen predictions reproduce metrics exactly. Repeating training/benchmarks depends on the recorded software, hardware and deterministic settings; runtime measurements naturally vary. B and C are identical datasets, so there are three distinct training regimes, not four duplicate runs. All treatments share binary validation; described-only means filtering training, with described evaluation separately reported.

## Interpretation limits

Codex labels are not independent human truth. Title-only labels are weak. Eighty binary rows in the existing human-review queue remain eligible with their recorded confidence; this experiment does not silently resolve them or certify description-present labels as clean. Body prefixes may omit adjudication evidence. Filters change both evidence quality and the number of optimizer steps, so their effects cannot be attributed exclusively to label quality. Only two model capacities and one fixed hyperparameter/seed protocol were tested; the experiment does not isolate the optimal capacity or establish a learning curve.

Validation-selected thresholds are distinct from retrospective evaluation frontiers. No outer label adjusts checkpoint selection, hyperparameters or operating thresholds. Employer-name masking and body-tail scoring are evaluation-only sensitivity diagnostics. Literal name masking is not an added occupational triage rule. All cache predictions exclude the posting's employer from training and validation; they are a cross-fit workload estimate, not a single production model.
