# RegEx evaluation methodology

JSM treats measurement as an experiment with named data roles. It does not combine unlike evidence into one impressive-looking number.

## Dataset roles

**Development / regression** contains known positives, known negatives, hard negatives, edge cases, and detector-debugging examples. Rules may be written using it. `RegexValidationCorpus.json` is `curated-regression-v1` and is displayed as **CURATED REGRESSION BENCHMARK**. Its near-perfect historical score shows that known behavior has been preserved; it is not production accuracy and does not estimate unseen real-world performance.

**Validation** is a separately maintained labeled set for comparing future rule additions, removals, and consolidation. A scientifically independent validation dataset is not yet available. Until one exists, candidate comparisons against the curated corpus are regression evidence only.

**Production holdout / test** estimates generalization using postings drawn before detector output is inspected. It must never guide rule creation, exclusions, wording, or lifecycle decisions. The frozen 200-posting sample is evaluated as **AI-ADJUDICATED PRODUCTION HOLDOUT**. Its references are machine-derived, not human ground truth, and its results stay separate from the curated benchmark.

## Reproducible production sampling

`EvaluationHoldoutPlan.example.json` defines the population, companies, posting-date range, active/inactive criteria, sample size, method, and seed before selection. The maintenance command performs deterministic simple-random sampling by assigning each eligible, deduplicated posting a SHA-256 random rank derived from the seed. It records plan, population, and sample fingerprints plus a sampling-run ID. The sample excludes all RegEx and Qwen output.

```text
JobSearchManager --regex-maintenance sample-holdout regex-rules.db cache-root EvaluationHoldoutPlan.example.json frozen-holdout.json
```

The sampler output is intentionally `frozen-unlabeled`. Labelers review posting text without detector results. Labels identify provenance honestly: human-reviewed, Codex-reviewed, machine-reviewed, consensus-reviewed, or ambiguous/unresolved. Codex labels are not human gold. RegEx or Qwen predictions are never ground truth.

## Prediction-blinded AI reference pipeline

The automated pipeline uses three isolated Codex roles. Labeler A and Labeler B each see only the frozen item ID and posting plus the canonical 85 concept IDs, names, definitions, and interpretation guidance. B never sees A. Neither sees RegEx rules, predictions, evidence, scores, benchmark failures, or Qwen results. After both full passes, an adjudicator sees only the posting, one disputed concept, and the two conflicting judgments. It may preserve `unresolved`.

The enforced order is:

1. validate the unchanged frozen sample and prediction-blinded A output;
2. validate the independent B output;
3. compare all 17,000 posting/concept decisions;
4. adjudicate disagreements only;
5. freeze all final references and calculate their fingerprint;
6. only then invoke RegEx and calculate metrics;
7. write an immutable report and evaluation-ledger record.

Progress is written after every phase, survives page refresh, and continues without an open browser. Missing or malformed AI artifacts cause a durable failure; JSM does not substitute Qwen or reveal detector output. Every final decision preserves the posting hash, taxonomy and prompt fingerprints, reviewer identities, A and B judgments, agreement state, adjudication when needed, final judgment, unresolved state, and contamination state.

The hardened JSM container does not contain Codex credentials or an embedded hosted-model client. Authorized Codex evaluation workers produce resumable blinded artifacts in the server-accessible evaluation directory; the Admin action validates those artifacts, performs compare/freeze/score/ledger orchestration, and never requires the browser to remain open. This execution boundary avoids mounting a personal Codex session into the product container and makes a missing worker artifact fail explicitly instead of falling back to Qwen.

The exact result disclaimer is: **Reference labels were generated through prediction-blinded AI review and adjudication. They are not human-ground-truth labels.**

Two independent AI passes are useful because disagreement exposes instability and adjudication focuses review on ambiguous decisions. They are still not equivalent to independently human-labeled ground truth. Codex roles may share model-family biases; agreement measures consistency rather than truth; low-support concepts remain uncertain; and unresolved decisions reduce evaluable sample size.

Every example records its dataset role, sampling run, first-seen time when independently known, label status/provenance, detector-output exposure, use in rule development, and contamination time/reason. Unknown first-seen time remains null rather than being invented. If an example influences a rule, mark it contaminated, exclude it from unseen scoring, and draw a fresh replacement under a recorded plan.

## Metrics and interpretation

For each concept, the ledger stores support through TP, FP, FN, and TN, plus precision, recall, and F1. A perfect F1 with support 2 is much weaker evidence than a similar result with support 200.

- Precision asks: of predicted positives, how many were labeled positive?
- Recall asks: of labeled positives, how many were detected?
- F1 is the harmonic mean of precision and recall. It is not accuracy.
- Macro averages give each concept equal weight.
- Micro averages pool decisions, so high-volume concepts have more influence.
- Prevalence is the share of decisions labeled positive (or negative).
- Support is the number of eligible reference-positive decisions for a concept.

