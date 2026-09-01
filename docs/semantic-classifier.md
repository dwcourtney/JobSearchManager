# Semantic Job Fit classifiers

## Default: DeBERTa

Automatic Job Fit screening uses `cross-encoder/nli-deberta-v3-base` at immutable revision
`6c749ce3425cd33b46d187e45b92bbf96ee12ec7`. The pinned weights digest is
`sha256:d8148c6d49e0a7925134294c56326c71fe0ab1dc390e37355e00c7efbb488afa`.

The recovered Phase 2B NLI configuration uses 384-token chunks with 64-token overlap, maximum
entailment aggregation across chunks, eight-concept GPU batches, a 0.5 match threshold, and dynamic entailment/contradiction
label discovery. Configuration `deberta-85-nli-v1` applies the same definition-based hypothesis
template to every one of the 85 canonical concepts. Its configuration fingerprint includes model
identity/revision/digest, chunking, maximum sequence length, threshold, hypothesis template, and
taxonomy fingerprint.

Classification is bounded and asynchronous, so source ingestion never waits for model inference.
Current/new jobs and newer posting dates are processed first. A valid persisted result is reused
when posting content, taxonomy, model, and configuration fingerprints are unchanged. Otherwise the
UI displays **Job Fit TBD**, never a synthetic 5/10 baseline. Failure leaves deterministic salary,
clearance, credentials, education, citizenship, location, browsing, and accounts functional.

Persisted default results include posting content hash, taxonomy version/fingerprint, model
identity/revision/digest, classifier configuration version/fingerprint, classification timestamp,
classification fingerprint, confidence score, and match decision for all 85 concepts.

## Optional: Qwen/Ollama

`qwen3:4b-instruct-2507-q4_K_M` is retained on the internal classifier network only for the
explicit **Deep Analyze with Qwen** action. It is never scheduled by ingestion or Admin backfill.
Qwen results are stored separately with their own posting hash, model provenance, timestamp, and
analysis text. They never replace the DeBERTa classification or silently change the Job Fit score.

## Quality boundary

The historical DeBERTa evaluation covered an eight-concept subset. The production adapter now
supports the complete canonical taxonomy structurally, but the definition-derived hypotheses for
the remaining concepts have not received equivalent per-concept calibration. Scores should be
treated as screening signals, especially for subtle work-environment and responsibility-shape
labels. The implementation intentionally uses one general template and does not add fixture-specific
prompt exceptions.
