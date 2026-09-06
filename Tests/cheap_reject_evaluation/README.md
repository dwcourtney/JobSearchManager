# Cheap rejection development experiment

This directory is offline evaluation material only. Nothing here is loaded by production JSM. The main project already excludes `Tests/**` from application content.

Run from the repository root using Python with NumPy and scikit-learn available:

```powershell
python -X utf8 -m unittest discover -s Tests/cheap_reject_evaluation -p 'test_*.py' -v
python -X utf8 Tests/cheap_reject_evaluation/evaluate.py --source-root . --output "$env:TEMP/jsm-cheap-reject-results.json"
```

The recorded run used Python 3.12.5, NumPy 2.0.2 and scikit-learn 1.5.2; consult `results.json` for the actual environment and source fingerprints. The scripts require no network/model service and make no application state changes. Runtime measurements vary by machine. This is a development experiment, not a packaged production model; no new application dependencies are introduced.

`development.jsonl` contains 166 deduplicated public posting texts with provisional editorial KEEP/REJECT labels and per-row reasons. Labels were reviewed by the coding assistant before candidate predictions, without calling Qwen, any external LLM API, or using saved model predictions. They are not independently human-adjudicated ground truth. `fixtures.jsonl` contains 60 explicitly synthetic contrast cases, never used to train the classical models. Synthetic performance is reported separately. `manifest.json` records selection, exclusions, provenance and dataset hashes. Do not silently relabel these files after seeing results; a changed target should get a new version.

KEEP means plausibly technical job-search work or unresolved adjacent technical duties. Software, systems, integration, automation, technical infrastructure/admin, cloud, data/AI, and technical solution consulting survive. Ambiguous mechanical/electrical engineering and mixed technical support survive. REJECT requires affirmative unrelated primary duties: clinical care, physical driving, construction/trades, accounting, pure selling, recruiting, hospitality, guarding, production/machine work, or nontechnical business administration. Civil construction/bridge/roadway design with no software or IT duties can be REJECT; discipline or the word “engineer” alone cannot establish this. Using office software, selling a technical product, or working for a technical employer does not establish technical duties. Seniority, location, travel, clearance, certifications and final suitability are outside this occupational target.

The fixed rule candidates are descriptive development results: rule vocabulary was designed after reviewing development data. Classical results use five employer-disjoint folds; each text is predicted only by a model trained on other employers. TF-IDF is fitted inside each training fold. Fixed KEEP-score thresholds are 0.05, 0.1, 0.2 and 0.5; they are not calibrated confidence estimates. Title-weighted TF-IDF scales title features 3:1 against body features. Full-development fits are used only for latency, synthetic contrast tests and optional unlabeled shadow counts, never for reported empirical KEEP recall. No candidate or threshold was evaluated on the existing blinded holdout.

Optional source-pool replay on the established Linux host:

```sh
python3 export_pool.py \
  --cache-root /home/codex/jsm-lab/data/app \
  --exclude-manifest /home/codex/jsm-lab/data/app/evaluation/holdout.json \
  --exclude-manifest /home/codex/jsm-cicd/evaluation/production-holdout-unlabeled-20260902.json \
  --output-dir /tmp/jsm-cheap-reject-pool
```

The exporter uses the holdout manifests solely as identity exclusion sets. No holdout examples, labels or predictions are exported. It excludes matching stable IDs, content hashes and every normalized title group before exporting only public title/body/company fields. The two manifests in this run were byte-identical; the audit records both hashes. The caches are live, so future exports may differ. The eligible snapshot has a canonical SHA-256 in the manifest. It was replayed and verified identical during this investigation. No Docker or inference work is necessary for this evaluation.

Given that already-excluded `eligible.json` pool, selection is reproducible:

```sh
python sample.py eligible.json manifest.json /tmp/unlabeled-selection.json
python evaluate.py --source-root /path/to/repo --shadow-pool eligible.json --output /tmp/replayed-results.json
```

Selection uses SHA-256 order with the fixed seed, title deduplication and per-family quotas before any labels are applied. The frozen 166 selected records are self-contained; the entire unlabeled pool is intentionally not added to the repository. Consequently, the development metrics are reproducible from this directory alone, while shadow counts additionally require the matching eligible pool. Neither a purposively balanced sample nor synthetic fixtures can estimate real workload reduction. Read the accompanying report in `docs/cheap-reject-investigation.md` before interpreting the results.

## First-stage leakage continuation

See `docs/cheap-reject-leakage.md` for the follow-up. `leakage.py` preserves the original baseline and records eight offline variants, including failed variants. The recommendation is `electrical-safe` for non-discarding shadow evaluation only. The original development labels and results remain unchanged; `leakage-results.json` explicitly records a General Finance label disagreement discovered during duty review.

`leakage-audit.jsonl` contains the 196-record stratified review; its weights are required for prevalence estimates. `leakage-delta-audit.jsonl` contains a 41-record post-prediction change audit and must not be used for prevalence estimation. Neither is an independent held-out evaluation. Dataset fingerprints and review limitations are in `leakage-manifest.json`.

Run from the repository root:

```powershell
python -X utf8 -m unittest discover -s Tests/cheap_reject_evaluation -p 'test_*.py' -v
python -X utf8 Tests/cheap_reject_evaluation/leakage.py --pool <matching-eligible.json> --output "$env:TEMP/jsm-leakage-results.json"
```

The full-cache replay requires the same previously exported 1,503-record `eligible.json` snapshot; a mismatched canonical hash is rejected. No new holdout access or server inference is required. The labeled-set metrics and changed-decision review coverage can be replayed by the tests from these files alone. No production module imports these scripts.
