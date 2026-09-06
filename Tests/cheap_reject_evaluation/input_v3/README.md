# Frozen input-representation experiment

Offline research only. No production changes, new labels, occupation rules, holdout tuning, generative inference, training deployment, or commits.

`report.md` contains the completed results. `protocol.json` fixes the experiment before training. The only training condition is supervised_v2's all-binary-data regime. The corpus, cache, splits, and electrical-safe decisions are byte-identical copies; `baseline-provenance.json` records their prior hashes and the three reused baseline prediction files.

## Inputs

Every representation preserves the full tokenized `Title: ...`. The remaining budget includes `Body:`, model special tokens, and a literal `[...]` between disjoint spans. Spans are chronological, merged, and never duplicated. Empty bodies receive the same title and empty Body prefix in all representations. A title exceeding the budget fails open at inference and aborts training; no frozen posting does so.

- **prefix256:** existing leading-body input, exactly reproduced; reuse the three v2 all-data checkpoints/predictions.
- **headtail256:** divide remaining body tokens approximately equally between beginning and end.
- **headmidtail256:** approximately equal beginning, centered middle, and end windows.
- **prefix384 / prefix512:** increase leading-body budget, preserving the title.
- **sections256:** reserve 25% for the body beginning, 50% for the first generic duties heading and following text, and 25% for the first generic qualifications heading and following text. Missing headings fall back to middle/end. Merge overlapping windows and spend overlap savings on head context. See the frozen literal heading lists in `inputs.py`; these contain no occupations. No label, employer, category, reason, or adjudication evidence is available to the selector.

Full bodies are tokenized before deterministic selection; benchmarks include this work. The sequence classifier never receives more than its configured 256/384/512 tokens. Tokenizer warnings about the full intermediate body length do not mean the classifier receives overlength sequences.

## Training and comparison

One pinned DeBERTa NLI encoder with a fresh binary head; all parameters trained. Same three employer-held-out folds, duplicate/title-family purging, train-only class weights, seeds, four epochs, learning rate, optimizer, FP32, batch size 4, and accumulation 4 as v2. Minimum unweighted validation cross entropy chooses the checkpoint. Three validation KEEP-recall targets (100%, 99%, 98%) independently set each fold's rejection threshold. Ties KEEP. Evaluation labels do not select thresholds or checkpoints. Fifteen new fits plus three reused baseline fits, no BERT-Tiny rerun.

The fixed representation hypotheses were motivated by prior evaluation errors. Reusing those folds makes this a development comparison, not a fresh independent test. The labels remain Codex-adjudicated and provisional; absence of false rejects against them cannot prove human-ground-truth safety. The 1,503-posting old cache includes 347 older labeled probes with partially different occupational definitions; its rejection rate is workload coverage, not validated accuracy.

Evidence coverage is a separate audit against recorded reviewed spans. It reports any character overlap, at least half the recorded characters, all recorded characters, and mean covered fraction. These spans record evidence consulted during adjudication, not exhaustive ground truth for every relevant sentence. Coverage can improve without improving classification.

## Reproduction on curiosity

Use the existing `/tmp/jsm-deberta.dxVPsh/venv` and pinned model cache, or recreate the versions in `requirements-lock.txt`. Stage this folder at `/tmp/jsm-input-v3`. The baseline checkpoints remain in `/home/codex/jsm-lab/experiments/supervised-corpus-v2-20260905`; new checkpoints are archived separately by `archive.py`.

Run `sh run.sh` on curiosity. The launcher uses offline model loading and runs GPU feasibility, coverage, training, scoring, six fresh-process benchmarks, and checkpoint archiving. Completed per-fit JSON files are resumable; reproduce training in a fresh output directory containing only the three reused prefix256 prediction JSONs. Run `validate_tokenizer.py` on curiosity for exact old/new baseline token parity. Run `python -B -m unittest test_inputs test_experiment -v` after retrieving results. `report.py` regenerates tables and analysis from frozen scores; retrospective threshold frontiers are explicitly diagnostic.
