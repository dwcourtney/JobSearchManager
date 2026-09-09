# One-concept improvement rounds

This is an explicit offline development workflow. It never edits production rules,
publishes a live evaluation, deploys a candidate, or starts a model in normal JSM.
The target is any exact ID in `JobConceptCatalog.json`; workflow logic has no CI/CD
special case. Use a new external round directory for each candidate. Existing global
evaluation artifacts and their scoring/metric implementation are reused unchanged.

## Start a round

Export only public cached-posting fields with the existing exporter. Keep the export,
raw prompts/labels, predictions and complete round archive outside the app repository.

```text
python tools/concept-evaluation/export_public_cache.py --cache-root <cache-root> --destination <new-public-export>
python tools/concept-evaluation/improve_concept.py start --run <history>/<concept-id>/<new-round> --concept <concept-id> --cache-root <public-export> --taxonomy JobConceptCatalog.json --rules rules/concepts-v1.json --history-root <history> --exposed-sample <prior-global-run>/sample.json --count 500 --seed <new-discovery-seed>
```

Sampling uses seeded SHA-256 ranking without replacement after existing posting-ID,
workspace-copy and normalized-description deduplication. It excludes all known prior
round samples and supplied exposed samples from both new discovery and fresh validation.
It reserves the remaining fresh population without exposing it to failure mining.
Broader development mining uses only other, already exposed postings. The actual
sample sizes and shortfall are explicit. No silent overlap fallback exists. Provide
all prior round history and known exposed samples; the tool cannot infer omitted history.

The discovery set is frozen immediately. The validation pool is frozen but its sample
is drawn with a separate seed only after the candidate seal and development evaluation.
Do not inspect the reserved pool or use it in terminology mining. Near duplicates may
remain; the available cache reflects user searches, not the whole labor market.

## Label only the target concept

Each phase directory contains the unchanged full taxonomy plus a verified one-concept
selection. The production predictor validates the selection against the full catalog.
Labelers receive only the selected definition and public posting text, never rules,
predictions, other labels, or validation history. Use the existing no-tool audited CLI
adapter, fresh ephemeral sessions and the same A/B/third-blinded-decision protocol.

```text
python tools/concept-evaluation/evaluate.py export --run <round>/discovery --phase a --batch-size 10
python tools/concept-evaluation/evaluate.py export --run <round>/discovery --phase b --batch-size 10
python tools/concept-evaluation/label_codex.py --run <round>/discovery --phase a --codex <executable> --model <available-model>
python tools/concept-evaluation/label_codex.py --run <round>/discovery --phase b --codex <executable> --model <available-model>
python tools/concept-evaluation/evaluate.py export --run <round>/discovery --phase adjudication --batch-size 10
python tools/concept-evaluation/label_codex.py --run <round>/discovery --phase adjudication --codex <executable> --model <available-model>
python tools/concept-evaluation/evaluate.py freeze-reference --run <round>/discovery
```

Empty adjudication sets are valid when A/B agree completely. Null remains unresolved
and excluded, never coerced to negative. Exact positive evidence quotes and complete
boolean/null vectors are required. Preserve failed attempts and retry missing batches
in fresh sessions. See the global evaluation README for external/manual batch import.
These are AI references, not human ground truth; same-model errors can be correlated.

## Baseline, evidence and candidate

```text
dotnet run --project tools/ConceptEvaluation/ConceptEvaluation.csproj -c Release -- <round>/discovery/sample.json <round>/discovery-baseline-predictions.json --rules <round>/baseline-rules.json
python tools/concept-evaluation/improve_concept.py evaluate --run <round> --role discovery --variant baseline --predictions <round>/discovery-baseline-predictions.json --policy evaluation/concept-detection/score-policy-v1.json
python tools/concept-evaluation/improve_concept.py mine --run <round>
```

Review frozen false-positive/false-negative decisions, exact evidence, recurring phrase
counts and broader-corpus contexts in `failure-analysis.json`. The miner derives generic
seeds from the concept definition and discovery-positive quotes. Its phrase counts are
posting counts, not occurrence inflation. Mining is a suggestion aid; a human/agent must
distinguish real concept evidence from association and document each proposed change.

