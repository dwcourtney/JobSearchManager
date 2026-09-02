# High-recall Job Fit triage experiment

This experiment asks only whether a posting is plausibly worth the cost of the full Job Fit LLM. It does not attempt to reproduce the 85-concept classifier and it does not change production ingestion or Job Fit behavior.

## Frozen reference

The v2 binary reference is a deterministic, conservative derivation from the already-frozen, prediction-blinded AI-adjudicated production references. Its definition and concept sets are fixed in `TriageEvaluationService` before a candidate is scored.

- A posting is **obviously irrelevant** when a frozen hard-conflict concept is present, or when a frozen physical/manual occupation concept is present with no present or unresolved technical role/skill concept.
- A posting is **worth sending** when a frozen technical role/skill concept is present or unresolved.
- Every other posting is an **ambiguous keep** and counts as worth sending.
- Generic responsibility concepts are not treated as technical-wheelhouse evidence.
- RegEx and triage predictions are never reference inputs.

The generated `triage-reference-labels-v2.json` records the source-reference fingerprint, definition fingerprint, per-posting basis, freeze time, and its own fingerprint. If the immutable artifact already exists, reruns validate it rather than overwrite it.

The discarded v1 derivation is retained only as audit evidence. It was rejected before candidate scoring because the generic `hands-on implementation` concept is not specific to technical work.

Reference labels were generated through prediction-blinded AI review and adjudication. They are not human-ground-truth labels.

## Candidate funnel

Stage 1 looks for explicit away/shipboard/diving conflicts and strongly physical/manual occupational titles. Technical-bucket evidence protects physical-sounding titles except for explicit hard conflicts. Stage 2 rejects only a small set of confidently nontechnical occupational titles when no broad technical evidence exists. All other postings survive.

The ten broad evidence buckets cover software/application development; cloud/platform/DevOps; systems/infrastructure/administration; automation/scripting; IT networking; cybersecurity; data/AI/ML; test/validation automation; technical architecture/integration; and technical support/operations.

Each rejection has a stable stage and human-readable reason. The candidate fingerprint covers every registered pattern, reason, bucket name, and bucket definition. The implementation has no model, network, GPU, Qwen, or 85-concept RegEx dependency.

## Evaluation discipline

The candidate receives title and posting text only. References are opened only after all candidate decisions are made. Reports record per-stage inputs, rejections, survivors, recall loss, false negatives, latency, rejection precision, final workload reduction, and measured GTX 1070/RTX 5080 cost projections for 1,000 postings.

The production holdout is one-shot evidence. A candidate must not be tuned against false-negative examples from this report and rescored on the same holdout as though the result were unseen. Any later candidate should be developed on a separate triage-development set and evaluated on newly frozen independent evidence.
