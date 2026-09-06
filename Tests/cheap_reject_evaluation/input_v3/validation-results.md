# Validation results

- Curiosity GPU pipeline: **PASS**. Fifteen new fits, four epochs each; three baseline fits reused. All 18 selected checkpoint hashes verified against model files on curiosity. New checkpoints archived under `/home/codex/jsm-lab/experiments/input-v3-20260905`; baseline checkpoints remain in the prior archive. See checkpoint-manifest.json.
- Six fresh-process, matched-input GPU/CPU inference benchmarks: **PASS**. No other experiment training was running during these benchmarks.
- 512-token feasibility: **PASS**, model maximum position count 512, original training batch size 4. This used a synthetic memory fixture with discarded weights, not new posting labels. See smoke.json. No batch-size reduction, mixed precision, or training protocol change was required.
- Exact prefix256 token parity against supervised_v2: **PASS**, all 4,701 corpus/cache postings, zero title overflows. See tokenizer-validation.json.
- New experiment unit tests: **13 PASS**, covering frozen hashes, employer/title-family/duplicate disjointness, binary-only fitting, one evaluation per posting, score completeness, checkpoint/source hashes, baseline parity, threshold ties, input budgets, title preservation, source-span uniqueness, missing bodies, and independence from labels/gold evidence.
- Existing cheap-reject tests: **23 PASS** (`python -B -X utf8 -m unittest test_evaluate test_leakage -v`).
- Existing corpus/adjudication tests: **13 PASS** (`python -B -X utf8 -m unittest test_corpus test_adjudication -v`).
- Total relevant tests: **49 PASS**.
- All experiment Python files parse successfully. Score/report/CSV/error-analysis replay is byte-identical on the validation runtime; see artifact-validation.json. Raw Linux JSON may use different newline conventions from regenerated Windows reports; scientific values are preserved.
- Explicit whitespace scan of new research files: **PASS**. `git diff --check`: **PASS**. Full application/.NET/UI/container suites were not run because this experiment changes no application or container code.
- Final artifact inventory and transfer verification: see artifact-manifest.json and `python -B -X utf8 manifest.py --verify`.

The experiment used only the frozen prepared corpus/cache, pinned encoder weights, and existing Linux runtime. It did not create labels, tune against the blinded production holdout, invoke Qwen/generative inference, modify occupation rules, change production behavior, deploy, or commit.

The source checkout remains on main, with no tracked or staged changes and zero commits ahead/behind the local origin/main tracking ref. Its existing untracked evaluation folder and four investigation documents remain; this experiment adds the isolated input_v3 folder within that evaluation folder. Final `git status` is reported in the task response.
