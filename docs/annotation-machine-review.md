# Annotation corpus exchange

The Admin area separates **Human Labeling**, a bounded one-card review workflow, from **Machine Labeling**, the append-only corpus and JSONL exchange workflow. Both remain protected by server-side administrator authorization.

## Recommended workflow

1. In Admin > Machine Labeling, optionally add corpus items with **Items to add** or **Add all eligible**. Corpus generation count is independent of review export count and never rebuilds existing data.
2. Under **Machine review batch**, choose a queue and **Items to export** (default 100), then select **Export batch**. **Export all matching** is a separate explicit action. Company and concept filters also apply.
3. Give the compact JSONL to ChatGPT, Codex, Qwen, or an offline reviewer. Return only explicit `review` records for items actually reviewed; preserve `batchId`, `annotationItemId`, `contentHash`, and `taxonomyFingerprint`; use only canonical IDs supplied in the input; do not invent confidence.
4. Upload the returned JSONL with **Import machine review JSONL**.
5. Review **Machine disagreements**, **Human-unreviewed machine labels**, **Unsure / ambiguous**, and **Rare concepts** in the one-card UI.
6. In Admin > Human Labeling, review one item at a time. Only authenticated decisions made in JSM become human-reviewed or human-overridden training-eligible labels.

Partial and shuffled returns are supported. Omitted batch members remain unchanged and are reported as remaining. The default `neverMachineReviewed` queue excludes an item after its first valid machine opinion. `machineReviewed` supports a second independent reviewer; `machineDisagreement`, `humanUnreviewedMachine`, and `unsure` expose exception queues.

## Compact batch format (interchange schema version 3)

Each persisted batch records its unique ID, UTC export time, taxonomy fingerprint, queue and filters, requested and actual counts, ordering version, ordered annotation item IDs and digest, and interchange version. Selection is reproducible against unchanged review state. Ordering is `review-priority-created-id-v1`: human/machine conflicts, machine disagreements, unsure items, human-unreviewed machine labels, never-reviewed items, then the remainder; ties use creation time and ordinal item ID.

The first JSONL line is one `batch` header with the canonical catalog exactly once. It is followed by one `source` record per distinct source (including the full posting exactly once) and one `item` record per selected annotation referencing `sourceId`. This removes repeated catalog and posting payload from the scalable review path.

Return records use the following shape:

```json
{"recordType":"review","schemaVersion":3,"batchId":"machine-batch-0123...","annotationItemId":"annotation-0123...","contentHash":"64-hex...","taxonomyFingerprint":"64-hex...","decision":"different-label","selectedConceptIds":["technical.backend-development"],"reviewerType":"chatgpt","reviewerIdentity":"model/version if known","confidence":0.82,"rationale":"Short optional rationale","reviewedUtc":"2026-09-01T12:00:00Z"}
```

The importer treats `batch`, `source`, and `item` context records as inert and changes only explicit review records. A supplied batch must exist and contain the exact item. Exact item ID, content hash, and taxonomy fingerprint matching still applies. Missing records never imply completion.

## Verbose archival export (schema version 2)

Every line is self-contained and includes the stable annotation/source IDs, company, title, URL, content hash, exact evidence and context, full posting, taxonomy version/fingerprint, the complete compact canonical concept catalog, detailed evidence guidance for relevant concepts, deterministic proposal provenance, imported machine opinions, authenticated human state, and timestamps.

```json
{"schemaVersion":2,"annotationItemId":"annotation-0123...","sourceJobId":"company:REQ-1","company":"company","title":"Platform Engineer","contentHash":"64-hex...","taxonomyFingerprint":"64-hex...","evidence":"Kubernetes","surroundingContext":"...","fullPosting":"...","suggestedConceptIds":["technical.containers"],"canonicalConceptDefinitions":[{"id":"technical.containers","displayName":"Containers","category":"Technical","definition":"Canonical JSM Technical concept: Containers."}],"machineProposal":{"method":"jsm-deterministic-concept-extraction"},"currentReview":{"status":"unreviewed","machineReviews":[],"trainingEligible":false}}
```

These clearly secondary archival exports remain available for `all`, `reviewed`, `unreviewed`, `unsure`, and `trainingEligible`. They can be restricted to a known batch ID for exact comparison. Unspecified taxonomy labels are never exported as negatives.

## Machine-review import record

```json
{"annotationItemId":"annotation-0123...","contentHash":"64-hex...","taxonomyFingerprint":"64-hex...","decision":"different-label","selectedConceptIds":["technical.backend-development"],"reviewerType":"chatgpt","reviewerIdentity":"gpt model/version if known","confidence":0.82,"rationale":"Short optional rationale","reviewedUtc":"2026-09-01T12:00:00Z"}
```

Required fields are `annotationItemId`, `contentHash`, `taxonomyFingerprint`, `decision`, and `reviewerType`. Confidence, rationale, identity, timestamp, and selected concepts are optional when the decision does not require them.

Supported decisions are `correct`, `incorrect`, `different-label`, `multiple-labels`, `none`, and `unsure`/`skip`. Supported reviewer types are `qwen`, `chatgpt`, `codex`, and `other-machine`. Imports claiming human provenance are rejected.

## Precedence and conflicts

- Deterministic proposals and imported machine reviews are opinions, never gold.
- Exact machine-review reimports are unchanged/idempotent.
- Distinct machine opinions are preserved. Disagreement is surfaced and remains training-ineligible.
- A machine opinion never overwrites an authenticated human decision.
- A later machine opinion that conflicts with existing human truth is retained as disagreement evidence and temporarily excludes the item from training.
- Reconfirming the item in the authenticated UI records `human-overridden` and resolves eligibility.
- Unsure/skip and unresolved conflicts remain excluded.
- Stale taxonomy fingerprints, changed content hashes, unknown items/concepts, invalid UTF-8, oversized records/files, and forged human provenance are rejected with category counts.

Imports are parsed and validated in memory, applied to a consistent corpus, and persisted with one atomic store write. Import audit summaries and bounded rejection metadata are retained; uploaded paths and executable content are never used.