An unresolved AI reference is excluded from binary TP/FP/FN/TN calculations and counted explicitly. If accuracy is ever reported, it must be separately named and defined as `(TP + TN) / eligible decisions`; it must never be substituted for F1.

Each evaluation run preserves its purpose, timestamp, ruleset, taxonomy and configuration fingerprints, dataset role/fingerprint, sampling method and seed, posting and decision counts, label provenance, overall metrics, per-concept metrics, per-rule evidence, timeouts, and notes. This answers “what exactly did F1 = X measure?” without relying on current code or memory.

Rule-level results include matches, true and false positives, precision, unique true positives, redundant true positives, representative matches, false-positive examples, and timeouts. Standalone rule recall/F1 is not treated as inherently meaningful because multiple rules can jointly implement a concept.

## Leakage, contamination, and negative results

Leakage occurs when test information influences detector design. Cherry-picking, sampling after seeing results, relabeling predictions as truth, removing difficult examples, or repeatedly tuning to holdout failures all invalidate a generalization claim. A holdout example viewed for development is contaminated even if no code is ultimately changed.

Random sampling gives eligible postings a known selection mechanism. A recorded seed makes the draw reproducible. Stratified random sampling can control source imbalance, but its strata and allocation must be declared before selected postings or detector results are inspected. The current sampler supports only predefined simple random sampling so it cannot silently improvise strata after inspection.

A lower independent holdout F1 is valid and more useful than a perfect contaminated result. Disappointing results remain in the immutable ledger. A holdout failure must not immediately become a new rule, exclusion, or wording change: that would turn test evidence into training data. If a posting is deliberately used later, record contamination, remove it from unbiased scoring, move/copy it to development evidence, and use a newly sampled unseen replacement under a new declared plan.

## Precision-recall curves

The current detector returns boolean present/absent decisions. Precision, recall, and F1 at that operating point are meaningful; a threshold-swept PR curve is not. Records include an optional future prediction score, but JSM does not manufacture pseudo-confidence. PR curves become appropriate only if a later detector produces a legitimate continuous evidence score whose thresholds have a defensible meaning.

Evaluation is explicit Admin/maintenance work. It never runs on normal job requests, and benchmark classifications do not increment production rule-use counters.

## Apples-to-apples local LLM evaluation

Admin -> Evaluation -> LLM uses the same frozen 200 postings, 85 concepts, reference-label
fingerprint, 97 unresolved exclusions, and metric calculator as the RegEx production-holdout run.
The versioned `job-fit-85-compact-json-v2` prompt uses the same pinned model, temperature, seed,
context length, and maximum output limit with a bounded, hashed 85-boolean output contract. V1's
incomplete 25-posting technical run is preserved separately and is never combined with v2. Qwen
receives only the full posting and canonical taxonomy; the v2 contract was fixed from technical
failure evidence and synthetic preflight, without reference labels or holdout scores. Qwen never
receives references, RegEx rules or predictions, RegEx evidence or scores, benchmark results, or
Codex A/B artifacts.

The enforced order is: validate the unlabeled holdout, run or safely resume exactly one prediction
per posting, freeze and fingerprint all 200 x 85 predictions, and only then open the immutable
AI-adjudicated reference file. A frozen prediction set is never overwritten or selectively retried.
A changed model, prompt, taxonomy, or generation contract requires a separately versioned
experiment. The generic evaluation ledger keeps RegEx and LLM runs separate and the LLM detail row
records model, prompt, generation, reference, prediction, comparison, and runtime provenance.

Latency and Ollama token timing come from the first inference responses. Ollama model residency and
VRAM allocation come from its private `/api/ps` response; adapter RAM is peak process RSS. GPU
utilization is deliberately unavailable inside the hardened JSM container rather than granting it
host or Docker-socket access. An operator may provide the evaluation directory with a validated
`llm-holdout-resource-observation-v2.json` generated from external `nvidia-smi` and `docker stats`
sampling during the run. The evaluator reads that observation only after predictions are frozen, so
resource measurement cannot change predictions.

## Practical interpretation

The old historical macro F1 near `0.9976` answers “did the detector preserve expected behavior on the original eight-concept known-case subset?” It does not answer “how accurate is this on future production jobs?” The broader curated corpus is also development/regression evidence. A holdout instead asks how the frozen detector behaves on a random production sample that did not influence its construction.

Adjudication means a third blinded review of A/B disagreements; it does not mean choosing whichever answer makes RegEx look better. Leakage means information from predictions or failures reached labeling or rule design. Contamination means a nominal test item influenced development and can no longer support an unbiased claim.
