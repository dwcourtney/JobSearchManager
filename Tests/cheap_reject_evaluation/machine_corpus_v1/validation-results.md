# Corpus adjudication validation

Status: 3,198 direct Codex decisions completed. No Qwen or other model service, classifier training, production changes, RegEx changes, or commits.

## Checks performed

- PASS: 13 tests with `python -B -X utf8 -m unittest discover -s . -p "test_*.py" -v` (five preparation tests and eight adjudication tests).
- PASS: exact 3,198-record coverage, original source-field preservation, label/confidence schema, evidence offsets against source text, manifest hashes, frozen ordinal mapping, summary reconciliation, targeted correction audit, review queue coverage.
- PASS: holdout and prior-evaluation exclusion intersections remain empty; frozen sample fingerprint unchanged; exclusion index contains no holdout text or labels.
- PASS: byte-identical replay of all seven generated artifacts from frozen explicit decisions and targeted reviews.
- PASS: Python syntax and new-text whitespace checks.
- PASS: repository git diff --check; all 15 transferred artifacts and original sample match staging hashes; all 13 tests pass from the repository location.
- Earlier preparation validation also verified byte-identical sampling replay from frozen eligible exports.

Application/.NET/UI/container tests were not run: this change adds offline data/reporting scripts only, with no application or container changes.

## Results and limitations

KEEP 2,121; REJECT 548; AMBIGUOUS 529. Confidence: high 1,014; medium 1,655; low 529.

Description available: 1,312 (KEEP 914, REJECT 245, AMBIGUOUS 153). Description unavailable: 1,886 (KEEP 1,207, REJECT 303, AMBIGUOUS 376). All missing-description decisions use title only and have medium or low confidence. One additional posting has only boilerplate in its description and was adjudicated from title only.

Every title was reviewed. Descriptions were reviewed through recorded excerpts and targeted additional spans, not exhaustively in full. Forty-five targeted second reads produced 13 class changes. There were zero class disagreements across 207 repeated title-only groups (602 records), and zero across two identical nonempty-body groups. These are internal consistency checks, not an accuracy estimate. No independent human accuracy or KEEP recall estimate is available.

The review queue contains 281 cases, including 201 low-confidence cases, and includes all observed title/body and jargon/duty conflicts. Do not treat provisional labels, especially missing-body rows, as human gold or evidence of 98-99% KEEP recall. AMBIGUOUS must not be forced into a binary training class.

## Exact adjudication files

All files are under `Tests/cheap_reject_evaluation/machine_corpus_v1/`.

Added:

- `adjudicate.py`
- `codex-decisions.jsonl`
- `quality_reviews.py`
- `codex-quality-reviews.jsonl`
- `build_labeled_corpus.py`
- `test_adjudication.py`
- `machine-labeled.jsonl`
- `human-review.jsonl`
- `human-review.csv`
- `label-summary.json`
- `quality-checks.json`
- `adjudication-manifest.json`
- `adjudication-report.md`

Updated:

- `README.md`
- `validation-results.md`

Existing preparation inputs, sampling manifest and exclusion files are unchanged. The broader evaluation directory and four earlier reports were already untracked; they are not new work from this adjudication step.
