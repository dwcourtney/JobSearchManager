# Human review preparation v1

Start with [queue.md](queue.md), then fill `human_decision`, `human_confidence`, `human_reason`, `reviewer`, and `reviewed_at_utc` in [queue.csv](queue.csv). Save a reviewed copy so this blank preparation remains auditable. Decisions: KEEP / REJECT / AMBIGUOUS; confidence: high / medium / low. None is preselected.

Use [the review rubric, rule-impact map and migration plan](../../../docs/cheap-triage-human-review-preparation.md). The question is broad technical/engineering occupational scope, not whether David would personally apply. The prior machine interpretation is displayed after the duty excerpts to reduce anchoring.

Files:

- `queue.csv`: exactly 29 rows, 24 QUESTIONABLE and five LIKELY WRONG from the previous audit. All human fields blank. UTF-8 CSV with quoted fields; import as UTF-8 if the spreadsheet program does not detect it. Scores not already captured stay blank.
- `queue.md`: compact family/title index and readable cards with literal duty excerpts and every requested metadata field.
- `excerpt-provenance.json`: source cache, stable ID, original audit index, posting fingerprint, normalized-body hash, and exact excerpt character offsets/text. Full descriptions remain in the prior isolated curiosity archive rather than duplicating them here.
- `workflow-snapshot.json`: later read-only state capture from the two existing workspace histories, with anonymous aliases, history hashes and explicit/default provenance. Both resolve every queued job to normal. No account/authentication data is included.
- `manifest.json`: input/artifact hashes, rules identity, queue and family counts. This is a **research provenance manifest**, not an implemented classification pipeline manifest.
- `build-human-review.py`: standard-library-only offline preparation and validation. Literal snippet anchors select context from known jobs; they are not new production regex/classification rules. It refuses an existing output directory.

Reproduce from the frozen `unique.json` produced by the previous audit's `analyze.py` (see [prior audit reproduction](../live_shadow_v1/README.md)):

```text
python -B Tests/cheap_reject_evaluation/human_review_v1/build-human-review.py --raw PATH/unique.json --audit Tests/cheap_reject_evaluation/live_shadow_v1/review.jsonl --workflow Tests/cheap_reject_evaluation/human_review_v1/workflow-snapshot.json --output NEW_OUTPUT_DIRECTORY
```

The generated CSV, Markdown and excerpt/workflow artifacts should match their manifest hashes. The manifest's raw workflow-input hash may differ when using the normalized snapshot instead of the original SSH capture; the captured state and queue outputs remain the same. No runtime, provider, model, training, historical relabeling or production write occurs.

Queue construction asserts the exact 29 IDs, decision/evidence joins, blank human fields, CSV round-trip and source-exact excerpts. Human decisions must later be imported through a separately reviewed validation process; this script does not accept completed adjudications or apply rules. Two distinct Airborne Sensor Operator IDs remain in the queue; do not count them as independent evidence of generalization.
