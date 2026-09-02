# RegEx evaluation methodology

JSM treats measurement as an experiment with named data roles. It does not combine unlike evidence into one impressive-looking number.

## Dataset roles

**Development / regression** contains known positives, known negatives, hard negatives, edge cases, and detector-debugging examples. Rules may be written using it. `RegexValidationCorpus.json` is `curated-regression-v1` and is displayed as **CURATED REGRESSION BENCHMARK**. Its near-perfect historical score shows that known behavior has been preserved; it is not production accuracy and does not estimate unseen real-world performance.

**Validation** is a separately maintained labeled set for comparing future rule additions, removals, and consolidation. A scientifically independent validation dataset is not yet available. Until one exists, candidate comparisons against the curated corpus are regression evidence only.

**Production holdout / test** estimates generalization using postings drawn before detector output is inspected. It must never guide rule creation, exclusions, wording, or lifecycle decisions. The current status is **NOT YET AVAILABLE** because independent labels do not exist.

## Reproducible production sampling

`EvaluationHoldoutPlan.example.json` defines the population, companies, posting-date range, active/inactive criteria, sample size, method, and seed before selection. The maintenance command performs deterministic simple-random sampling by assigning each eligible, deduplicated posting a SHA-256 random rank derived from the seed. It records plan, population, and sample fingerprints plus a sampling-run ID. The sample excludes all RegEx and Qwen output.

```text
JobSearchManager --regex-maintenance sample-holdout regex-rules.db cache-root EvaluationHoldoutPlan.example.json frozen-holdout.json
```

The output is intentionally `frozen-unlabeled`. Labelers review posting text without detector results. Labels identify provenance honestly: human-reviewed, Codex-reviewed, machine-reviewed, consensus-reviewed, or ambiguous/unresolved. Codex labels are not human gold. RegEx or Qwen predictions are never ground truth.

Every example records its dataset role, sampling run, first-seen time when independently known, label status/provenance, detector-output exposure, use in rule development, and contamination time/reason. Unknown first-seen time remains null rather than being invented. If an example influences a rule, mark it contaminated, exclude it from unseen scoring, and draw a fresh replacement under a recorded plan.

## Metrics and interpretation

For each concept, the ledger stores support through TP, FP, FN, and TN, plus precision, recall, and F1. A perfect F1 with support 2 is much weaker evidence than a similar result with support 200.

- Precision asks: of predicted positives, how many were labeled positive?
- Recall asks: of labeled positives, how many were detected?
- F1 is the harmonic mean of precision and recall. It is not accuracy.
- Macro averages give each concept equal weight.
- Micro averages pool decisions, so high-volume concepts have more influence.
- Prevalence is the share of decisions labeled positive (or negative).

Each evaluation run preserves its purpose, timestamp, ruleset, taxonomy and configuration fingerprints, dataset role/fingerprint, sampling method and seed, posting and decision counts, label provenance, overall metrics, per-concept metrics, per-rule evidence, timeouts, and notes. This answers “what exactly did F1 = X measure?” without relying on current code or memory.

Rule-level results include matches, true and false positives, precision, unique true positives, redundant true positives, representative matches, false-positive examples, and timeouts. Standalone rule recall/F1 is not treated as inherently meaningful because multiple rules can jointly implement a concept.

## Leakage, contamination, and negative results

Leakage occurs when test information influences detector design. Cherry-picking, sampling after seeing results, relabeling predictions as truth, removing difficult examples, or repeatedly tuning to holdout failures all invalidate a generalization claim. A holdout example viewed for development is contaminated even if no code is ultimately changed.

Random sampling gives eligible postings a known selection mechanism. A recorded seed makes the draw reproducible. Stratified random sampling can control source imbalance, but its strata and allocation must be declared before selected postings or detector results are inspected. The current sampler supports only predefined simple random sampling so it cannot silently improvise strata after inspection.

A lower independent holdout F1 is valid and more useful than a perfect contaminated result. Negative results remain in the ledger.

## Precision-recall curves

The current detector returns boolean present/absent decisions. Precision, recall, and F1 at that operating point are meaningful; a threshold-swept PR curve is not. Records include an optional future prediction score, but JSM does not manufacture pseudo-confidence. PR curves become appropriate only if a later detector produces a legitimate continuous evidence score whose thresholds have a defensible meaning.

Evaluation is explicit Admin/maintenance work. It never runs on normal job requests, and benchmark classifications do not increment production rule-use counters.
