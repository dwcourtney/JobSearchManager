# Annotation corpus exchange

The Admin **Labeling** page supports a bounded, one-card human workflow backed by a corpus that can contain thousands of items. Generation appends deterministic candidates and never rebuilds or removes existing decisions.

## Recommended workflow

1. In Admin > Labeling, choose a target corpus size or **All eligible**, optionally select a company or concept, and generate.
2. Choose **Export all JSONL** or **Export unreviewed**. The selected company and concept filters also apply to export.
3. Give the JSONL to ChatGPT, Codex, Qwen, or an offline reviewer with this instruction: return exactly one JSON object per input line using the machine-review result shape below; preserve `annotationItemId`, `contentHash`, and `taxonomyFingerprint`; use only canonical IDs supplied in the input; do not invent confidence.
4. Upload the returned JSONL with **Import machine review JSONL**.
5. Review **Machine disagreements**, **Human-unreviewed machine labels**, **Unsure / ambiguous**, and **Rare concepts** in the one-card UI.
6. Only authenticated decisions made in JSM become human-reviewed or human-overridden training-eligible labels.

## Export record (schema version 2)

Every line is self-contained and includes the stable annotation/source IDs, company, title, URL, content hash, exact evidence and context, full posting, taxonomy version/fingerprint, the complete compact canonical concept catalog, detailed evidence guidance for relevant concepts, deterministic proposal provenance, imported machine opinions, authenticated human state, and timestamps.

```json
{"schemaVersion":2,"annotationItemId":"annotation-0123...","sourceJobId":"company:REQ-1","company":"company","title":"Platform Engineer","contentHash":"64-hex...","taxonomyFingerprint":"64-hex...","evidence":"Kubernetes","surroundingContext":"...","fullPosting":"...","suggestedConceptIds":["technical.containers"],"canonicalConceptDefinitions":[{"id":"technical.containers","displayName":"Containers","category":"Technical","definition":"Canonical JSM Technical concept: Containers."}],"machineProposal":{"method":"jsm-deterministic-concept-extraction"},"currentReview":{"status":"unreviewed","machineReviews":[],"trainingEligible":false}}
```

Exports are available for `all`, `reviewed`, `unreviewed`, `unsure`, and `trainingEligible`. Unspecified taxonomy labels are never exported as negatives.

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
