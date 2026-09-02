# Semantic Job Fit classifiers

## Default: lifecycle-managed RegEx

Automatic Job Fit screening is deterministic and runs inside JSM. The classifier loads active and
review-due rules from SQLite, applies the recovered title evidence, exclusions, local negation,
required-context groups, remote signals, and extended-location signals, and emits matches against the
canonical 85-concept taxonomy.

Each persisted result includes posting content hash, ruleset fingerprint, taxonomy fingerprint,
classification fingerprint, timestamp, and all concept predictions. A cache entry is current only
when every output-affecting fingerprint still matches. Classification remains asynchronous for cache
backfill, but each individual RegEx evaluation is local and bounded.

The development/regression corpus contains 148 curated fixtures and 1,740 concept decisions. The exact historical
eight-concept benchmark is preserved:

| Aggregate | Precision | Recall | F1 |
| --- | ---: | ---: | ---: |
| Macro | 0.995370370370370 | 1.000000000000000 | 0.997641509433962 |
| Micro | 0.993865030674847 | 1.000000000000000 | 0.996923076923077 |

This is the **CURATED REGRESSION BENCHMARK**, not production accuracy. Across every labeled concept
in the corpus, macro F1 is `0.894597869692249` and micro F1 is
`0.915770609318996`. These are fixed-corpus point metrics, not a precision-recall curve. A curve would
be misleading because deterministic boolean rules have no calibrated threshold continuum.

Evaluation reports include per-concept TP/FP/FN/TN and precision/recall/F1, plus per-rule match count,
true/false positives, unique/redundant true positives, and representative examples. Evaluation calls
are explicitly non-production usage and do not alter rule telemetry.

## Dormant: Qwen/Ollama

`qwen3:4b-instruct-2507-q4_K_M` remains installed for possible later restoration, but JSM exposes no
normal UI or HTTP execution route for it. It is never scheduled by ingestion or RegEx backfill.
Historical requests may persist as queued, running, completed, or failed;
duplicate work with the same posting/model fingerprint is coalesced. Results retain posting hash,
model digest, taxonomy and prompt provenance, classification fingerprint, timestamp, all 85 concept
predictions, and the analysis text.

The adapter exposes only `/healthz` and `/deep-analyze`. The former DeBERTa `/classify` endpoint,
weights, model mount, CUDA runtime, and automatic model inference are intentionally absent.

## Quality boundary

The historical subset is a development/regression contract, not proof of generalization to all possible job
language. Candidate changes must be tested on the versioned fixed corpus before validation, then
separately approved for activation. Production usage telemetry and review-due rules provide the
evidence for ongoing maintenance; additions to the corpus should be reviewed and versioned rather
than silently tailored to one posting.
