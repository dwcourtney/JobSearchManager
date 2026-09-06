# Validation results — 2026-09-05

* Curiosity GPU: pinned safetensors SHA verified; all 1,563 postings scored in each of three views (4,689 posting/view evaluations, two NLI pairs each), no inference errors.
* Curiosity `validate_deberta.py`: PASS. All 4,689 inputs preserve full titles and stay within 512 tokens; overlong title fails open; deterministic head/tail slicing; manual tokenizer pair construction and logits match the standard API; entailment/contradiction/neutral sanity cases pass. Raw output: `validation.log`.
* Windows `python -B -X utf8 -m unittest discover -s Tests/cheap_reject_evaluation/deberta_v1 -p 'test_*.py' -v`: 7/7 PASS (frozen artifacts, decision logic, metric arithmetic, frontier replay).
* Windows `python -B -X utf8 -m unittest discover -s Tests/cheap_reject_evaluation -p 'test_*.py' -v`: existing 23/23 PASS, including unchanged frozen development labels, electrical-safe results, and audit checks.
* Frozen-score replay: all 72 configurations, all reference metrics, diagnostic examples and selected choices match exactly. Only fresh CPU timing is excluded from equality. Recorded baseline timings were measured on curiosity; the verification replay ran on Windows.
* `prepare_input.py`: exact byte-for-byte replay against the archived label-free input, including deterministic line ending. Eligible pool canonical hash matches the prior manifest.
* `scripts/validate-source.ps1`: PASS, including its JavaScript syntax/UI tests and source architecture checks. An initial attempt to invoke an unavailable `powershell` executable failed before running anything; invoking the script in the existing PowerShell session passed.
* Whitespace: `git diff --check` and explicit UTF-8 trailing-whitespace checks for new text files PASS. Tracked diff is empty.

No .NET runtime tests, new container build, Trivy scan, or actual GitHub CodeQL run were needed or performed in this experiment: no application/container dependencies or production code changed. The source validation script's security/CodeQL integration tests check repository integration, not live security-scan results. This is not a production-readiness certification.

No commits, push, production discard activation, RegEx changes, training, Qwen, or second-stage work. Final repository changes are untracked evaluation/report artifacts only; local main and origin/main have no divergence as recorded by local refs.