Create an isolated full ruleset JSON and rationale keyed by each changed/new rule ID:
`sourcePhrases`, `supportingPostingIds`, `falsePositiveRisk`. Rationale phrases must appear
in the cited discovery/mining postings; held-out IDs are rejected. Existing IDs must stay.
No other concept rule may change. Adding rules may require mechanical canonical
`executionOrder` renumbering; no unrelated rule semantics may change. Existing .NET
schema/regex/100 ms bounded matching validation applies to candidate loading.

```text
python tools/concept-evaluation/improve_concept.py seal-candidate --run <round> --candidate <proposal.json> --rationale <rationale.json>
dotnet run --project tools/ConceptEvaluation/ConceptEvaluation.csproj -c Release -- <round>/discovery/sample.json <round>/discovery-candidate-predictions.json --rules <round>/candidate-rules.json
python tools/concept-evaluation/improve_concept.py evaluate --run <round> --role discovery --variant candidate --predictions <round>/discovery-candidate-predictions.json --policy evaluation/concept-detection/score-policy-v1.json
```

Discovery metrics are development only. Candidate files, rationale and seal are immutable.
An edited candidate requires a new round; never tune it against this round's validation.

## Fresh validation

```text
python tools/concept-evaluation/improve_concept.py validation --run <round> --count 500 --seed <new-validation-seed>
```

Repeat the exact A/B/adjudication/freeze sequence above with `validation` instead of
`discovery`. Then run both baseline and candidate predictions and `evaluate` with
`--role validation`. Candidate/sample/reference hashes are checked. The production
matcher still emits all concept outputs, permitting exact 84-other-concept comparison.

## Historical regression and decision

Reuse frozen samples/references from earlier rounds and the global evaluation without
relabeling. Convert curated/legacy fixtures to the same sample/reference shape while
retaining provenance and null for unlabeled concepts. Unlabeled current-cache replays
use null references and are decision-change/parity checks, not quality measurements.
Never present earlier discovery as fresh validation. Preserve original source hashes.

Create a dataset list with `name`, `sample` (directory), `baseline` and `candidate`
(prediction file paths). Generate both predictions with explicit frozen rule paths.

```text
python tools/concept-evaluation/improve_concept.py regression --run <round> --datasets <dataset-list.json> --policy evaluation/concept-detection/score-policy-v1.json
python tools/concept-evaluation/improve_concept.py decide --run <round> --regression <round>/regression-results.json --review <review.json>
```

Review every changed target decision and any new FP family. `review.json` must explicitly
record `noUnexplainedRegression` and `noBroadFalsePositiveFamily`, with supporting prose.
Acceptance thresholds are frozen at round start: F1 gain at least 5 percentage points,
precision at least 80%, precision loss no more than 3 points, maintained/improved AP,
at least ten positive and ten negative validation examples, exact untouched-concept
parity, and explicit regression review. Failure of a metric/parity gate means REJECT;
insufficient support or missing satisfactory qualitative review means NEEDS REVIEW.
Passing means ACCEPTABLE FOR RELEASE REVIEW, never automatic production adoption.

AP and scores use the existing separately versioned evidence policy without changes.
AP is a threshold/tie-aware recall-weighted precision sum, not trapezoidal PR AUC.
Results contain confusion counts, metrics, full raw PR points, score distributions,
accepted rule IDs/evidence and per-posting decisions. Rule improvements can legitimately
change downstream Job Fit only for the target if eventually released; this command does
not release anything. Record the recommendation, limitations and all changed decisions.

## Tests and retention

```text
python -B -m unittest discover -s tools/concept-evaluation -p "test_*.py" -v
dotnet build tools/ConceptEvaluation/ConceptEvaluation.csproj -c Release
```

Run the existing complete application, UI, provider/cache/auth/workflow, security and
cross-platform suites before review. Preserve the entire external round including raw
label sessions. Compact candidate/rationale/metrics/decision artifacts may be placed
under `research/concept-improvement/<concept-id>/rounds/<id>`; research is excluded from
normal app compile/publish/runtime. Keep raw cache exports, host backups and credentials
outside Git. No Admin redesign, automatic scheduler, commit, push or deployment occurs.

For downstream score parity with the target left neutral, replay through the actual Jobs caller:

```text
node tools/concept-evaluation/job_fit_parity.js <repository> <baseline-predictions.json> <candidate-predictions.json> <target-concept-id>
```

This compares all postings under five unaffected-concept preference profiles, configured/null travel and work-location settings, and enabled/disabled Job Fit. Neutral evidence rows may change; score, total and active dimension impacts must not.
